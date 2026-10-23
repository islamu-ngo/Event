using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using Explore.Blazor.Services;
using Explore.Blazor.Services.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Explore.Blazor.IntegrationTests.Services;

public sealed class BffSessionAuthorityForwardingTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task LivePeerCircuitStopsForwardingAfterExactSessionRevocation(bool explicitBearer)
    {
        var authority = new SuppliedSessionAuthority();
        var principal = CreatePrincipal(Guid.NewGuid().ToString("D"));
        var binding = Binding(principal);
        authority.Active.Add(binding);
        var token = CreateToken();
        var context = CreateContext(principal, token, authority);
        var accessor = new HttpContextAccessor { HttpContext = context };
        var store = new CircuitTokenStore(NullLogger<CircuitTokenStore>.Instance);
        var local = new CircuitAccessTokenService(store, accessor, NullLogger<CircuitAccessTokenService>.Instance);
        local.SetToken(token);
        accessor.HttpContext = null;
        using var services = new ServiceCollection().AddSingleton<IBffSessionAuthority>(authority).BuildServiceProvider();
        var terminal = new CaptureHandler();
        using var handler = ActivatorUtilities.CreateInstance<AccessTokenForwardingHandler>(services,
            accessor, local, new CircuitUserContext(), store, NullLogger<AccessTokenForwardingHandler>.Instance);
        handler.InnerHandler = terminal;
        using var client = new HttpMessageInvoker(handler);

        using var admittedRequest = Request();
        using var admitted = await client.SendAsync(admittedRequest, CancellationToken.None);
        await Assert.That(terminal.LastBearer == token).IsTrue();
        authority.Active.Remove(binding);
        terminal.LastBearer = null;
        using var staleRequest = Request();
        if (explicitBearer) staleRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var denied = await client.SendAsync(staleRequest, CancellationToken.None);

        await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(terminal.LastBearer is null).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task MissingOrUnavailableAuthorityCannotForwardHttpContextToken(bool unavailable)
    {
        var authority = new SuppliedSessionAuthority { Unavailable = unavailable };
        var principal = CreatePrincipal(Guid.NewGuid().ToString("D"));
        using var result = await ForwardAsync(CreateContext(principal, CreateToken(), authority));

        await Assert.That(result.StatusCode).IsEqualTo(unavailable
            ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.Unauthorized);
        await Assert.That(result.RequestMessage?.Headers.Authorization is null).IsTrue();
    }

    [Test]
    public async Task MissingBindingCannotBorrowOtherBrowserTokenForSameOwner()
    {
        var authority = new SuppliedSessionAuthority();
        var owner = Guid.NewGuid().ToString("D");
        var first = CreatePrincipal(owner);
        var second = CreatePrincipal(owner);
        authority.Active.Add(Binding(first));
        authority.Active.Add(Binding(second));
        var store = new CircuitTokenStore(NullLogger<CircuitTokenStore>.Instance);
        var accessor = new HttpContextAccessor { HttpContext = CreateContext(first, null, authority) };
        new CircuitAccessTokenService(store, accessor, NullLogger<CircuitAccessTokenService>.Instance).SetToken(CreateToken());

        using var exactMiss = await ForwardAsync(CreateContext(second, null, authority), store);
        await Assert.That(exactMiss.RequestMessage?.Headers.Authorization is null).IsTrue();

        var secondToken = CreateToken();
        accessor.HttpContext = CreateContext(second, null, authority);
        new CircuitAccessTokenService(store, accessor, NullLogger<CircuitAccessTokenService>.Instance).SetToken(secondToken);
        authority.Active.Remove(Binding(first));
        using var unaffected = await ForwardAsync(CreateContext(second, null, authority), store);
        await Assert.That(unaffected.RequestMessage?.Headers.Authorization?.Parameter == secondToken).IsTrue();

        ((ClaimsIdentity)second.Identity!).RemoveClaim(second.FindFirst(BffSessionIdentity.HandleClaim)!);
        using var missing = await ForwardAsync(CreateContext(second, null, authority), store);
        await Assert.That(missing.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(missing.RequestMessage?.Headers.Authorization is null).IsTrue();
    }

    [Test]
    public async Task CapturedActivityBindingRejectsRevocationAcrossPooledHandlerScopes()
    {
        var authority = new SuppliedSessionAuthority();
        var binding = Binding(CreatePrincipal(Guid.NewGuid().ToString("D")));
        authority.Active.Add(binding);
        var store = new CircuitTokenStore(NullLogger<CircuitTokenStore>.Instance);
        var token = CreateToken();
        store.Store(binding.OwnerPartitionKey, binding.Handle, token);
        var captured = new CircuitUserContext();
        captured.SetUserId(binding.OwnerPartitionKey);
        captured.SetSessionBinding(binding);
        using var activity = captured.BeginActivityScope();
        using var services = new ServiceCollection().AddSingleton<IBffSessionAuthority>(authority).BuildServiceProvider();
        var accessor = new HttpContextAccessor();
        using var handler = ActivatorUtilities.CreateInstance<AccessTokenForwardingHandler>(services,
            accessor, new CircuitAccessTokenService(store, accessor, NullLogger<CircuitAccessTokenService>.Instance),
            new CircuitUserContext(), store, NullLogger<AccessTokenForwardingHandler>.Instance);
        var terminal = new CaptureHandler();
        handler.InnerHandler = terminal;
        using var client = new HttpMessageInvoker(handler);
        using var admittedRequest = Request();
        using var admitted = await client.SendAsync(admittedRequest, CancellationToken.None);
        await Assert.That(terminal.LastBearer == token).IsTrue();
        authority.Active.Remove(binding);
        terminal.LastBearer = null;

        using var deniedRequest = Request();
        using var denied = await client.SendAsync(deniedRequest, CancellationToken.None);

        await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(terminal.LastBearer is null).IsTrue();
    }

    [Test]
    public async Task ExplicitPreCookieBearerAndAnonymousRequestsNeedNoSessionAuthority()
    {
        var context = new DefaultHttpContext { RequestServices = new ServiceCollection().BuildServiceProvider() };
        using var challenge = Request();
        challenge.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken());
        using var explicitResult = await ForwardAsync(context, request: challenge);
        await Assert.That(explicitResult.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(explicitResult.RequestMessage?.Headers.Authorization?.Parameter == challenge.Headers.Authorization.Parameter).IsTrue();
        using var anonymous = await ForwardAsync(context);
        await Assert.That(anonymous.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(anonymous.RequestMessage?.Headers.Authorization is null).IsTrue();

        var backgroundToken = CreateToken();
        var store = new CircuitTokenStore(NullLogger<CircuitTokenStore>.Instance);
        var background = new CircuitAccessTokenService(store, new HttpContextAccessor(), NullLogger<CircuitAccessTokenService>.Instance);
        background.SetToken(backgroundToken);
        using var backgroundResult = await ForwardAsync(context, store, localTokenService: background);
        await Assert.That(backgroundResult.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(backgroundResult.RequestMessage?.Headers.Authorization?.Parameter == backgroundToken).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task MissingCanonicalOwnerOrMismatchedCapturedOwnerNeverForwards(bool missingCanonicalOwner)
    {
        var authority = new SuppliedSessionAuthority();
        var principal = CreatePrincipal(Guid.NewGuid().ToString("D"));
        var binding = Binding(principal);
        authority.Active.Add(binding);
        var circuit = new CircuitUserContext();
        if (missingCanonicalOwner)
        {
            ((ClaimsIdentity)principal.Identity!).RemoveClaim(principal.FindFirst("sub")!);
        }
        else
        {
            circuit.SetSessionBinding(binding);
            circuit.SetUserId(Guid.NewGuid().ToString("D"));
        }

        var context = CreateContext(principal, CreateToken(), authority);
        var accessor = new HttpContextAccessor { HttpContext = context };
        var store = new CircuitTokenStore(NullLogger<CircuitTokenStore>.Instance);
        using var handler = ActivatorUtilities.CreateInstance<AccessTokenForwardingHandler>(context.RequestServices,
            accessor, new CircuitAccessTokenService(store, accessor, NullLogger<CircuitAccessTokenService>.Instance),
            circuit, store, NullLogger<AccessTokenForwardingHandler>.Instance);
        var terminal = new CaptureHandler();
        handler.InnerHandler = terminal;
        using var client = new HttpMessageInvoker(handler);
        using var request = Request();
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken());

        using var denied = await client.SendAsync(request, CancellationToken.None);

        await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(terminal.LastBearer is null).IsTrue();
    }

    [Test]
    public async Task ChangedHttpContextBindingCannotUseOriginalCircuitToken()
    {
        var authority = new SuppliedSessionAuthority();
        var owner = Guid.NewGuid().ToString("D");
        var original = CreatePrincipal(owner);
        var replacement = CreatePrincipal(owner);
        authority.Active.UnionWith([Binding(original), Binding(replacement)]);
        var originalContext = CreateContext(original, null, authority);
        var accessor = new HttpContextAccessor { HttpContext = originalContext };
        var store = new CircuitTokenStore(NullLogger<CircuitTokenStore>.Instance);
        var local = new CircuitAccessTokenService(store, accessor, NullLogger<CircuitAccessTokenService>.Instance);
        local.SetToken(CreateToken());
        accessor.HttpContext = CreateContext(replacement, null, authority);
        using var handler = ActivatorUtilities.CreateInstance<AccessTokenForwardingHandler>(originalContext.RequestServices,
            accessor, local, new CircuitUserContext(), store, NullLogger<AccessTokenForwardingHandler>.Instance);
        var terminal = new CaptureHandler();
        handler.InnerHandler = terminal;
        using var client = new HttpMessageInvoker(handler);

        using var deniedRequest = Request();
        using var denied = await client.SendAsync(deniedRequest, CancellationToken.None);

        await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(terminal.LastBearer is null).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ReusedHandlerRefreshKeepsBrowsersIndependent(bool sameOwner)
    {
        var authority = new SuppliedSessionAuthority();
        var owner = Guid.NewGuid().ToString("D");
        var first = CreatePrincipal(owner);
        var second = CreatePrincipal(sameOwner ? owner : Guid.NewGuid().ToString("D"));
        authority.Active.UnionWith([Binding(first), Binding(second)]);
        var firstToken = CreateToken();
        var secondToken = CreateToken();
        var firstContext = CreateContext(first, null, authority, refreshedToken: firstToken);
        var secondContext = CreateContext(second, secondToken, authority);
        var accessor = new HttpContextAccessor { HttpContext = firstContext };
        var store = new CircuitTokenStore(NullLogger<CircuitTokenStore>.Instance);
        var handlerScopeTokens = new CircuitAccessTokenService(store, accessor, NullLogger<CircuitAccessTokenService>.Instance);
        using var handler = ActivatorUtilities.CreateInstance<AccessTokenForwardingHandler>(firstContext.RequestServices,
            accessor, handlerScopeTokens, new CircuitUserContext(), store, NullLogger<AccessTokenForwardingHandler>.Instance);
        var terminal = new CaptureHandler();
        handler.InnerHandler = terminal;
        using var client = new HttpMessageInvoker(handler);
        using var firstRequest = Request();
        using var firstResponse = await client.SendAsync(firstRequest, CancellationToken.None);
        await Assert.That(firstResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(terminal.LastBearer == firstToken).IsTrue();

        accessor.HttpContext = secondContext;
        using var secondRequest = Request();
        using var secondResponse = await client.SendAsync(secondRequest, CancellationToken.None);
        await Assert.That(secondResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(terminal.LastBearer == secondToken).IsTrue();

        authority.Active.Remove(Binding(first));
        accessor.HttpContext = firstContext;
        terminal.LastBearer = null;
        using var revokedRequest = Request();
        using var revokedResponse = await client.SendAsync(revokedRequest, CancellationToken.None);
        await Assert.That(revokedResponse.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(terminal.LastBearer is null).IsTrue();

        accessor.HttpContext = secondContext;
        using var survivingRequest = Request();
        using var survivingResponse = await client.SendAsync(survivingRequest, CancellationToken.None);
        await Assert.That(survivingResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(terminal.LastBearer == secondToken).IsTrue();
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task SecondaryAuthenticatedIdentityRetainsSessionAuthority(bool revoked, bool detachedCircuit)
    {
        var authenticated = CreatePrincipal(Guid.NewGuid().ToString("D"));
        var principal = new ClaimsPrincipal(new[] { new ClaimsIdentity(), (ClaimsIdentity)authenticated.Identity! });
        var binding = Binding(principal);
        var authority = new SuppliedSessionAuthority();
        if (!revoked) authority.Active.Add(binding);
        var token = CreateToken();
        var context = CreateContext(principal, token, authority);
        var accessor = new HttpContextAccessor { HttpContext = context };
        var store = new CircuitTokenStore(NullLogger<CircuitTokenStore>.Instance);
        var local = new CircuitAccessTokenService(store, accessor, NullLogger<CircuitAccessTokenService>.Instance);
        if (detachedCircuit)
        {
            local.SetToken(token);
            accessor.HttpContext = null;
        }

        using var handler = ActivatorUtilities.CreateInstance<AccessTokenForwardingHandler>(context.RequestServices,
            accessor, local, new CircuitUserContext(), store, NullLogger<AccessTokenForwardingHandler>.Instance);
        var terminal = new CaptureHandler();
        handler.InnerHandler = terminal;
        using var client = new HttpMessageInvoker(handler);
        using var request = Request();
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.SendAsync(request, CancellationToken.None);

        await Assert.That(response.StatusCode).IsEqualTo(revoked ? HttpStatusCode.Unauthorized : HttpStatusCode.OK);
        await Assert.That(terminal.LastBearer == (revoked ? null : token)).IsTrue();
    }

    private static async Task<HttpResponseMessage> ForwardAsync(
        HttpContext context, ICircuitTokenStore? store = null, HttpRequestMessage? request = null,
        ICircuitAccessTokenService? localTokenService = null)
    {
        var accessor = new HttpContextAccessor { HttpContext = context };
        store ??= new CircuitTokenStore(NullLogger<CircuitTokenStore>.Instance);
        var local = localTokenService ?? new CircuitAccessTokenService(store, accessor, NullLogger<CircuitAccessTokenService>.Instance);
        using var handler = ActivatorUtilities.CreateInstance<AccessTokenForwardingHandler>(context.RequestServices,
            accessor, local, new CircuitUserContext(), store, NullLogger<AccessTokenForwardingHandler>.Instance);
        handler.InnerHandler = new CaptureHandler();
        using var client = new HttpMessageInvoker(handler);
        using var ownedRequest = request is null ? Request() : null;
        return await client.SendAsync(request ?? ownedRequest!, CancellationToken.None);
    }

    private static HttpRequestMessage Request() => new(HttpMethod.Get, "https://api.example.test/api/protected");

    private static ClaimsPrincipal CreatePrincipal(string owner) => new(new ClaimsIdentity([
        new Claim("sub", owner),
        new Claim(BffSessionIdentity.HandleClaim, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)))
    ], "Cookies"));

    private static BffSessionBinding Binding(ClaimsPrincipal principal)
    {
        if (!BffSessionIdentity.TryGetBinding(principal, out var binding)) throw new InvalidOperationException();
        return binding;
    }

    private static DefaultHttpContext CreateContext(ClaimsPrincipal principal, string? token, IBffSessionAuthority authority,
        string? refreshedToken = null)
    {
        var properties = new AuthenticationProperties();
        if (token is not null) properties.StoreTokens([new AuthenticationToken { Name = "access_token", Value = token }]);
        var authentication = Substitute.For<IAuthenticationService>();
        var initial = AuthenticateResult.Success(new AuthenticationTicket(principal, properties, "Cookies"));
        if (refreshedToken is not null)
        {
            var refreshedProperties = new AuthenticationProperties();
            refreshedProperties.StoreTokens([new AuthenticationToken { Name = "access_token", Value = refreshedToken }]);
            authentication.AuthenticateAsync(Arg.Any<HttpContext>(), Arg.Any<string?>()).Returns(initial,
                AuthenticateResult.Success(new AuthenticationTicket(principal, refreshedProperties, "Cookies")));
        }
        else
        {
            authentication.AuthenticateAsync(Arg.Any<HttpContext>(), Arg.Any<string?>()).Returns(initial);
        }
        return new DefaultHttpContext
        {
            User = principal,
            RequestServices = new ServiceCollection().AddSingleton(authority).AddSingleton(authentication).BuildServiceProvider()
        };
    }

    private static string CreateToken() => new JwtSecurityTokenHandler().WriteToken(
        new JwtSecurityToken(claims: [new Claim("sub", Guid.NewGuid().ToString("D"))], expires: DateTime.UtcNow.AddMinutes(30)));

    // These supplied liveness facts exercise forwarding decisions; Redis/native cookie behavior has separate witnesses.
    private sealed class SuppliedSessionAuthority : IBffSessionAuthority
    {
        public HashSet<BffSessionBinding> Active { get; } = [];
        public bool Unavailable { get; init; }
        public Task<bool> IsActiveAsync(BffSessionBinding binding, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Unavailable ? throw new BffSessionAuthorityUnavailableException() : Task.FromResult(Active.Contains(binding));
        }
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public string? LastBearer { get; set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastBearer = request.Headers.Authorization?.Parameter;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request });
        }
    }
}
