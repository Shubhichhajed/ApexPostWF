using ApexPost.Domain;
using Xunit;

namespace ApexPost.UnitTests.Domain;

/// <summary>
/// Heavy-parcel surcharge, built test-first (see TDD-LOG.md).
/// Rule: parcels strictly heavier than 30 kg pay a flat 12.50 surcharge on top of the zone rate.
/// Expected values are written as literals on purpose so the tests do not repeat the implementation.
/// </summary>
public sealed class ShipmentPricingTests
{
    // Zone rate used by every test: 40.00 + 10.00 per kg, up to 70 kg.
    private static readonly ShipmentRate StandardRate = new("ZONE-01", ServiceLevel.Standard, 40m, 10m, 70m);

    public static TheoryData<decimal> NotHeavyWeights => new() { 0.1m, 10m, 29.99m, 30m };
    public static TheoryData<decimal> HeavyWeights => new() { 30.001m, 30.01m, 45m, 70m };

    [Theory]
    [MemberData(nameof(NotHeavyWeights))]
    public void Quote_WeightAtOrBelow30Kg_HasNoSurcharge(decimal weightKg)
    {
        // Act
        var quote = ShipmentPricing.Quote(StandardRate, weightKg);

        // Assert
        Assert.Equal(0m, quote.HeavyParcelSurcharge);
    }

    [Theory]
    [MemberData(nameof(HeavyWeights))]
    public void Quote_WeightAbove30Kg_AddsHeavyParcelSurcharge(decimal weightKg)
    {
        // Act
        var quote = ShipmentPricing.Quote(StandardRate, weightKg);

        // Assert
        Assert.Equal(12.50m, quote.HeavyParcelSurcharge);
    }

    [Fact]
    public void Quote_LightParcel_TotalIsZoneRatePriceOnly()
    {
        // Arrange
        const decimal weightKg = 2.5m;

        // Act
        var quote = ShipmentPricing.Quote(StandardRate, weightKg);

        // Assert: 40 + 2.5 x 10
        Assert.Equal(65m, quote.BaseCharge);
        Assert.Equal(65m, quote.Total);
    }

    [Fact]
    public void Quote_HeavyParcel_TotalIsBaseChargePlusSurcharge()
    {
        // Arrange
        const decimal weightKg = 31m;

        // Act
        var quote = ShipmentPricing.Quote(StandardRate, weightKg);

        // Assert: 40 + 31 x 10 = 350, plus 12.50
        Assert.Equal(350m, quote.BaseCharge);
        Assert.Equal(362.50m, quote.Total);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Quote_ZeroOrNegativeWeight_ThrowsArgumentOutOfRangeException(int weightKg)
    {
        // Act
        Action act = () => ShipmentPricing.Quote(StandardRate, weightKg);

        // Assert
        Assert.Throws<ArgumentOutOfRangeException>(act);
    }

    [Fact]
    public void Quote_WeightAboveRateMaximum_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        const decimal justOverMaximum = 70.01m;

        // Act
        Action act = () => ShipmentPricing.Quote(StandardRate, justOverMaximum);

        // Assert
        Assert.Throws<ArgumentOutOfRangeException>(act);
    }
}
