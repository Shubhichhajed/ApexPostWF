namespace ApexPost.Domain;

public sealed record ShipmentPriceQuote(decimal BaseCharge, decimal HeavyParcelSurcharge)
{
    public decimal Total => BaseCharge + HeavyParcelSurcharge;
}

public static class ShipmentPricing
{
    public static ShipmentPriceQuote Quote(ShipmentRate rate, decimal weightKg)
    {
        if (weightKg <= 0m || weightKg > rate.MaximumWeightKg)
            throw new ArgumentOutOfRangeException(nameof(weightKg));

        var surcharge = weightKg > 30m ? 12.50m : 0m;
        return new ShipmentPriceQuote(rate.BaseRate + weightKg * rate.PerKgRate, surcharge);
    }
}