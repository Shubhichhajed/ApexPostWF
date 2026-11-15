using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ApexPost.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Test-only authentication: the caller is described by the X-Test-User header, "Admin" or "Courier:courier-id".
/// No header means anonymous. This replaces JWT validation so tests can exercise the [Authorize] role rules
/// without a signing key or token issuer. JWT validation itself is a framework feature configured in
/// Program.cs and covered by the Week 4 .http checks, not by these tests.
/// </summary>
public sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string UserHeader = "X-Test-User";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var value = Request.Headers[UserHeader].ToString();
        if (string.IsNullOrWhiteSpace(value))
            return Task.FromResult(AuthenticateResult.NoResult());

        var parts = value.Split(':', 2, StringSplitOptions.TrimEntries);
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, parts.Length == 2 ? parts[1] : parts[0]),
            new(ClaimTypes.Role, parts[0])
        };
        if (parts.Length == 2)
            claims.Add(new Claim("courierId", parts[1]));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}
