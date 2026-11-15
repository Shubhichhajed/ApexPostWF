using ApexPost.Courier.Offline;
using Xunit;

namespace ApexPost.IntegrationTests.Courier;

/// <summary>
/// CouchbaseScanRepository against a real embedded Couchbase Lite database in a temporary folder.
/// No server or manual setup is needed. This is the level that proves the SQL++ text, parameters,
/// indexes and document mapping; the outbox unit tests mock IScanRepository and cannot prove any of that.
/// Each test gets its own database folder, created in InitializeAsync and deleted in DisposeAsync.
/// </summary>
public sealed class CouchbaseScanRepositoryTests : IAsyncLifetime
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "apexpost-courier-tests", Guid.NewGuid().ToString("N"));
    private CourierDatabase _database = null!;
    private CouchbaseScanRepository _repository = null!;

    public async Task InitializeAsync()
    {
        _database = new CourierDatabase(directory: _folder);
        await _database.InitializeAsync();
        _repository = new CouchbaseScanRepository(_database);
    }

    public Task DisposeAsync()
    {
        _database.Dispose();
        try
        {
            if (Directory.Exists(_folder))
                Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // The native library can release file handles a moment after Dispose; a leftover temp folder is harmless.
        }
        return Task.CompletedTask;
    }

    [Fact]
    public async Task CreateAsync_NewScan_IsStoredAsPendingAndReadableByKey()
    {
        // Arrange
        var scan = Scan("APX-50000001", ScanType.Pickup, minutesAgo: 30);
        scan.SyncStatus = SyncStatus.Synced; // must be overwritten: new scans always start in the outbox

        // Act
        await _repository.CreateAsync(scan);
        var stored = await _repository.GetByIdAsync(scan.Id);

        // Assert
        Assert.NotNull(stored);
        Assert.Equal(SyncStatus.Pending, stored.SyncStatus);
        Assert.Equal("APX-50000001", stored.TrackingNumber);
        Assert.Equal(scan.ScannedAtUtc, stored.ScannedAtUtc);
    }

    [Fact]
    public async Task CreateAsync_IdNotInScanGuidFormat_ThrowsArgumentException()
    {
        // Arrange
        var scan = Scan("APX-50000001", ScanType.Pickup, minutesAgo: 1);
        scan.Id = "not-a-scan-id";

        // Act
        var act = () => _repository.CreateAsync(scan);

        // Assert
        await Assert.ThrowsAsync<ArgumentException>(act);
    }

    [Fact]
    public async Task CreateAsync_SameIdTwice_ThrowsInvalidOperationException()
    {
        // Arrange
        var scan = Scan("APX-50000001", ScanType.Pickup, minutesAgo: 1);
        await _repository.CreateAsync(scan);

        // Act
        var act = () => _repository.CreateAsync(scan);

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(act);
    }

    [Fact]
    public async Task GetScansByTrackingNumberAsync_MixedShipments_ReturnsOnlyThatShipmentOldestFirst()
    {
        // Arrange
        var later = await _repository.CreateAsync(Scan("APX-50000001", ScanType.InTransit, minutesAgo: 10));
        var earlier = await _repository.CreateAsync(Scan("APX-50000001", ScanType.Pickup, minutesAgo: 60));
        await _repository.CreateAsync(Scan("APX-59999999", ScanType.Pickup, minutesAgo: 5));

        // Act
        var scans = (await _repository.GetScansByTrackingNumberAsync("APX-50000001")).ToList();

        // Assert
        Assert.Equal(new[] { earlier.Id, later.Id }, scans.Select(x => x.Id));
    }

    [Fact]
    public async Task GetPendingScansAsync_ScanMarkedSynced_IsNoLongerReturned()
    {
        // Arrange
        var synced = await _repository.CreateAsync(Scan("APX-50000001", ScanType.Pickup, minutesAgo: 20));
        var pending = await _repository.CreateAsync(Scan("APX-50000001", ScanType.InTransit, minutesAgo: 10));
        synced.SyncStatus = SyncStatus.Synced;
        await _repository.UpdateAsync(synced);

        // Act
        var result = await _repository.GetPendingScansAsync();

        // Assert
        Assert.Equal(pending.Id, Assert.Single(result).Id);
    }

    [Fact]
    public async Task GetScanCountByCourierAsync_SeveralTypes_GroupsCountsByScanType()
    {
        // Arrange
        await _repository.CreateAsync(Scan("APX-50000001", ScanType.Pickup, minutesAgo: 30));
        await _repository.CreateAsync(Scan("APX-50000002", ScanType.Pickup, minutesAgo: 20));
        await _repository.CreateAsync(Scan("APX-50000001", ScanType.Delivered, minutesAgo: 10, notes: "Signed by A. Morgan"));
        await _repository.CreateAsync(Scan("APX-50000003", ScanType.Pickup, minutesAgo: 5, courierId: "courier-other"));

        // Act
        var counts = await _repository.GetScanCountByCourierAsync("courier-demo-01");

        // Assert
        Assert.Equal(2, counts[ScanType.Pickup]);
        Assert.Equal(1, counts[ScanType.Delivered]);
        Assert.False(counts.ContainsKey(ScanType.InTransit));
    }

    [Fact]
    public async Task GetScansByTrackingNumberAsync_ValueLooksLikeSqlInjection_IsTreatedAsData()
    {
        // Arrange
        await _repository.CreateAsync(Scan("APX-50000001", ScanType.Pickup, minutesAgo: 5));

        // Act: if the value were concatenated into the SQL++ text this would return every scan
        var scans = await _repository.GetScansByTrackingNumberAsync("x' OR '1'='1");

        // Assert
        Assert.Empty(scans);
    }

    [Fact]
    public async Task DeleteAsync_ExistingScan_RemovesDocument()
    {
        // Arrange
        var scan = await _repository.CreateAsync(Scan("APX-50000001", ScanType.Pickup, minutesAgo: 5));

        // Act
        await _repository.DeleteAsync(scan.Id);

        // Assert
        Assert.Null(await _repository.GetByIdAsync(scan.Id));
    }

    [Fact]
    public async Task ChangeListener_ScanCreated_ReportsChangedIdAndPendingCount()
    {
        // Arrange
        using var listener = new ChangeListenerService(_database, _repository);
        var changed = new TaskCompletionSource<ChangeListenerService.ChangeEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        listener.DocumentChanged += (_, args) => changed.TrySetResult(args);
        await listener.StartListeningAsync();
        var scan = Scan("APX-50000001", ScanType.Pickup, minutesAgo: 5);

        // Act
        await _repository.CreateAsync(scan);
        var notification = await changed.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // Assert
        Assert.Contains(scan.Id, notification.ChangedDocumentIds);
        Assert.Equal(1, notification.PendingScanCount);
    }

    private static ShipmentScan Scan(string trackingNumber, ScanType type, int minutesAgo,
        string courierId = "courier-demo-01", string? notes = null) => new()
    {
        Id = ScanDocumentId.New(),
        TrackingNumber = trackingNumber,
        ScanType = type,
        CourierId = courierId,
        Location = "Indore Hub",
        Notes = notes,
        // A fixed instant keeps the ordering assertions independent of when the suite runs.
        ScannedAtUtc = new DateTime(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc).AddMinutes(-minutesAgo)
    };
}
