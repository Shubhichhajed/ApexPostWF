namespace ApexPost.Domain;

/// <summary>Price of one parcel: the zone/service-level charge plus any heavy-parcel surcharge.</summary>
public sealed record ShipmentPriceQuote(decimal BaseCharge, decimal HeavyParcelSurcharge)
{
    public decimal Total => BaseCharge + HeavyParcelSurcharge;
}

/// <summary>
/// Shipment pricing rule (built test-first, see TDD-LOG.md): parcels heavier than 30 kg need extra handling
/// and carry a flat heavy-parcel surcharge. Exactly 30 kg is not heavy.
/// </summary>
public static class ShipmentPricing
{
    /// <summary>Weights strictly above this value are heavy parcels.</summary>
    public const decimal HeavyParcelThresholdKg = 30m;

    /// <summary>Flat surcharge added to every heavy parcel.</summary>
    public const decimal HeavyParcelSurchargeAmount = 12.50m;

    public static ShipmentPriceQuote Quote(ShipmentRate rate, decimal weightKg)
    {
        ArgumentNullException.ThrowIfNull(rate);
        if (weightKg <= 0m)
            throw new ArgumentOutOfRangeException(nameof(weightKg), weightKg, "Weight must be greater than zero.");
        if (weightKg > rate.MaximumWeightKg)
            throw new ArgumentOutOfRangeException(nameof(weightKg), weightKg,
                $"The {rate.Zone} {rate.ServiceLevel} rate accepts parcels up to {rate.MaximumWeightKg} kg.");

        var baseCharge = rate.BaseRate + weightKg * rate.PerKgRate;
        var surcharge = IsHeavy(weightKg) ? HeavyParcelSurchargeAmount : 0m;
        return new ShipmentPriceQuote(baseCharge, surcharge);
    }

    public static bool IsHeavy(decimal weightKg) => weightKg > HeavyParcelThresholdKg;
}
