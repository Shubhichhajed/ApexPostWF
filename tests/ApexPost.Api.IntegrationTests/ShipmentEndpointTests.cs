using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ApexPost.Api.IntegrationTests.Infrastructure;
using Xunit;

namespace ApexPost.Api.IntegrationTests;

/// <summary>
/// Full HTTP pipeline (routing, model validation, authorization, exception handler, EF Core, SQL Server).
/// One database per test class; each test uses its own tracking numbers so tests never share rows.
/// </summary>
public sealed class ShipmentEndpointTests(ApiTestHost host) : IClassFixture<ApiTestHost>
{
    private const string Admin = "Admin";
    private const string Courier = "Courier:courier-demo-01";

    [Fact]
    public async Task PostShipment_AsAdmin_Returns201WithLocationAndNestedRecipient()
    {
        // Arrange
        using var client = host.CreateClientAs(Admin);

        // Act
        using var response = await client.PostAsJsonAsync("api/v2/shipments", NewShipment("APX-40000001"));

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.EndsWith("/api/v2/shipments/APX-40000001", response.Headers.Location?.ToString());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Created", body.GetProperty("status").GetString());
        Assert.Equal("Springfield", body.GetProperty("recipient").GetProperty("city").GetString());
    }

    [Fact]
    public async Task PostShipment_DuplicateTrackingNumber_Returns409ProblemDetails()
    {
        // Arrange
        using var client = host.CreateClientAs(Admin);
        (await client.PostAsJsonAsync("api/v2/shipments", NewShipment("APX-40000002"))).EnsureSuccessStatusCode();

        // Act
        using var response = await client.PostAsJsonAsync("api/v2/shipments", NewShipment("APX-40000002"));

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData(0.09)]
    [InlineData(70.01)]
    public async Task PostShipment_WeightOutsideLimits_Returns400ValidationProblemDetails(double weight)
    {
        // Arrange
        using var client = host.CreateClientAs(Admin);
        var request = NewShipment("APX-40000003") with { Weight = (decimal)weight };

        // Act
        using var response = await client.PostAsJsonAsync("api/v2/shipments", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty("Weight", out _));
    }

    [Fact]
    public async Task GetShipment_Anonymous_Returns401()
    {
        // Arrange
        using var client = host.CreateClientAs(null);

        // Act
        using var response = await client.GetAsync("api/v2/shipments/APX-40000001");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeleteShipment_AsCourier_Returns403()
    {
        // Arrange
        using var client = host.CreateClientAs(Courier);

        // Act
        using var response = await client.DeleteAsync("api/v2/shipments/APX-40000001");

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UploadScans_SameScanIdTwice_SecondUploadIsCountedAsDuplicate()
    {
        // Arrange
        using var admin = host.CreateClientAs(Admin);
        (await admin.PostAsJsonAsync("api/v2/shipments", NewShipment("APX-40000004"))).EnsureSuccessStatusCode();
        using var courier = host.CreateClientAs(Courier);
        var upload = new { scans = new[] { NewScan("scan::7c2a4d3f-9b5e-4a8c-8d2f-3e4a5b6c7d01") } };
        (await courier.PostAsJsonAsync("api/v2/shipments/APX-40000004/scans", upload)).EnsureSuccessStatusCode();

        // Act
        using var response = await courier.PostAsJsonAsync("api/v2/shipments/APX-40000004/scans", upload);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, body.GetProperty("accepted").GetInt32());
        Assert.Equal(1, body.GetProperty("duplicatesSkipped").GetInt32());
        using var history = await courier.GetFromJsonAsync<JsonDocument>("api/v2/shipments/APX-40000004/scans");
        Assert.Equal(1, history!.RootElement.GetProperty("scans").GetArrayLength());
    }

    [Fact]
    public async Task UploadScans_ForAnotherCourier_Returns403()
    {
        // Arrange
        using var admin = host.CreateClientAs(Admin);
        (await admin.PostAsJsonAsync("api/v2/shipments", NewShipment("APX-40000005"))).EnsureSuccessStatusCode();
        using var courier = host.CreateClientAs("Courier:someone-else");
        var upload = new { scans = new[] { NewScan("scan::7c2a4d3f-9b5e-4a8c-8d2f-3e4a5b6c7d02") } };

        // Act
        using var response = await courier.PostAsJsonAsync("api/v2/shipments/APX-40000005/scans", upload);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PatchStatus_DeliveredBackToInTransit_Returns409()
    {
        // Arrange: walk the shipment through its whole lifecycle
        using var admin = host.CreateClientAs(Admin);
        (await admin.PostAsJsonAsync("api/v2/shipments", NewShipment("APX-40000006"))).EnsureSuccessStatusCode();
        using var courier = host.CreateClientAs(Courier);
        foreach (var status in new[] { "PickedUp", "InTransit", "OutForDelivery", "Delivered" })
            (await courier.PatchAsJsonAsync("api/v2/shipments/APX-40000006/status", new { status })).EnsureSuccessStatusCode();

        // Act
        using var response = await courier.PatchAsJsonAsync("api/v2/shipments/APX-40000006/status", new { status = "InTransit" });

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task GetShipment_V1_ReturnsFlatRecipientAndReportsDeprecation()
    {
        // Arrange
        using var admin = host.CreateClientAs(Admin);
        (await admin.PostAsJsonAsync("api/v2/shipments", NewShipment("APX-40000007"))).EnsureSuccessStatusCode();

        // Act
        using var response = await admin.GetAsync("api/v1/shipments/APX-40000007");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Alex Morgan", body.GetProperty("recipientName").GetString());
        Assert.True(response.Headers.TryGetValues("api-deprecated-versions", out var deprecated));
        Assert.Contains("1.0", string.Join(",", deprecated));
    }

    [Fact]
    public async Task GetPartnerShipment_WrongApiKey_Returns401()
    {
        // Arrange
        using var client = host.CreateClientAs(null);
        client.DefaultRequestHeaders.Add("X-Api-Key", "wrong-key");

        // Act
        using var response = await client.GetAsync("api/v2/partner/shipments/APX-40000001");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static ShipmentRequest NewShipment(string trackingNumber) => new(
        trackingNumber, 2.5m, "Alex Morgan", "+44 20 7946 0958", "100 Example Avenue", "Springfield", null);

    private static object NewScan(string scanId) => new
    {
        scanId, scanType = "Pickup", courierId = "courier-demo-01", location = "Springfield Depot",
        scannedAtUtc = DateTime.UtcNow.AddMinutes(-5)
    };

    private sealed record ShipmentRequest(string TrackingNumber, decimal Weight, string RecipientName, string RecipientPhone,
        string RecipientAddress, string RecipientCity, string? Notes);
}
