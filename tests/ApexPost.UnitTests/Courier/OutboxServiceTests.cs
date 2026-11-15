using ApexPost.Courier.Offline;
using Moq;
using Xunit;

namespace ApexPost.UnitTests.Courier;

/// <summary>
/// The offline outbox in isolation: IScanRepository and IShipmentUploader are Moq mocks, so the tests
/// can verify exactly which scans were uploaded and which status writes happened.
/// What these mocks do NOT prove: that Couchbase Lite stores or queries the documents correctly
/// (covered by ApexPost.IntegrationTests/Courier) or that the real API accepts the payload (ApexPost.Specs).
/// </summary>
public sealed class OutboxServiceTests
{
    private readonly Mock<IScanRepository> _repository = new();
    private readonly Mock<IShipmentUploader> _uploader = new(MockBehavior.Strict);

    public OutboxServiceTests()
    {
        _repository.Setup(x => x.UpdateAsync(It.IsAny<ShipmentScan>())).ReturnsAsync((ShipmentScan scan) => scan);
    }

    [Theory]
    [InlineData(ScanSyncOutcome.Accepted, SyncStatus.Synced)]
    [InlineData(ScanSyncOutcome.Duplicate, SyncStatus.Synced)]
    [InlineData(ScanSyncOutcome.Rejected, SyncStatus.Failed)]
    public async Task SyncPendingScansAsync_ServerOutcome_MapsToLocalStatus(ScanSyncOutcome outcome, SyncStatus expected)
    {
        // Arrange
        var scan = PendingScan();
        GivenPending(scan);
        _uploader.Setup(x => x.UploadScansAsync(It.IsAny<IEnumerable<ShipmentScan>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Outcomes((scan.Id, outcome)));

        // Act
        var result = await CreateService().SyncPendingScansAsync();

        // Assert
        Assert.Equal(expected, scan.SyncStatus);
        Assert.Equal(1, result.TotalProcessed);
        _repository.Verify(x => x.UpdateAsync(It.Is<ShipmentScan>(s => s.Id == scan.Id && s.SyncStatus == expected)), Times.Once());
    }

    [Fact]
    public async Task SyncPendingScansAsync_EmptyOutbox_NeverCallsUploader()
    {
        // Arrange
        GivenPending();

        // Act
        var result = await CreateService().SyncPendingScansAsync();

        // Assert
        Assert.Equal(0, result.TotalProcessed);
        _uploader.Verify(x => x.UploadScansAsync(It.IsAny<IEnumerable<ShipmentScan>>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task SyncPendingScansAsync_NoOutcomeForScan_LeavesItPendingForRetry()
    {
        // Arrange
        var scan = PendingScan();
        GivenPending(scan);
        _uploader.Setup(x => x.UploadScansAsync(It.IsAny<IEnumerable<ShipmentScan>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UploadResult());

        // Act
        await CreateService().SyncPendingScansAsync();

        // Assert
        Assert.Equal(SyncStatus.Pending, scan.SyncStatus);
        _repository.Verify(x => x.UpdateAsync(It.IsAny<ShipmentScan>()), Times.Never());
    }

    [Fact]
    public async Task SyncPendingScansAsync_OutcomeForUnknownScanId_ThrowsAndWritesNothing()
    {
        // Arrange
        var scan = PendingScan();
        GivenPending(scan);
        _uploader.Setup(x => x.UploadScansAsync(It.IsAny<IEnumerable<ShipmentScan>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Outcomes(("scan::unknown", ScanSyncOutcome.Accepted)));

        // Act
        var act = () => CreateService().SyncPendingScansAsync();

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.Equal(SyncStatus.Pending, scan.SyncStatus);
        _repository.Verify(x => x.UpdateAsync(It.IsAny<ShipmentScan>()), Times.Never());
    }

    [Fact]
    public async Task SyncPendingScansAsync_FortyFivePendingScans_UploadsBatchesOf20And20And5()
    {
        // Arrange
        var scans = Enumerable.Range(0, 45).Select(_ => PendingScan()).ToArray();
        GivenPending(scans);
        _uploader.Setup(x => x.UploadScansAsync(It.IsAny<IEnumerable<ShipmentScan>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<ShipmentScan> batch, CancellationToken _) =>
                Outcomes(batch.Select(s => (s.Id, Outcome: ScanSyncOutcome.Accepted)).ToArray()));

        // Act
        var result = await CreateService().SyncPendingScansAsync(OutboxService.DefaultBatchSize);

        // Assert
        Assert.Equal(45, result.AcceptedCount);
        _uploader.Verify(x => x.UploadScansAsync(It.Is<IEnumerable<ShipmentScan>>(b => b.Count() == 20), It.IsAny<CancellationToken>()), Times.Exactly(2));
        _uploader.Verify(x => x.UploadScansAsync(It.Is<IEnumerable<ShipmentScan>>(b => b.Count() == 5), It.IsAny<CancellationToken>()), Times.Once());
        Assert.All(scans, scan => Assert.Equal(SyncStatus.Synced, scan.SyncStatus));
    }

    [Fact]
    public async Task SyncPendingScansAsync_UploadInterrupted_StopsAndKeepsRemainingScansPending()
    {
        // Arrange
        var scans = Enumerable.Range(0, 30).Select(_ => PendingScan()).ToArray();
        GivenPending(scans);
        _uploader.Setup(x => x.UploadScansAsync(It.IsAny<IEnumerable<ShipmentScan>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UploadResult { IncompleteReason = "Server unavailable (circuit open)" });

        // Act
        var result = await CreateService().SyncPendingScansAsync(20);

        // Assert
        Assert.Equal("Server unavailable (circuit open)", result.IncompleteReason);
        _uploader.Verify(x => x.UploadScansAsync(It.IsAny<IEnumerable<ShipmentScan>>(), It.IsAny<CancellationToken>()), Times.Once());
        Assert.All(scans, scan => Assert.Equal(SyncStatus.Pending, scan.SyncStatus));
    }

    [Fact]
    public async Task SyncPendingScansAsync_UploaderThrows_PropagatesAndKeepsScanPending()
    {
        // Arrange
        var scan = PendingScan();
        GivenPending(scan);
        _uploader.Setup(x => x.UploadScansAsync(It.IsAny<IEnumerable<ShipmentScan>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("No route to host"));

        // Act
        var act = () => CreateService().SyncPendingScansAsync(20);

        // Assert
        await Assert.ThrowsAsync<HttpRequestException>(act);
        Assert.Equal(SyncStatus.Pending, scan.SyncStatus);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task SyncPendingScansAsync_BatchSizeBelowOne_ThrowsArgumentOutOfRange(int batchSize)
    {
        // Act
        var act = () => CreateService().SyncPendingScansAsync(batchSize);

        // Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(act);
    }

    [Fact]
    public async Task RetryScanAsync_FailedScan_ReturnsToPending()
    {
        // Arrange
        var scan = PendingScan();
        scan.SyncStatus = SyncStatus.Failed;
        _repository.Setup(x => x.GetByIdAsync(scan.Id)).ReturnsAsync(scan);

        // Act
        var found = await CreateService().RetryScanAsync(scan.Id);

        // Assert
        Assert.True(found);
        Assert.Equal(SyncStatus.Pending, scan.SyncStatus);
        _repository.Verify(x => x.UpdateAsync(scan), Times.Once());
    }

    [Fact]
    public async Task RetryScanAsync_SyncedScan_DoesNotWrite()
    {
        // Arrange
        var scan = PendingScan();
        scan.SyncStatus = SyncStatus.Synced;
        _repository.Setup(x => x.GetByIdAsync(scan.Id)).ReturnsAsync(scan);

        // Act
        var found = await CreateService().RetryScanAsync(scan.Id);

        // Assert
        Assert.True(found);
        Assert.Equal(SyncStatus.Synced, scan.SyncStatus);
        _repository.Verify(x => x.UpdateAsync(It.IsAny<ShipmentScan>()), Times.Never());
    }

    [Fact]
    public async Task RetryScanAsync_UnknownScan_ReturnsFalse()
    {
        // Arrange
        _repository.Setup(x => x.GetByIdAsync(It.IsAny<string>())).ReturnsAsync((ShipmentScan?)null);

        // Act
        var found = await CreateService().RetryScanAsync("scan::missing");

        // Assert
        Assert.False(found);
    }

    [Theory]
    [InlineData("scan::3f2504e0-4f89-11d3-9a0c-0305e82c3301", true)]
    [InlineData("scan::not-a-guid", false)]
    [InlineData("3f2504e0-4f89-11d3-9a0c-0305e82c3301", false)]
    [InlineData("SCAN::3f2504e0-4f89-11d3-9a0c-0305e82c3301", false)]
    [InlineData(null, false)]
    public void IsValid_DocumentId_RequiresScanPrefixAndGuid(string? id, bool expected)
    {
        // Act
        var valid = ScanDocumentId.IsValid(id);

        // Assert
        Assert.Equal(expected, valid);
    }

    private OutboxService CreateService() => new(_repository.Object, _uploader.Object);

    private void GivenPending(params ShipmentScan[] scans) =>
        _repository.Setup(x => x.GetPendingScansAsync()).ReturnsAsync(scans.AsEnumerable());

    private static UploadResult Outcomes(params (string Id, ScanSyncOutcome Outcome)[] outcomes)
    {
        var result = new UploadResult();
        foreach (var (id, outcome) in outcomes)
            result.Outcomes[id] = outcome;
        return result;
    }

    private static ShipmentScan PendingScan() => new()
    {
        Id = ScanDocumentId.New(), TrackingNumber = "APX-12345678", ScanType = ScanType.Pickup,
        CourierId = "courier-001", Location = "Indore Hub",
        ScannedAtUtc = new DateTime(2026, 11, 15, 9, 0, 0, DateTimeKind.Utc), SyncStatus = SyncStatus.Pending
    };
}
