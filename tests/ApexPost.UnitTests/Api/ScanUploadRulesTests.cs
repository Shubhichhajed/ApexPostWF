using ApexPost.Api.Services;
using Xunit;

namespace ApexPost.UnitTests.Api;

/// <summary>
/// Server-side scan validation. The clock is a fixed value passed in, so "future" boundaries are exact
/// and the tests never depend on when they run.
/// </summary>
public sealed class ScanUploadRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Validate_CompletePickupScan_ReturnsNull()
    {
        // Arrange
        var scan = ValidScan();

        // Act
        var reason = ScanUploadRules.Validate(scan, Now);

        // Assert
        Assert.Null(reason);
    }

    [Theory]
    [InlineData(-3600, true)] // an hour ago
    [InlineData(0, true)]     // exactly now
    [InlineData(5, true)]     // at the 5-second clock-skew limit
    [InlineData(6, false)]    // just beyond the limit
    [InlineData(3600, false)] // an hour in the future
    public void Validate_ScanTimeRelativeToServerClock_RejectsOnlyBeyondClockSkew(int secondsFromNow, bool expectedValid)
    {
        // Arrange
        var scan = ValidScan();
        scan.ScannedAtUtc = Now.UtcDateTime.AddSeconds(secondsFromNow);

        // Act
        var reason = ScanUploadRules.Validate(scan, Now);

        // Assert
        Assert.Equal(expectedValid, reason is null);
    }

    [Theory]
    [InlineData("scan::not-a-guid", "ScanId must use scan::{Guid} format.")]
    [InlineData("3f2504e0-4f89-11d3-9a0c-0305e82c3301", "ScanId must use scan::{Guid} format.")]
    [InlineData("", "ScanId must use scan::{Guid} format.")]
    public void Validate_MalformedScanId_ReturnsFormatReason(string scanId, string expectedReason)
    {
        // Arrange
        var scan = ValidScan();
        scan.ScanId = scanId;

        // Act
        var reason = ScanUploadRules.Validate(scan, Now);

        // Assert
        Assert.Equal(expectedReason, reason);
    }

    [Theory]
    [InlineData("Created")]
    [InlineData("Lost")]
    [InlineData("")]
    public void Validate_UnsupportedScanType_ReturnsReasonNamingTheType(string scanType)
    {
        // Arrange
        var scan = ValidScan();
        scan.ScanType = scanType;

        // Act
        var reason = ScanUploadRules.Validate(scan, Now);

        // Assert
        Assert.Equal($"Unsupported scan type '{scanType}'.", reason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_DeliveredWithoutSignature_IsRejected(string? notes)
    {
        // Arrange
        var scan = ValidScan();
        scan.ScanType = "Delivered";
        scan.Notes = notes;

        // Act
        var reason = ScanUploadRules.Validate(scan, Now);

        // Assert
        Assert.Equal("Delivered scans must include recipient signature/name in Notes.", reason);
    }

    [Fact]
    public void Validate_DeliveredWithSignature_ReturnsNull()
    {
        // Arrange
        var scan = ValidScan();
        scan.ScanType = "Delivered";
        scan.Notes = "Signed by A. Morgan";

        // Act
        var reason = ScanUploadRules.Validate(scan, Now);

        // Assert
        Assert.Null(reason);
    }

    [Fact]
    public void Validate_MissingTimestamp_IsRejected()
    {
        // Arrange
        var scan = ValidScan();
        scan.ScannedAtUtc = default;

        // Act
        var reason = ScanUploadRules.Validate(scan, Now);

        // Assert
        Assert.Equal("Scan timestamp is required.", reason);
    }

    private static OfflineScanUpload ValidScan() => new()
    {
        ScanId = "scan::3f2504e0-4f89-11d3-9a0c-0305e82c3301",
        ScanType = "Pickup",
        CourierId = "courier-demo-01",
        Location = "Indore Hub",
        ScannedAtUtc = Now.UtcDateTime.AddMinutes(-10)
    };
}
