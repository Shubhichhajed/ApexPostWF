using System.ComponentModel.DataAnnotations;
using ApexPost.Api.Models;
using ApexPost.Api.Services;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace ApexPost.UnitTests.Api;

/// <summary>
/// Request DTO validation run through the same DataAnnotations validator that [ApiController] uses.
/// Cases are chosen at the edges of each rule (just inside / just outside) rather than in bulk.
/// Replaces the Week 4 ApiRequestValidationMatrixTests (see TESTING-STRATEGY.md, existing-test review).
/// </summary>
public sealed class ShipmentRequestValidationTests
{
    [Theory]
    [InlineData("APX-12345678", true)]
    [InlineData("APX-00000000", true)]
    [InlineData("APX-1234567", false)]   // 7 digits
    [InlineData("APX-123456789", false)] // 9 digits
    [InlineData("apx-12345678", false)]  // lower-case prefix
    [InlineData("APX12345678", false)]   // missing hyphen
    [InlineData("APX-1234567X", false)]  // letter in the number
    [InlineData(" APX-12345678", false)] // leading space
    [InlineData("", false)]
    public void CreateShipmentRequest_TrackingNumber_MustMatchApxAndEightDigits(string trackingNumber, bool expectedValid)
    {
        // Arrange
        var request = ValidCreateRequest();
        request.TrackingNumber = trackingNumber;

        // Act
        var valid = IsPropertyValid(request, nameof(CreateShipmentRequest.TrackingNumber));

        // Assert
        Assert.Equal(expectedValid, valid);
    }

    [Theory]
    [InlineData("APX-12345678", true)]
    [InlineData("APX-1234567", false)]
    [InlineData("ABC-12345678", false)]
    public void TrackingNumberRouteConstraint_Value_MatchesOnlyCanonicalFormat(string value, bool expected)
    {
        // Arrange
        var constraint = new TrackingNumberRouteConstraint();
        var values = new RouteValueDictionary { ["trackingNumber"] = value };

        // Act
        var matched = constraint.Match(null, null, "trackingNumber", values, RouteDirection.IncomingRequest);

        // Assert
        Assert.Equal(expected, matched);
    }

    public static TheoryData<decimal, bool> WeightBoundaries => new()
    {
        { 0m, false },
        { 0.09m, false },
        { 0.1m, true },   // lower limit is inclusive
        { 35m, true },
        { 70m, true },    // upper limit is inclusive
        { 70.01m, false },
        { -1m, false }
    };

    [Theory]
    [MemberData(nameof(WeightBoundaries))]
    public void CreateShipmentRequest_Weight_MustBeBetween0Point1And70Kg(decimal weight, bool expectedValid)
    {
        // Arrange
        var request = ValidCreateRequest();
        request.Weight = weight;

        // Act
        var valid = IsPropertyValid(request, nameof(CreateShipmentRequest.Weight));

        // Assert
        Assert.Equal(expectedValid, valid);
    }

    [Theory]
    [MemberData(nameof(WeightBoundaries))]
    public void UpdateShipmentRequest_Weight_UsesTheSameLimitsAsCreate(decimal weight, bool expectedValid)
    {
        // Arrange
        var request = new UpdateShipmentRequest
        {
            RecipientName = "Alex Morgan", RecipientPhone = "+44 20 7946 0958",
            RecipientAddress = "100 Example Avenue", RecipientCity = "Springfield", Weight = weight
        };

        // Act
        var valid = IsPropertyValid(request, nameof(UpdateShipmentRequest.Weight));

        // Assert
        Assert.Equal(expectedValid, valid);
    }

    [Theory]
    [InlineData("+44 20 7946 0958", true)]
    [InlineData("1234567", true)]            // 7 digits: shortest allowed
    [InlineData("123456789012345", true)]    // 15 digits: longest allowed
    [InlineData("0731 255-0101", true)]       // spaces and hyphens are allowed separators
    [InlineData("123456", false)]            // 6 digits
    [InlineData("1234567890123456", false)]  // 16 digits
    [InlineData("call me", false)]
    [InlineData("++441234567", false)]
    [InlineData("", false)]                  // required
    public void CreateShipmentRequest_RecipientPhone_Needs7To15Digits(string phone, bool expectedValid)
    {
        // Arrange
        var request = ValidCreateRequest();
        request.RecipientPhone = phone;

        // Act
        var valid = IsPropertyValid(request, nameof(CreateShipmentRequest.RecipientPhone));

        // Assert
        Assert.Equal(expectedValid, valid);
    }

