using ApexPost.Courier.Offline;

namespace ApexPost.Specs.Support;

/// <summary>
/// The courier device's local store for the sync scenarios. Couchbase Lite itself is covered by
/// ApexPost.IntegrationTests; here the interest is the outbox talking to the real API.
/// </summary>
public sealed class InMemoryScanRepository : IScanRepository
{
    private readonly List<ShipmentScan> _scans = [];

    public IReadOnlyList<ShipmentScan> All => _scans;

    public Task<ShipmentScan> CreateAsync(ShipmentScan scan)
    {
        scan.SyncStatus = SyncStatus.Pending;
        _scans.Add(scan);
        return Task.FromResult(scan);
    }

    public Task<ShipmentScan?> GetByIdAsync(string id) => Task.FromResult(_scans.SingleOrDefault(x => x.Id == id));
    public Task<ShipmentScan> UpdateAsync(ShipmentScan scan) => Task.FromResult(scan);

    public Task DeleteAsync(string id)
    {
        _scans.RemoveAll(x => x.Id == id);
        return Task.CompletedTask;
    }

    public Task<IEnumerable<ShipmentScan>> ListAllAsync() => Task.FromResult<IEnumerable<ShipmentScan>>(_scans.ToArray());

    public Task<IEnumerable<ShipmentScan>> GetScansByTrackingNumberAsync(string trackingNumber)
        => Task.FromResult<IEnumerable<ShipmentScan>>(_scans.Where(x => x.TrackingNumber == trackingNumber).ToArray());

    public Task<IEnumerable<ShipmentScan>> GetPendingScansAsync()
        => Task.FromResult<IEnumerable<ShipmentScan>>(_scans.Where(x => x.SyncStatus == SyncStatus.Pending).ToArray());

    public Task<Dictionary<ScanType, int>> GetScanCountByCourierAsync(string courierId)
        => Task.FromResult(_scans.Where(x => x.CourierId == courierId).GroupBy(x => x.ScanType).ToDictionary(x => x.Key, x => x.Count()));

    public Task<IEnumerable<ShipmentScan>> GetScansByCourierAsync(string courierId)
        => Task.FromResult<IEnumerable<ShipmentScan>>(_scans.Where(x => x.CourierId == courierId).ToArray());
}
