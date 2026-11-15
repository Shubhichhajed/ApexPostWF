using ApexPost.Api.Services;
using Xunit;

namespace ApexPost.UnitTests.Api;

/// <summary>Risk: a shipment jumping or going back a step corrupts tracking for customers and partners.</summary>
public sealed class ShipmentStatusRulesTests
{
    [Theory]
    [InlineData("Created", "PickedUp")]
    [InlineData("PickedUp", "InTransit")]
    [InlineData("InTransit", "OutForDelivery")]
    [InlineData("OutForDelivery", "Delivered")]
    [InlineData("OutForDelivery", "Failed")]
    [InlineData("created", "pickedup")] // status names are matched case-insensitively
    public void CanTransition_NextStepInLifecycle_ReturnsTrue(string current, string next)
    {
        // Act
        var allowed = ShipmentStatusRules.CanTransition(current, next);

        // Assert
        Assert.True(allowed);
    }

    [Theory]
    [InlineData("Created", "InTransit")]        // skips PickedUp
    [InlineData("Created", "Delivered")]        // skips everything
    [InlineData("InTransit", "PickedUp")]       // goes backwards
    [InlineData("Delivered", "InTransit")]      // final status cannot be reopened
    [InlineData("Failed", "OutForDelivery")]    // final status cannot be reopened
    [InlineData("Delivered", "Delivered")]      // no self-transition
    [InlineData("Created", "Created")]
    [InlineData("Unknown", "PickedUp")]
    [InlineData("Created", "")]
    [InlineData(null, "PickedUp")]
    [InlineData("Created", null)]
    public void CanTransition_SkippedBackwardsOrFinal_ReturnsFalse(string? current, string? next)
    {
        // Act
        var allowed = ShipmentStatusRules.CanTransition(current, next);

        // Assert
        Assert.False(allowed);
    }

    [Fact]
    public void Apply_AllowedTransitionInLowerCase_ReturnsCanonicalStatusName()
    {
        // Act
        var status = ShipmentStatusRules.Apply("OutForDelivery", "delivered");

        // Assert
        Assert.Equal("Delivered", status);
    }

    [Fact]
    public void Apply_DeliveredBackToInTransit_ThrowsShipmentConflictException()
    {
        // Act
        Action act = () => ShipmentStatusRules.Apply("Delivered", "InTransit");

        // Assert
        var error = Assert.Throws<ShipmentConflictException>(act);
        Assert.Contains("Delivered to InTransit", error.Message);
    }
}
