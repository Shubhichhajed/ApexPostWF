using System.Text.Json;
using ApexPost.Api.Middleware;
using ApexPost.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ApexPost.UnitTests.Api;

public sealed class GlobalExceptionHandlerTests
{
    [Theory]
    [InlineData(404, "Shipment not found")]
    [InlineData(409, "Shipment conflict")]
    [InlineData(400, "Invalid request")]
    public async Task TryHandleAsync_KnownException_WritesMatchingProblemDetails(int expectedStatus, string expectedTitle)
    {
        // Arrange
        Exception exception = expectedStatus switch
        {
            404 => new ShipmentNotFoundException("No matching shipment."),
            409 => new ShipmentConflictException("The transition is not allowed."),
            _ => new ArgumentException("Invalid weight.")
        };
        var (context, handler) = Create(Environments.Production);

        // Act
        var handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);

        // Assert
        Assert.True(handled);
        Assert.Equal(expectedStatus, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
        using var body = await ReadBodyAsync(context);
        Assert.Equal(expectedStatus, body.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(expectedTitle, body.RootElement.GetProperty("title").GetString());
        Assert.True(body.RootElement.TryGetProperty("traceId", out _));
    }

    [Fact]
    public async Task TryHandleAsync_UnexpectedExceptionInProduction_HidesMessage()
    {
        // Arrange
        var (context, handler) = Create(Environments.Production);

        // Act
        await handler.TryHandleAsync(context, new InvalidOperationException("secret stack detail"), CancellationToken.None);

        // Assert
        using var body = await ReadBodyAsync(context);
        Assert.Equal(500, body.RootElement.GetProperty("status").GetInt32());
        Assert.DoesNotContain("secret stack detail", body.RootElement.GetRawText());
    }

    [Fact]
    public async Task TryHandleAsync_UnexpectedExceptionInDevelopment_IncludesDetail()
    {
        // Arrange
        var (context, handler) = Create(Environments.Development);

        // Act
        await handler.TryHandleAsync(context, new InvalidOperationException("development diagnostic"), CancellationToken.None);

        // Assert
        using var body = await ReadBodyAsync(context);
        Assert.Equal("development diagnostic", body.RootElement.GetProperty("detail").GetString());
    }

    private static (DefaultHttpContext Context, GlobalExceptionHandler Handler) Create(string environment)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance,
            new TestHostEnvironment { EnvironmentName = environment });
        return (context, handler);
    }

    private static async Task<JsonDocument> ReadBodyAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;
        return await JsonDocument.ParseAsync(context.Response.Body);
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "ApexPost.UnitTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