    [Theory]
    [InlineData(nameof(CreateShipmentRequest.RecipientName), "")]
    [InlineData(nameof(CreateShipmentRequest.RecipientAddress), "")]
    [InlineData(nameof(CreateShipmentRequest.RecipientAddress), "1 St")] // shorter than 5 characters
    [InlineData(nameof(CreateShipmentRequest.RecipientCity), "")]
    public void CreateShipmentRequest_MissingOrTooShortRecipientField_IsInvalid(string property, string value)
    {
        // Arrange
        var request = ValidCreateRequest();
        typeof(CreateShipmentRequest).GetProperty(property)!.SetValue(request, value);

        // Act
        var valid = IsPropertyValid(request, property);

        // Assert
        Assert.False(valid);
    }

    [Theory]
    [InlineData("PickedUp", true)]
    [InlineData("Delivered", true)]
    [InlineData("Created", false)] // a shipment can never be moved back to Created
    [InlineData("Lost", false)]
    [InlineData("", false)]
    public void UpdateStatusRequest_Status_AcceptsOnlyKnownTargetStatuses(string status, bool expectedValid)
    {
        // Arrange
        var request = new UpdateStatusRequest { Status = status };

        // Act
        var valid = IsPropertyValid(request, nameof(UpdateStatusRequest.Status));

        // Assert
        Assert.Equal(expectedValid, valid);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(100, true)]
    [InlineData(101, false)]
    public void ScanUploadRequest_BatchSize_MustBeOneToOneHundred(int count, bool expectedValid)
    {
        // Arrange
        var request = new ScanUploadRequest
        {
            Scans = Enumerable.Range(0, count).Select(i => ValidScan($"scan::3f2504e0-4f89-11d3-9a0c-{i:D12}")).ToList()
        };

        // Act
        var valid = IsPropertyValid(request, nameof(ScanUploadRequest.Scans));

        // Assert
        Assert.Equal(expectedValid, valid);
    }

    [Fact]
    public void ScanUploadRequest_RepeatedScanId_IsAllowedSoTheServerCanCountDuplicates()
    {
        // Arrange
        const string sameId = "scan::3f2504e0-4f89-11d3-9a0c-0305e82c3301";
        var request = new ScanUploadRequest { Scans = [ValidScan(sameId), ValidScan(sameId)] };

        // Act
        var errors = Validate(request);

        // Assert
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(-60, true)]
    [InlineData(60, false)]
    public void ScanItemRequest_ScanTime_CannotBeInTheFuture(int minutesFromNow, bool expectedValid)
    {
        // Arrange
        var scan = ValidScan("scan::3f2504e0-4f89-11d3-9a0c-0305e82c3301");
        scan.ScannedAtUtc = DateTime.UtcNow.AddMinutes(minutesFromNow); // relative to the run, so deterministic

        // Act
        var valid = IsPropertyValid(scan, nameof(ScanItemRequest.ScannedAtUtc));

        // Assert
        Assert.Equal(expectedValid, valid);
    }

    [Fact]
    public void ScanItemRequest_DeliveredWithoutSignature_IsInvalid()
    {
        // Arrange
        var scan = ValidScan("scan::3f2504e0-4f89-11d3-9a0c-0305e82c3301");
        scan.ScanType = "Delivered";
        scan.Notes = " ";

        // Act
        var valid = IsPropertyValid(scan, nameof(ScanItemRequest.Notes));

        // Assert
        Assert.False(valid);
    }

    private static CreateShipmentRequest ValidCreateRequest() => new()
    {
        TrackingNumber = "APX-12345678", Weight = 2.5m, RecipientName = "Alex Morgan",
        RecipientPhone = "+44 20 7946 0958", RecipientAddress = "100 Example Avenue", RecipientCity = "Springfield"
    };

    private static ScanItemRequest ValidScan(string scanId) => new()
    {
        ScanId = scanId, ScanType = "Pickup", CourierId = "courier-demo-01", Location = "Indore Hub",
        ScannedAtUtc = DateTime.UtcNow.AddMinutes(-5)
    };

    private static bool IsPropertyValid(object instance, string propertyName) =>
        !Validate(instance).Any(error => error.MemberNames.Contains(propertyName)
            || error.MemberNames.Any(member => member.EndsWith($".{propertyName}", StringComparison.Ordinal)));

    private static List<ValidationResult> Validate(object instance)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(instance, new ValidationContext(instance), results, validateAllProperties: true);
        return results;
    }
}
