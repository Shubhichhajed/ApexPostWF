# TDD Log: heavy-parcel surcharge

## Requirement

> **Shipments over 30 kg require a surcharge.**

Agreed details before writing any code:

- The surcharge is a flat **12.50** added on top of the normal zone/service-level price (`BaseRate + weight × PerKgRate`).
- "Over 30 kg" means **strictly greater than 30 kg**: a 30.000 kg parcel pays no surcharge, 30.001 kg does.
- The price is quoted from a `ShipmentRate`, so the rule must not need a database.
- Invalid input fails loudly: weight ≤ 0 or above the rate's maximum throws `ArgumentOutOfRangeException`.

The rule did not exist before this week: `ShipmentService` computed `rate.BaseRate + weight * rate.PerKgRate` inline, in two places (create and update).

## Commit history (branch `week5-testing`)

| Commit | Message | Content |
|---|---|---|
| 1 | `RED: heavy-parcel surcharge tests for ShipmentPricing.Quote` | `tests/ApexPost.UnitTests/Domain/ShipmentPricingTests.cs` only |
| 2 | `GREEN: minimal ShipmentPricing.Quote with 30 kg surcharge` | `src/ApexPost.Domain/Rules/ShipmentPricing.cs` (minimal version below) |
| 3 | `REFACTOR: name pricing constants, clearer guards, use ShipmentPricing in ShipmentService` | final `ShipmentPricing.cs`, `ShipmentService.cs` |

## RED: the first failing test

The tests were written against an API that did not exist yet, deciding the shape of the result first: a quote with the base charge and the surcharge separately, so a receipt can show both.

```csharp
public static TheoryData<decimal> HeavyWeights => new() { 30.001m, 30.01m, 45m, 70m };

[Theory]
[MemberData(nameof(HeavyWeights))]
public void Quote_WeightAbove30Kg_AddsHeavyParcelSurcharge(decimal weightKg)
{
    // Act
    var quote = ShipmentPricing.Quote(StandardRate, weightKg);

    // Assert
    Assert.Equal(12.50m, quote.HeavyParcelSurcharge);
}
```

The full RED set (`ShipmentPricingTests`) also contains:

- `Quote_WeightAtOrBelow30Kg_HasNoSurcharge` for 0.1, 10, 29.99 and **30** kg (the boundary that decides "over");
- `Quote_LightParcel_TotalIsZoneRatePriceOnly` (2.5 kg → 65.00);
- `Quote_HeavyParcel_TotalIsBaseChargePlusSurcharge` (31 kg → 350.00 + 12.50 = 362.50);
- `Quote_ZeroOrNegativeWeight_ThrowsArgumentOutOfRangeException`;
- `Quote_WeightAboveRateMaximum_ThrowsArgumentOutOfRangeException` (70.01 kg on a 70 kg rate).

Expected values are written as literals (12.50, 362.50), never as `ShipmentPricing.HeavyParcelSurchargeAmount`, so the tests cannot pass just because they repeat the implementation.

**Result:** the test project did not compile: `error CS0103: The name 'ShipmentPricing' does not exist in the current context`. A compile failure is the first red: the behaviour does not exist at all.

## GREEN: the minimal implementation

Just enough to make every test pass, with the magic numbers left inline on purpose:

```csharp
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
```

**Result:** all `ShipmentPricingTests` pass; the rest of the suite is unchanged.

## REFACTOR: what changed (tests stayed green throughout)

1. **Named the rule's numbers:** `HeavyParcelThresholdKg = 30m` and `HeavyParcelSurchargeAmount = 12.50m`, and an `IsHeavy(weight)` helper, so the rule reads like the requirement.
2. **Separate, explicit guards** with messages that say what was wrong (weight ≤ 0 vs. above the rate's maximum, naming the zone and service level), plus `ArgumentNullException.ThrowIfNull(rate)`.
3. **Removed duplication in `ShipmentService`:** the price formula was written twice (create and update). Both now call `ShipmentPricing.Quote(rate, weight).Total`, so the surcharge applies wherever a price is calculated, and there is one place to change pricing.

No new behaviour was added during the refactor; the exception type and all values are unchanged, which is why the same tests cover both versions.

## Production code changed to make it testable, and why

| Change | Why it was needed |
|---|---|
| Pricing written as a **pure function** `ShipmentPricing.Quote(ShipmentRate, decimal)` in the Domain project, instead of inside `ShipmentService` | The old formula lived in a method that needs `ApexPostDbContext` and a SQL Server database. As a pure function over a rate it can be tested in microseconds with any rate the test invents. |
| **`TimeProvider` injected into `ShipmentService`** (registered as `TimeProvider.System` in `Program.cs`) instead of `DateTime.UtcNow` / `DateTimeOffset.UtcNow` | The scan "not in the future" rule and the shipment `CreatedAt` depended on the real clock, so the +5 s clock-skew boundary could not be tested exactly. Tests can now supply a fixed time. |
| **Status transitions extracted** to `ShipmentStatusRules.CanTransition/Apply`, **scan checks** to `ScanUploadRules.Validate(scan, now)` | They were private members of `ShipmentService` and only reachable through the database. Now each transition pair and each scan rule is a parameterised unit test (`ShipmentStatusRulesTests`, `ScanUploadRulesTests`), and `ShipmentService` keeps the same behaviour by calling them. |

## How to replay the cycle

```powershell
git checkout week5-testing
git log --oneline -- src/ApexPost.Domain/Rules/ShipmentPricing.cs tests/ApexPost.UnitTests/Domain/ShipmentPricingTests.cs
dotnet test tests/ApexPost.UnitTests --filter "FullyQualifiedName~ShipmentPricingTests"
```
