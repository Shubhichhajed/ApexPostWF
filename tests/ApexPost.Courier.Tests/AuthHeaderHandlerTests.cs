using System.Net;
using ApexPost.Courier.Auth;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ApexPost.Courier.Tests;

public sealed class AuthHeaderHandlerTests
{
    [Fact]
    public async Task SendAsync_TokenStored_AddsBearerHeader()
    {
        var stub = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        using var invoker = Create(new InMemoryTokenStore("aaa.bbb.ccc"), new SessionEvents(), stub);

        using var response = await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://api.test/api/v2/shipments/APX-12345678"), CancellationToken.None);

        Assert.Equal("Bearer aaa.bbb.ccc", Assert.Single(stub.AuthorizationHeaders));
    }

    [Fact]
    public async Task SendAsync_LoggedOut_SendsNoAuthorizationHeader()
    {
        var stub = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        using var invoker = Create(new InMemoryTokenStore(null), new SessionEvents(), stub);

        using var response = await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://api.test/health"), CancellationToken.None);

        Assert.Null(Assert.Single(stub.AuthorizationHeaders));
    }

    [Fact]
    public async Task SendAsync_Response401_ClearsTokenAndRaisesSessionExpired()
    {
        var store = new InMemoryTokenStore("expired.token.value");
        var events = new SessionEvents();
        var raised = 0;
        events.SessionExpired += (_, _) => raised++;
        var stub = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var invoker = Create(store, events, stub);

        using var response = await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://api.test/api/v2/shipments"), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(store.Token);
        Assert.Equal(1, store.ClearCalls);
        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task SendAsync_Response403_KeepsToken()
    {
        var store = new InMemoryTokenStore("valid.token.value");
        var stub = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.Forbidden));
        using var invoker = Create(store, new SessionEvents(), stub);

        using var response = await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Post, "https://api.test/api/v2/shipments/APX-12345678/scans"), CancellationToken.None);

        Assert.Equal("valid.token.value", store.Token);
    }

    private static HttpMessageInvoker Create(ITokenStore store, SessionEvents events, HttpMessageHandler inner) =>
        new(new AuthHeaderHandler(store, events, NullLogger<AuthHeaderHandler>.Instance) { InnerHandler = inner });
}
