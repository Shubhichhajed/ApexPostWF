using System.Net;
using System.Text;
using ApexPost.Courier.Api;
using ApexPost.Courier.Auth;
using ApexPost.Courier.Resilience;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Moq.Protected;
using Polly.Timeout;
using Xunit;

namespace ApexPost.Courier.Tests;

/// <summary>
/// The Week 4 Part D typed client against a mocked HttpMessageHandler (Moq.Protected): no socket is ever opened.
/// What this does NOT prove: that the real API returns these shapes (contract drift) - that is what the
/// ScanSync specs run against the in-memory API host.
/// </summary>
public sealed class ApexApiClientHttpTests
{
    private static readonly Uri BaseAddress = new("https://api.test/");
    private readonly Mock<HttpMessageHandler> _handler = new();

    [Fact]
    public async Task GetShipmentAsync_Response200WithJson_ReturnsMappedShipment()
    {
        // Arrange
        RespondWith(HttpStatusCode.OK,
            """{"trackingNumber":"APX-12345678","status":"OutForDelivery","weight":2.5,"recipient":{"name":"Alex Morgan","city":"Springfield"}}""");
        var client = CreateClient();

        // Act
        var shipment = await client.GetShipmentAsync("APX-12345678");

        // Assert
        Assert.NotNull(shipment);
        Assert.Equal(ShipmentStatus.OutForDelivery, shipment.Status);
        Assert.Equal("Springfield", shipment.Recipient?.City);
        _handler.Protected().Verify<Task<HttpResponseMessage>>("SendAsync", Times.Once(),
            ItExpr.Is<HttpRequestMessage>(r => r.Method == HttpMethod.Get
                && r.RequestUri!.AbsolutePath == "/api/v2/shipments/APX-12345678"),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task GetShipmentAsync_Response404_ReturnsNull()
    {
        // Arrange
        RespondWith(HttpStatusCode.NotFound, """{"title":"Shipment not found","status":404}""", "application/problem+json");
        var client = CreateClient();

        // Act
        var shipment = await client.GetShipmentAsync("APX-00000000");

        // Assert
        Assert.Null(shipment);
    }

    [Fact]
    public async Task GetShipmentAsync_Response500_ThrowsNonPermanentApiProblemException()
    {
        // Arrange
        RespondWith(HttpStatusCode.InternalServerError, """{"title":"An unexpected error occurred","status":500,"traceId":"00-1"}""",
            "application/problem+json");
        var client = CreateClient();

        // Act
        var act = () => client.GetShipmentAsync("APX-12345678");

        // Assert
        var error = await Assert.ThrowsAsync<ApiProblemException>(act);
        Assert.Equal(HttpStatusCode.InternalServerError, error.StatusCode);
        Assert.False(error.IsPermanent); // 5xx may succeed later, so scans stay Pending
    }

    [Fact]
    public async Task UploadScansAsync_ValidationProblemDetails_ThrowsPermanentErrorWithServerMessage()
    {
        // Arrange
        string? sentBody = null;
        _handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.Method == HttpMethod.Post),
                ItExpr.IsAny<CancellationToken>())
            .Returns(async (HttpRequestMessage request, CancellationToken token) =>
            {
                sentBody = await request.Content!.ReadAsStringAsync(token);
                return Json(HttpStatusCode.BadRequest,
                    """{"title":"One or more validation errors occurred.","status":400,"errors":{"Scans[0].Notes":["A Delivered scan requires the recipient signature/name in Notes."]}}""",
                    "application/problem+json");
            });
        var client = CreateClient();
        var body = new ScanUploadRequestDto
        {
            Scans = [new ScanItemDto { ScanId = "scan::3f2504e0-4f89-11d3-9a0c-0305e82c3301", ScanType = "Delivered",
                CourierId = "courier-demo-01", Location = "Front door", ScannedAtUtc = new DateTime(2026, 11, 15, 9, 0, 0, DateTimeKind.Utc) }]
        };

        // Act
        var act = () => client.UploadScansAsync("APX-12345678", body);

        // Assert
        var error = await Assert.ThrowsAsync<ApiProblemException>(act);
        Assert.True(error.IsPermanent);
        Assert.Equal("The server rejected the data: A Delivered scan requires the recipient signature/name in Notes.", error.Message);
        Assert.NotNull(sentBody);
        Assert.Contains("\"scanId\":\"scan::3f2504e0-4f89-11d3-9a0c-0305e82c3301\"", sentBody);
    }

    [Fact]
    public async Task GetShipmentAsync_CallerCancels_ThrowsOperationCanceledException()
    {
        // Arrange
        _handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Returns((HttpRequestMessage _, CancellationToken token) => Task.FromCanceled<HttpResponseMessage>(token));
        var client = CreateClient();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act
        var act = () => client.GetShipmentAsync("APX-12345678", cancellation.Token);

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(act);
    }

    [Fact]
    public async Task GetShipmentAsync_ServerNeverAnswers_AttemptTimeoutFiresAndIsRetriedOnce()
    {
        // Arrange: the handler waits until it is cancelled, like a server that accepted the connection and hung.
        _handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Returns(async (HttpRequestMessage _, CancellationToken token) =>
            {
                await Task.Delay(Timeout.Infinite, token);
                return new HttpResponseMessage(HttpStatusCode.OK);
            });
        await using var provider = BuildResilientProvider();
        var client = provider.GetRequiredService<ApexApiClient>();

        // Act
        var act = () => client.GetShipmentAsync("APX-12345678");

        // Assert
        await Assert.ThrowsAsync<TimeoutRejectedException>(act);
        _handler.Protected().Verify<Task<HttpResponseMessage>>("SendAsync", Times.Exactly(2),
            ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
    }

    private ApexApiClient CreateClient() => new(new HttpClient(_handler.Object) { BaseAddress = BaseAddress });

    private void RespondWith(HttpStatusCode status, string json, string mediaType = "application/json") =>
        _handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => Json(status, json, mediaType));

    private static HttpResponseMessage Json(HttpStatusCode status, string json, string mediaType = "application/json") =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, mediaType) };

    /// <summary>Real resilience pipeline with very short timeouts so the hang test finishes in well under a second.</summary>
    private ServiceProvider BuildResilientProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<ResilienceSettings>(settings =>
        {
            settings.MaxRetryAttempts = 1;
            settings.BaseDelaySeconds = 0.01;
            settings.AttemptTimeoutSeconds = 0.05;
            settings.TotalTimeoutSeconds = 2;
        });
        services.AddSingleton<ConnectionState>();
        services.AddSingleton<ITokenStore>(new InMemoryTokenStore("aaa.bbb.ccc"));
        services.AddSingleton<SessionEvents>();
        services.AddTransient<AuthHeaderHandler>();
        services.AddHttpClient<ApexApiClient>(client => client.BaseAddress = BaseAddress)
            .AddHttpMessageHandler<AuthHeaderHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => _handler.Object)
            .AddResilienceHandler(ApexApiResilience.PipelineName, ApexApiResilience.Configure);
        return services.BuildServiceProvider();
    }
}
