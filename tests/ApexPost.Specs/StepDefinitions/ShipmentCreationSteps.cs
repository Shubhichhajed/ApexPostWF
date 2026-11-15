using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ApexPost.Api.IntegrationTests.Infrastructure;
using ApexPost.Domain;
using ApexPost.Infrastructure.Data;
using ApexPost.Specs.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Reqnroll;
using Xunit;

namespace ApexPost.Specs.StepDefinitions;

[Binding]
public sealed class ShipmentCreationSteps(ApiScenarioState state)
{
    [Given("^I am signed in as an administrator$")]
    public void GivenSignedInAsAdministrator() => state.User = "Admin";

    [Given("^I am signed in as courier \"(.*)\"$")]
    public void GivenSignedInAsCourier(string courierId) => state.User = $"Courier:{courierId}";

    [Given("^the network offers a Standard rate for parcels up to 70 kg$")]
    public async Task GivenStandardRateUpTo70Kg()
    {
        await using var scope = state.Host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApexPostDbContext>();
        var rate = await db.ShippingRates.AsNoTracking()
            .SingleAsync(x => x.Zone == ApiTestHost.Zone && x.ServiceLevel == ServiceLevel.Standard);
        Assert.Equal(70m, rate.MaximumWeightKg);
    }

    [Given("^shipment \"(.*)\" has already been registered$")]
    public async Task GivenShipmentAlreadyRegistered(string trackingNumber)
    {
        await RegisterAsync(trackingNumber, 1.0m, "Alex Morgan", "Springfield");
        Assert.Equal(HttpStatusCode.Created, state.LastStatus);
    }

    [When("^I register shipment \"(.*)\" weighing (.*) kg for \"(.*)\" in \"(.*)\"$")]
    public Task WhenIRegisterShipment(string trackingNumber, decimal weight, string recipient, string city)
        => RegisterAsync(trackingNumber, weight, recipient, city);

    [Then("^the shipment is accepted$")]
    public void ThenTheShipmentIsAccepted() => Assert.Equal(HttpStatusCode.Created, state.LastStatus);

    [Then("^shipment \"(.*)\" can be looked up with status \"(.*)\"$")]
    public async Task ThenShipmentCanBeLookedUp(string trackingNumber, string status)
    {
        using var client = state.Host.CreateClientAs("Admin");
        using var response = await client.GetAsync($"api/v2/shipments/{trackingNumber}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        state.LastBody = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(status, state.LastBody.GetProperty("status").GetString());
    }

    [Then("^its recipient city is \"(.*)\"$")]
    public void ThenItsRecipientCityIs(string city)
        => Assert.Equal(city, state.LastBody.GetProperty("recipient").GetProperty("city").GetString());

    [Then("^the shipment is rejected as a duplicate$")]
    public void ThenRejectedAsDuplicate() => Assert.Equal(HttpStatusCode.Conflict, state.LastStatus);

    [Then("^the shipment is rejected because the weight is invalid$")]
    public void ThenRejectedForWeight()
    {
        Assert.Equal(HttpStatusCode.BadRequest, state.LastStatus);
        Assert.True(state.LastBody.GetProperty("errors").TryGetProperty("Weight", out _),
            "Expected a validation error for Weight.");
    }

    [Then("^shipment \"(.*)\" does not exist$")]
    public async Task ThenShipmentDoesNotExist(string trackingNumber)
    {
        using var client = state.Host.CreateClientAs("Admin");
        using var response = await client.GetAsync($"api/v2/shipments/{trackingNumber}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Then("^the request is refused as forbidden$")]
    public void ThenForbidden() => Assert.Equal(HttpStatusCode.Forbidden, state.LastStatus);

    private async Task RegisterAsync(string trackingNumber, decimal weight, string recipient, string city)
    {
        using var client = state.CreateClient();
        using var response = await client.PostAsJsonAsync("api/v2/shipments", new
        {
            trackingNumber, weight, recipientName = recipient, recipientPhone = "+44 20 7946 0958",
            recipientAddress = "100 Example Avenue", recipientCity = city
        });
        state.LastStatus = response.StatusCode;
        var text = await response.Content.ReadAsStringAsync();
        state.LastBody = string.IsNullOrWhiteSpace(text) ? default : JsonDocument.Parse(text).RootElement.Clone();
        if (response.StatusCode == HttpStatusCode.Created)
            state.CreatedTrackingNumbers.Add(trackingNumber);
    }
}
