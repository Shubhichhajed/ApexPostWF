using ApexPost.Domain;
using Xunit;

namespace ApexPost.UnitTests.Domain;

/// <summary>Value objects and counter-transaction state rules.</summary>
public sealed class ValueObjectAndTransactionTests
{
    [Fact]
    public void BranchCode_PaddedLowerCaseValue_IsTrimmedAndUpperCased()
    {
        // Act
        var branchCode = new BranchCode("  ind01 ");

        // Assert
        Assert.Equal("IND01", branchCode.Value);
        Assert.Equal("IND01", branchCode.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("BRANCH-CODE-TOO-LONG")] // more than 12 characters
    public void BranchCode_BlankOrTooLong_ThrowsDomainException(string value)
    {
        // Act
        Action act = () => _ = new BranchCode(value);

        // Assert
        Assert.Throws<DomainException>(act);
    }

    [Fact]
    public void Sku_PaddedLowerCaseValue_IsTrimmedAndUpperCased()
    {
        // Act
        var sku = new Sku(" apx-001 ");

        // Assert
        Assert.Equal("APX-001", sku.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Sku_Blank_ThrowsDomainException(string value)
    {
        // Act
        Action act = () => _ = new Sku(value);

        // Assert
        Assert.Throws<DomainException>(act);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Weight_ZeroOrNegative_ThrowsDomainException(int kilograms)
    {
        // Act
        Action act = () => _ = new Weight(kilograms);

        // Assert
        Assert.Throws<DomainException>(act);
    }

    [Fact]
    public void MoneyAdd_ValuesWithThreeDecimals_RoundsToTwoDecimals()
    {
        // Arrange
        var left = new Money(10.105m);
        var right = new Money(2.105m);

        // Act
        var result = left.Add(right);

        // Assert
        Assert.Equal(12.21m, result.Amount);
        Assert.Equal("INR", result.Currency);
    }

    [Fact]
    public void MoneyAdd_DifferentCurrencies_ThrowsDomainException()
    {
        // Arrange
        var rupees = new Money(10m, "INR");
        var dollars = new Money(5m, "USD");

        // Act
        Action act = () => rupees.Add(dollars);

        // Assert
        Assert.Throws<DomainException>(act);
    }

    [Theory]
    [InlineData(TaxCategory.Standard, 0.18)]
    [InlineData(TaxCategory.Reduced, 0.05)]
    [InlineData(TaxCategory.ZeroRated, 0.0)]
    public void TaxRate_EachCategory_ReturnsConfiguredRate(TaxCategory category, double expected)
    {
        // Act
        var rate = BusinessRules.TaxRate(category);

        // Assert
        Assert.Equal((decimal)expected, rate);
    }

    [Fact]
    public void MarkPaid_TransactionWithNoLines_ThrowsAndStaysOpen()
    {
        // Arrange
        var transaction = new CounterTransaction();

        // Act
        Action act = () => transaction.MarkPaid(PaymentMethod.Cash);

        // Assert
        Assert.Throws<DomainException>(act);
        Assert.Equal(TransactionStatus.Open, transaction.Status);
    }

    [Theory]
    [InlineData(0, 0.18)]  // quantity below 1
    [InlineData(1, 1.10)]  // tax rate above 100 %
    public void AddLine_InvalidQuantityOrTaxRate_ThrowsAndAddsNothing(int quantity, double taxRate)
    {
        // Arrange
        var transaction = new CounterTransaction();
        var line = new TransactionLine("line-1", LineKind.RetailProduct, "Box", "APX-001", quantity, 10m, (decimal)taxRate, 10m);

        // Act
        Action act = () => transaction.AddLine(line);

        // Assert
        Assert.Throws<DomainException>(act);
        Assert.Empty(transaction.Lines);
    }

    [Fact]
    public void Void_PaidTransaction_SetsVoidedAndStoresTrimmedReason()
    {
        // Arrange
        var transaction = new CounterTransaction();
        transaction.AddLine(new TransactionLine("line-1", LineKind.RetailProduct, "Box", "APX-001", 1, 10m, 0m, 10m));
        transaction.MarkPaid(PaymentMethod.Cash);

        // Act
        transaction.Void("  Duplicate transaction  ");

        // Assert
        Assert.Equal(TransactionStatus.Voided, transaction.Status);
        Assert.Equal("Duplicate transaction", transaction.Reason);
    }

    [Fact]
    public void Refund_UnpaidTransaction_ThrowsAndStaysOpen()
    {
        // Arrange
        var transaction = new CounterTransaction();

        // Act
        Action act = () => transaction.Refund("Customer request");

        // Assert
        Assert.Throws<DomainException>(act);
        Assert.Equal(TransactionStatus.Open, transaction.Status);
    }

    [Theory]
    [InlineData(1000.00, false)]
    [InlineData(1000.01, true)]
    public void RequiresIdentityVerification_AmountAroundThreshold_OnlyTrueAboveIt(double amount, bool expected)
    {
        // Arrange
        var policy = new MoneyOrderPolicy(1000m);

        // Act
        var required = policy.RequiresIdentityVerification((decimal)amount);

        // Assert
        Assert.Equal(expected, required);
    }
}
