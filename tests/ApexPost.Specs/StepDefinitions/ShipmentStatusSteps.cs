using ApexPost.Api.Services;
using Reqnroll;
using Xunit;

namespace ApexPost.Specs.StepDefinitions;

// Copilot-assisted: first draft generated with GitHub Copilot (COPILOT-LOG.md, prompt 2), then reviewed and corrected.
// Runs at domain-rule level (no API host): the lifecycle is a pure rule, and the API-level 409 is covered by
// ApexPost.Api.IntegrationTests.PatchStatus_DeliveredBackToInTransit_Returns409.
[Binding]
public sealed class ShipmentStatusSteps(StatusScenarioState state)
{
    [Given("^the standard shipment lifecycle applies$")]
    public void GivenStandardLifecycle()
    {
        // The lifecycle is fixed in ShipmentStatusRules; this step documents the precondition for readers.
        Assert.True(ShipmentStatusRules.CanTransition("Created", "PickedUp"));
    }

    [Given("^a shipment that is \"(.*)\"$")]
    public void GivenShipmentInStatus(string status) => state.Current = status;

    [When("^it is moved to \"(.*)\"$")]
    public void WhenMovedTo(string next)
    {
        try
        {
            state.Result = ShipmentStatusRules.Apply(state.Current, next);
        }
        catch (ShipmentConflictException ex)
        {
            state.Error = ex;
        }
    }

    [Then("^the move is (allowed|refused)$")]
    public void ThenTheMoveIs(string outcome)
    {
        if (outcome == "allowed")
        {
            Assert.Null(state.Error);
            Assert.NotNull(state.Result);
        }
        else
        {
            Assert.NotNull(state.Error);
            Assert.Null(state.Result);
        }
    }

    [Then("^the move is refused with the message \"(.*)\"$")]
    public void ThenRefusedWithMessage(string message)
    {
        Assert.NotNull(state.Error);
        Assert.Equal(message, state.Error.Message);
    }
}

public sealed class StatusScenarioState
{
    public string Current { get; set; } = string.Empty;
    public string? Result { get; set; }
    public ShipmentConflictException? Error { get; set; }
}
