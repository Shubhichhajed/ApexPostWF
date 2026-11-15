using ApexPost.Api.Data;
using ApexPost.Api.IntegrationTests.Infrastructure;
using ApexPost.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Reqnroll;
using Xunit;

namespace ApexPost.Specs.Support;

/// <summary>
/// Lifecycle hooks for @api features. The in-memory API host (and its database) is created once per feature
/// and kept in the FeatureContext; every scenario deletes the shipments it created, so scenarios stay independent.
/// </summary>
[Binding]
public sealed class ApiHostHooks
{
    [BeforeFeature("api")]
    public static async Task StartApiHostAsync(FeatureContext featureContext)
    {
        var host = new ApiTestHost();
        await host.InitializeAsync();
        featureContext.Set(host);
    }

    [AfterFeature("api")]
    public static async Task StopApiHostAsync(FeatureContext featureContext)
    {
        if (featureContext.TryGetValue(out ApiTestHost host))
            await ((IAsyncLifetime)host).DisposeAsync();
    }

    [AfterScenario("api")]
    public static async Task DeleteScenarioShipmentsAsync(FeatureContext featureContext, ApiScenarioState state)
    {
        if (state.CreatedTrackingNumbers.Count == 0 || !featureContext.TryGetValue(out ApiTestHost host))
            return;

        var trackingNumbers = state.CreatedTrackingNumbers.ToArray();
        await using var scope = host.Services.CreateAsyncScope();
        // Metadata first: its scans are removed by the ON DELETE CASCADE on ShipmentApiScans.
        await scope.ServiceProvider.GetRequiredService<ApexPostApiDbContext>().ShipmentMetadata
            .Where(x => trackingNumbers.Contains(x.TrackingNumber)).ExecuteDeleteAsync();
        await scope.ServiceProvider.GetRequiredService<ApexPostDbContext>().Shipments
            .Where(x => trackingNumbers.Contains(x.Id)).ExecuteDeleteAsync();
    }
}
