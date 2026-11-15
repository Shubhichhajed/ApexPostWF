using ApexPost.Domain;
using ApexPost.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ApexPost.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Hosts ApexPost.Api in memory against its own SQL Server database (created by the API's migrations on start-up
/// and dropped on dispose). The default server is LocalDB; set APEXPOST_TEST_CONNECTION to use another instance.
/// Never points at a development or production database: the database name is always unique per host.
/// </summary>
public sealed class ApiTestHost : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string PartnerKey = "partner-key-for-tests";
    public const string BranchCode = "TST01";
    public const string Zone = "ZONE-T1";

    private readonly string _connectionString = BuildConnectionString($"ApexPost_ApiTests_{Guid.NewGuid():N}");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // UseSetting values are visible to Program.cs while it reads configuration, before Build().
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:DefaultConnection", _connectionString);
        builder.UseSetting("Auth:SigningKey", "test-only-signing-key-not-a-secret-0123456789");
        builder.UseSetting("Partner:ApiKey", PartnerKey);
        builder.UseSetting("Database:ApplyMigrationsOnStartup", "true");

        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(options =>
                {
                    options.DefaultScheme = TestAuthHandler.SchemeName;
                    options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                    options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                    options.DefaultForbidScheme = TestAuthHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        });
    }

    /// <summary>HTTPS client that sends the given test identity ("Admin", "Courier:courier-demo-01") or none.</summary>
    public HttpClient CreateClientAs(string? user)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost/"),
            AllowAutoRedirect = false
        });
        if (user is not null)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, user);
        }
        return client;
    }

    /// <summary>Starts the host (which applies the migrations) and seeds the reference data shipments need.</summary>
    public async Task InitializeAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApexPostDbContext>();

        if (!await db.ShippingZones.AnyAsync(x => x.Code == Zone))
        {
            db.ShippingZones.Add(new ShippingZoneRecord { Code = Zone, Region = "Test" });
        }
        if (!await db.ShippingRates.AnyAsync(x => x.Zone == Zone && x.ServiceLevel == ServiceLevel.Standard))
        { db.ShippingRates.Add(new ShipmentRate(Zone, ServiceLevel.Standard, 40m, 10m, 70m)); }
        if (!await db.Branches.AnyAsync(x => x.Code == BranchCode))
        { db.Branches.Add(new Branch { Code = BranchCode, Name = "Test Branch", Region = "Test", OpeningHours = "09:00-17:00", Status = BranchStatus.Active }); }
        await db.SaveChangesAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        try
        {
            await using var scope = Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ApexPostDbContext>().Database.EnsureDeletedAsync();
        }
        finally
        {
            await DisposeAsync();
        }
    }

    private static string BuildConnectionString(string databaseName)
    {
        var configured = Environment.GetEnvironmentVariable("APEXPOST_TEST_CONNECTION")
            ?? @"Server=(localdb)\MSSQLLocalDB;Database=master;Trusted_Connection=True;TrustServerCertificate=True";
        return new SqlConnectionStringBuilder(configured) { InitialCatalog = databaseName }.ConnectionString;
    }
}
