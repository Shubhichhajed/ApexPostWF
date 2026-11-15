using ApexPost.Api.IntegrationTests.Infrastructure;
using ApexPost.Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ApexPost.Api.IntegrationTests;

/// <summary>
/// Repository-level test of the API's scan store against real SQL Server. Scan de-duplication relies on
/// a primary key and a serializable transaction, which an in-memory provider or a mocked DbSet cannot reproduce.
/// </summary>
public sealed class ShipmentMetadataStoreTests(ApiTestHost host) : IClassFixture<ApiTestHost>
{
    [Fact]
    public async Task TryAddScanAsync_NewScanId_ReturnsInserted()
    {
        // Arrange
        await using var scope = host.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<ShipmentMetadataStore>();
        await store.SaveAsync(new ShipmentMetadata { TrackingNumber = "APX-49000001", RecipientCity = "Springfield" });

        // Act
        var result = await store.TryAddScanAsync("APX-49000001", Scan("scan::00000000-0000-4000-8000-000000000101"));

        // Assert
        Assert.Equal(ScanInsertResult.Inserted, result);
    }

    [Fact]
    public async Task TryAddScanAsync_SameScanIdForSameShipment_ReturnsDuplicateAndStoresOneRow()
    {
        // Arrange
        await using var scope = host.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<ShipmentMetadataStore>();
        await store.SaveAsync(new ShipmentMetadata { TrackingNumber = "APX-49000002", RecipientCity = "Springfield" });
        await store.TryAddScanAsync("APX-49000002", Scan("scan::00000000-0000-4000-8000-000000000102"));

        // Act
        var result = await store.TryAddScanAsync("APX-49000002", Scan("scan::00000000-0000-4000-8000-000000000102"));

        // Assert
        Assert.Equal(ScanInsertResult.Duplicate, result);
        var stored = await store.GetAsync("APX-49000002");
        Assert.Single(stored!.Scans);
    }

    [Fact]
    public async Task TryAddScanAsync_ScanIdAlreadyUsedByAnotherShipment_ReturnsIdBelongsToAnotherShipment()
    {
        // Arrange
        await using var scope = host.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<ShipmentMetadataStore>();
        await store.SaveAsync(new ShipmentMetadata { TrackingNumber = "APX-49000003", RecipientCity = "Springfield" });
        await store.SaveAsync(new ShipmentMetadata { TrackingNumber = "APX-49000004", RecipientCity = "Springfield" });
        await store.TryAddScanAsync("APX-49000003", Scan("scan::00000000-0000-4000-8000-000000000103"));

        // Act
        var result = await store.TryAddScanAsync("APX-49000004", Scan("scan::00000000-0000-4000-8000-000000000103"));

        // Assert
        Assert.Equal(ScanInsertResult.IdBelongsToAnotherShipment, result);
    }

    [Fact]
    public async Task TryAddScanAsync_NoMetadataForShipment_ReturnsShipmentMetadataMissing()
    {
        // Arrange
        await using var scope = host.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<ShipmentMetadataStore>();

        // Act
        var result = await store.TryAddScanAsync("APX-49000099", Scan("scan::00000000-0000-4000-8000-000000000104"));

        // Assert
        Assert.Equal(ScanInsertResult.ShipmentMetadataMissing, result);
    }

    private static ApiScanRecord Scan(string scanId) => new()
    {
        ScanId = scanId, ScanType = "Pickup", CourierId = "courier-demo-01", Location = "Depot",
        ScannedAtUtc = new DateTime(2026, 11, 15, 9, 0, 0, DateTimeKind.Utc)
    };
}
