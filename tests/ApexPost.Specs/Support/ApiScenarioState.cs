using System.Net;
using System.Text.Json;
using ApexPost.Api.IntegrationTests.Infrastructure;
using Reqnroll;

namespace ApexPost.Specs.Support;

/// <summary>
/// Per-scenario state shared between step classes through Reqnroll context injection
/// (a new instance for every scenario; no static fields).
/// </summary>
public sealed class ApiScenarioState(FeatureContext featureContext)
{
    public ApiTestHost Host => featureContext.Get<ApiTestHost>();

    /// <summary>Test identity sent to the API: "Admin" or "Courier:{courierId}".</summary>
    public string? User { get; set; }

    public HttpStatusCode? LastStatus { get; set; }
    public JsonElement LastBody { get; set; }

    /// <summary>Everything this scenario created, deleted again by the AfterScenario hook.</summary>
    public List<string> CreatedTrackingNumbers { get; } = [];

    public HttpClient CreateClient() => Host.CreateClientAs(User);
}
