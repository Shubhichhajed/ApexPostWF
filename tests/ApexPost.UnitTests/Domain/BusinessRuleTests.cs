using ApexPost.Domain;
using Xunit;

namespace ApexPost.UnitTests.Domain;

/// <summary>Week 2–3 counter business rules. One behaviour per test, Arrange-Act-Assert.</summary>
public sealed class BusinessRuleTests
{
    [Fact]
    public void Adjust_RemovingMoreThanInStock_ThrowsAndKeepsQuantity()
    {
        // Arrange
        var stock = StockItem.Create("IND01", "APX-001", 2);

        // Act
        Action act = () => stock.Adjust(-3);

        // Assert
        Assert.Throws<DomainException>(act);
        Assert.Equal(2, stock.Quantity);
    }

    [Fact]
    public void Adjust_RemovingExactlyTheStockOnHand_LeavesZero()
    {
        // Arrange
        var stock = StockItem.Create("IND01", "APX-001", 2);

        // Act
        stock.Adjust(-2);

        // Assert
        Assert.Equal(0, stock.Quantity);
    }

    [Fact]
    public void EnsureMoneyOrderVerified_AboveThresholdWithoutVerification_Throws()
    {
        // Act
        Action act = () => BusinessRules.EnsureMoneyOrderVerified(1000.01m, 1000m, false);

        // Assert
        Assert.Throws<DomainException>(act);
    }

    [Theory]
    [InlineData(1000.00, false)] // exactly at the threshold: no ID needed
    [InlineData(999.99, false)]
    [InlineData(1000.01, true)]  // above the threshold with a successful ID check
    public void EnsureMoneyOrderVerified_AtThresholdOrVerified_DoesNotThrow(double amount, bool verified)
    {
        // Act
        var exception = Record.Exception(() => BusinessRules.EnsureMoneyOrderVerified((decimal)amount, 1000m, verified));

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public void ShippingPrice_ParcelHeavierThanServiceLevelLimit_Throws()
    {
        // Arrange
        var rates = new[] { new ShipmentRate("ZONE-01", ServiceLevel.Standard, 50m, 10m, 2m) };

        // Act
        Action act = () => BusinessRules.ShippingPrice(ShipmentKind.Parcel, new Weight(2.1m), "ZONE-01", ServiceLevel.Standard, rates);

        // Assert
        Assert.Throws<DomainException>(act);
    }

    [Theory]
    [InlineData(ServiceLevel.Standard, 70.00)]
    [InlineData(ServiceLevel.Express, 122.50)]
    public void ShippingPrice_ConfiguredZoneRate_AppliesServiceLevelMultiplier(ServiceLevel level, double expected)
    {
        // Arrange
        var rates = new[]
        {
            new ShipmentRate("ZONE-01", ServiceLevel.Standard, 50m, 10m, 10m),
            new ShipmentRate("ZONE-01", ServiceLevel.Express, 50m, 10m, 10m)
        };

        // Act
        var price = BusinessRules.ShippingPrice(ShipmentKind.Parcel, new Weight(2m), "ZONE-01", level, rates);

        // Assert
        Assert.Equal((decimal)expected, price);
    }

    [Fact]
    public void EnsureCanTransact_InactiveStaff_Throws()
    {
        // Arrange
        var staff = new Staff { HomeBranchCode = "IND01", Status = StaffStatus.Inactive };
        var branch = new Branch { Code = "IND01", Status = BranchStatus.Active };

        // Act
        Action act = () => BusinessRules.EnsureCanTransact(staff, branch, "IND01");

        // Assert
        Assert.Throws<DomainException>(act);
    }

    [Fact]
    public void EnsureCanTransact_ClosedBranch_Throws()
    {
        // Arrange
        var staff = new Staff { HomeBranchCode = "IND01", Status = StaffStatus.Active };
        var branch = new Branch { Code = "IND01", Status = BranchStatus.Closed };

        // Act
        Action act = () => BusinessRules.EnsureCanTransact(staff, branch, "IND01");

        // Assert
        Assert.Throws<DomainException>(act);
    }

    [Fact]
    public void EnsureCanTransact_ClerkAtAnotherBranch_Throws()
    {
        // Arrange
        var staff = new Staff { HomeBranchCode = "IND01", Status = StaffStatus.Active };
        var branch = new Branch { Code = "IND01", Status = BranchStatus.Active };

        // Act
        Action act = () => BusinessRules.EnsureCanTransact(staff, branch, "DEL01");

        // Assert
        Assert.Throws<DomainException>(act);
    }

    [Theory]
    [InlineData("ABCD123456789", "****6789")]
    [InlineData("1234", "****")]
    public void Mask_DocumentNumber_NeverReturnsTheFullNumber(string documentNumber, string expected)
    {
        // Act
        var masked = IdentityVerification.Mask(documentNumber);

        // Assert
        Assert.Equal(expected, masked);
    }

    [Fact]
    public void EnsureManager_CounterClerk_Throws()
    {
        // Arrange
        var clerk = new Staff { Role = StaffRole.CounterClerk };

        // Act
        Action act = () => BusinessRules.EnsureManager(clerk, "mistake");

        // Assert
        Assert.Throws<DomainException>(act);
    }

    [Fact]
    public void EnsureManager_ManagerWithBlankReason_Throws()
    {
        // Arrange
        var manager = new Staff { Role = StaffRole.BranchManager };

        // Act
        Action act = () => BusinessRules.EnsureManager(manager, " ");

        // Assert
        Assert.Throws<DomainException>(act);
    }

    [Fact]
    public void EnsureManager_ManagerWithReason_DoesNotThrow()
    {
        // Arrange
        var manager = new Staff { Role = StaffRole.BranchManager };

        // Act
        var exception = Record.Exception(() => BusinessRules.EnsureManager(manager, "Customer request"));

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public void AddLine_RetailLine_RecalculatesSubtotalTaxAndTotal()
    {
        // Arrange
        var transaction = new CounterTransaction();

        // Act
        transaction.AddLine(new TransactionLine("1", LineKind.RetailProduct, "Box", "APX-001", 2, 10m, 0.18m, 23.60m));

        // Assert
        Assert.Equal(20m, transaction.Subtotal);
        Assert.Equal(3.60m, transaction.Tax);
        Assert.Equal(23.60m, transaction.Total);
    }

    [Fact]
    public void AddLine_AfterPayment_Throws()
    {
        // Arrange
        var transaction = new CounterTransaction();
        transaction.AddLine(new TransactionLine("1", LineKind.RetailProduct, "Box", "APX-001", 2, 10m, 0.18m, 23.60m));
        transaction.MarkPaid(PaymentMethod.Cash);

        // Act
        Action act = () => transaction.AddLine(new TransactionLine("2", LineKind.Shipping, "Ship", null, 1, 5m, 0m, 5m));

        // Assert
        Assert.Throws<DomainException>(act);
    }
}
