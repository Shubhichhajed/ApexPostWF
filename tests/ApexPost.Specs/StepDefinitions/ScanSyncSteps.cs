using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ApexPost.Courier.Api;
using ApexPost.Courier.Offline;
using ApexPost.Courier.Sync;
using ApexPost.Specs.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Reqnroll;
using Xunit;

namespace ApexPost.Specs.StepDefinitions;

/// <summary>
/// Courier side of the flow: real OutboxService + real ApiShipmentUploader + real ApexApiClient,
/// talking HTTP to the real API host. Only the device's local store is an in-memory fake.
/// </summary>
[Binding]
public sealed class ScanSyncSteps(ApiScenarioState state, SyncScenarioState sync)
{
    [Given("^courier \"(.*)\" is signed in on the courier app$")]
    public void GivenCourierSignedIn(string courierId)
    {
        sync.CourierId = courierId;
        state.User = $"Courier:{courierId}";
    }

    [Given("^shipment \"(.*)\" is registered for delivery$")]
    public async Task GivenShipmentRegistered(string trackingNumber)
    {
        using var admin = state.Host.CreateClientAs("Admin");
        using var response = await admin.PostAsJsonAsync("api/v2/shipments", new
        {
            trackingNumber, weight = 2.5m, recipientName = "Alex Morgan", recipientPhone = "+44 20 7946 0958",
            recipientAddress = "100 Example Avenue", recipientCity = "Springfield"
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        state.CreatedTrackingNumbers.Add(trackingNumber);
    }

    [Given("^the courier recorded an? \"(.*)\" scan at \"(.*)\" while offline$")]
    public Task GivenCourierRecordedScan(ScanType scanType, string location)
        => RecordAsync("APX-70000001", scanType, location, minutesAgo: 10, notes: null);

    [Given("^the courier recorded a delivery without a recipient signature for \"(.*)\" while offline$")]
    public Task GivenDeliveryWithoutSignature(string trackingNumber)
        => RecordAsync(trackingNumber, ScanType.Delivered, "Front door", minutesAgo: 5, notes: null);

    [Given("^the courier recorded a scan timed one hour in the future for \"(.*)\" while offline$")]
    public Task GivenScanInTheFuture(string trackingNumber)
        => RecordAsync(trackingNumber, ScanType.Pickup, "Springfield Depot", minutesAgo: -60, notes: null);

    [Given("^the courier recorded a pickup for \"(.*)\" while offline$")]
    public Task GivenPickupFor(string trackingNumber)
        => RecordAsync(trackingNumber, ScanType.Pickup, "Springfield Depot", minutesAgo: 10, notes: null);

    [Given("^the outbox has synced$")]
    public async Task GivenTheOutboxHasSynced()
    {
        await WhenTheOutboxSyncs();
        Assert.Null(sync.LastResult!.IncompleteReason);
    }

    [Given("^the same scan is queued again because the confirmation was lost$")]
    public void GivenSameScanQueuedAgain()
    {
        foreach (var scan in sync.Device.All)
            scan.SyncStatus = SyncStatus.Pending;
    }

    [When("^the outbox syncs$")]
    public async Task WhenTheOutboxSyncs()
    {
        using var http = state.CreateClient();
        var uploader = new ApiShipmentUploader(new ApexApiClient(http), NullLogger<ApiShipmentUploader>.Instance);
        var outbox = new OutboxService(sync.Device, uploader);
        sync.LastResult = await outbox.SyncPendingScansAsync(OutboxService.DefaultBatchSize);
    }

    [Then("^(\\d+) scans? (?:is|are) reported as accepted$")]
    public void ThenScansAccepted(int expected) => Assert.Equal(expected, sync.LastResult!.AcceptedCount);

    [Then("^the scan is reported as a duplicate$")]
    public void ThenReportedAsDuplicate()
    {
        Assert.Equal(1, sync.LastResult!.DuplicatesSkippedCount);
        Assert.Equal(0, sync.LastResult.AcceptedCount);
    }

    [Then("^every scan on the device is marked \"(.*)\"$")]
    public void ThenEveryScanIsMarked(SyncStatus expected)
    {
        Assert.NotEmpty(sync.Device.All);
        Assert.All(sync.Device.All, scan => Assert.Equal(expected, scan.SyncStatus));
    }

    [Then("^the server holds (\\d+) scans? for \"(.*)\"$")]
    public async Task ThenServerHoldsScans(int expected, string trackingNumber)
    {
        using var client = state.Host.CreateClientAs("Admin");
        using var response = await client.GetAsync($"api/v2/shipments/{trackingNumber}/scans");
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            Assert.Equal(0, expected); // an unknown shipment can hold no scans
            return;
        }

        var history = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(expected, history.GetProperty("scans").GetArrayLength());
    }

    private Task RecordAsync(string trackingNumber, ScanType scanType, string location, int minutesAgo, string? notes)
        => sync.Device.CreateAsync(new ShipmentScan
        {
            Id = ScanDocumentId.New(),
            TrackingNumber = trackingNumber,
            ScanType = scanType,
            CourierId = sync.CourierId,
            Location = location,
            Notes = notes,
            ScannedAtUtc = DateTime.UtcNow.AddMinutes(-minutesAgo) // relative to the run, never a fixed "now"
        });
}

/// <summary>Courier-side state for one scenario (context injection).</summary>
public sealed class SyncScenarioState
{
    public string CourierId { get; set; } = "courier-demo-01";
    public InMemoryScanRepository Device { get; } = new();
    public UploadResult? LastResult { get; set; }
}
