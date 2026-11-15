using System.Security.Claims;
using ApexPost.Api.Controllers;
using ApexPost.Api.Models;
using ApexPost.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Moq;
using Xunit;

namespace ApexPost.UnitTests.Api;

/// <summary>
/// Controller in isolation: IShipmentService is mocked, so these tests prove the HTTP mapping
/// (status codes, Location route values, the courierId ownership rule) and nothing about the database.
/// The end-to-end path is covered by ApexPost.Api.IntegrationTests.
/// </summary>
public sealed class ShipmentsV2ControllerTests
{
    private readonly Mock<IShipmentService> _service = new(MockBehavior.Strict);

    [Fact]
    public async Task GetShipment_UnknownTrackingNumber_Returns404ProblemDetails()
    {
        // Arrange
        _service.Setup(x => x.GetShipmentAsync("APX-00000000", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ShipmentDTO?)null);
        var controller = CreateController(Courier("courier-demo-01"));

        // Act
        var result = await controller.GetShipment("APX-00000000", CancellationToken.None);

        // Assert
        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
        Assert.IsType<ProblemDetails>(problem.Value);
    }

    [Fact]
    public async Task GetShipment_KnownTrackingNumber_Returns200WithNestedRecipientAndEnumStatus()
    {
        // Arrange
        _service.Setup(x => x.GetShipmentAsync("APX-10000001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Dto("APX-10000001", "InTransit"));
        var controller = CreateController(Courier("courier-demo-01"));

        // Act
        var result = await controller.GetShipment("APX-10000001", CancellationToken.None);

        // Assert
        var ok = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<ShipmentResponseV2>(ok.Value);
        Assert.Equal(ShipmentStatus.InTransit, body.Status);
        Assert.Equal("Springfield", body.Recipient.City);
    }

    [Fact]
    public async Task CreateShipment_ValidRequest_ReturnsCreatedAtRouteForGetShipmentV2()
    {
        // Arrange
        _service.Setup(x => x.CreateShipmentAsync("APX-10000002", 2.5m, "Alex Morgan", It.IsAny<string>(),
                It.IsAny<string>(), "Springfield", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Dto("APX-10000002", "Created"));
        var controller = CreateController(Admin());
        var request = new CreateShipmentRequest
        {
            TrackingNumber = "APX-10000002", Weight = 2.5m, RecipientName = "Alex Morgan",
            RecipientPhone = "+44 20 7946 0958", RecipientAddress = "100 Example Avenue", RecipientCity = "Springfield"
        };

        // Act
        var result = await controller.CreateShipment(request, CancellationToken.None);

        // Assert
        var created = Assert.IsType<CreatedAtRouteResult>(result);
        Assert.Equal("GetShipmentV2", created.RouteName);
        Assert.NotNull(created.RouteValues);
        Assert.Equal("APX-10000002", created.RouteValues["trackingNumber"]);
        Assert.Equal("2", created.RouteValues["version"]);
        _service.Verify(x => x.CreateShipmentAsync(It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task UploadScans_ScanForAnotherCourier_Returns403AndNeverCallsService()
    {
        // Arrange
        var controller = CreateController(Courier("courier-demo-01"));
        var request = new ScanUploadRequest { Scans = [Scan("scan::3f2504e0-4f89-11d3-9a0c-0305e82c3301", "someone-else")] };

        // Act
        var result = await controller.UploadScans("APX-10000001", request, CancellationToken.None);

        // Assert
        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
        _service.Verify(x => x.UploadScansAsync(It.IsAny<string>(), It.IsAny<List<OfflineScanUpload>>(), It.IsAny<CancellationToken>()),
            Times.Never());
    }

    [Fact]
    public async Task UploadScans_OwnScans_PassesEveryScanToServiceAndReturnsCounts()
    {
        // Arrange
        const string scanId = "scan::3f2504e0-4f89-11d3-9a0c-0305e82c3301";
        _service.Setup(x => x.UploadScansAsync("APX-10000001",
                It.Is<List<OfflineScanUpload>>(scans => scans.Count == 1 && scans[0].ScanId == scanId && scans[0].CourierId == "courier-demo-01"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScanUploadResult { Accepted = 0, DuplicatesSkipped = 1 });
        var controller = CreateController(Courier("courier-demo-01"));
        var request = new ScanUploadRequest { Scans = [Scan(scanId, "courier-demo-01")] };

        // Act
        var result = await controller.UploadScans("APX-10000001", request, CancellationToken.None);

        // Assert
        var ok = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<ScanUploadResponse>(ok.Value);
        Assert.Equal(1, body.DuplicatesSkipped);
        _service.VerifyAll();
    }

    [Theory]
    [InlineData(0, 20)]  // page below 1
    [InlineData(1, 0)]   // pageSize below 1
    [InlineData(1, 51)]  // pageSize above the maximum of 50
    public async Task GetShipments_PagingOutOfRange_Returns400AndNeverQueries(int page, int pageSize)
    {
        // Arrange
        var controller = CreateController(Admin());

        // Act
        var result = await controller.GetShipments(page, pageSize, null, null, CancellationToken.None);

        // Assert
        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        _service.VerifyNoOtherCalls();
    }

    private ShipmentsV2Controller CreateController(ClaimsPrincipal user)
    {
        var httpContext = new DefaultHttpContext { User = user };
        var routeData = new RouteData(new RouteValueDictionary { ["version"] = "2" });
        return new ShipmentsV2Controller(_service.Object)
        {
            ControllerContext = new ControllerContext(new ActionContext(httpContext, routeData, new ControllerActionDescriptor())),
            ProblemDetailsFactory = new SimpleProblemDetailsFactory()
        };
    }

    private static ClaimsPrincipal Courier(string courierId) => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.Role, "Courier"), new Claim("courierId", courierId)], "Test"));

    private static ClaimsPrincipal Admin() => new(new ClaimsIdentity([new Claim(ClaimTypes.Role, "Admin")], "Test"));

    private static ScanItemRequest Scan(string scanId, string courierId) => new()
    {
        ScanId = scanId, ScanType = "Pickup", CourierId = courierId, Location = "Depot",
        ScannedAtUtc = new DateTime(2026, 11, 15, 9, 0, 0, DateTimeKind.Utc)
    };

    private static ShipmentDTO Dto(string trackingNumber, string status) => new()
    {
        Id = trackingNumber, TrackingNumber = trackingNumber, Status = status, Weight = 2.5m,
        RecipientName = "Alex Morgan", RecipientPhone = "+44 20 7946 0958", RecipientAddress = "100 Example Avenue",
        RecipientCity = "Springfield", CreatedAtUtc = new DateTime(2026, 11, 15, 8, 0, 0, DateTimeKind.Utc)
    };

    /// <summary>Minimal factory so ControllerBase.Problem() works without the full MVC service container.</summary>
    private sealed class SimpleProblemDetailsFactory : ProblemDetailsFactory
    {
        public override ProblemDetails CreateProblemDetails(HttpContext httpContext, int? statusCode = null, string? title = null,
            string? type = null, string? detail = null, string? instance = null)
            => new() { Status = statusCode ?? 500, Title = title, Type = type, Detail = detail, Instance = instance };

        public override ValidationProblemDetails CreateValidationProblemDetails(HttpContext httpContext, ModelStateDictionary modelStateDictionary,
            int? statusCode = null, string? title = null, string? type = null, string? detail = null, string? instance = null)
            => new(modelStateDictionary) { Status = statusCode ?? 400, Title = title, Type = type, Detail = detail, Instance = instance };
    }
}
