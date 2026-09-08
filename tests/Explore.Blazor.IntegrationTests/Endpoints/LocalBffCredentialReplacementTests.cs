
using System.IdentityModel.Tokens.Jwt;
using System.Buffers;
using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Extensions;
using Explore.Blazor.IntegrationTests.Fixtures;
using Explore.Blazor.Services;
using Event.Web.BffHosting.Security;
using Event.Web.BffHosting.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Protocol;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Net.Http.Headers;

namespace Explore.Blazor.IntegrationTests.Endpoints;

public sealed class LocalBffCredentialReplacementTests
{
    public enum MalformedChallenge
    {
        MissingToken,
        Expired,
        LifetimeTooLong,
        ContradictorySuccess,
        FailureCode,
        OrdinaryToken,
        Profile,
        Roles
    }

    public enum InvalidBrowserBody { MissingPassword, BlankPassword, TooLongPassword, UnknownAuthority }
    public enum InvalidProtectedCookie { Tampered, Expired }
    public enum ChangedAdmission { OtherProvider, MissingProvider, NullProvider, ProviderUnavailable, OnboardingChanged, OnboardingUnavailable }
    public enum ChangedCircuitProvider { Missing, Conflicting }
    public enum RevokedCookieAuthority
    {
        Unauthorized, TransportFailure, MalformedResponse, ServerError, MissingUserId, EmptyUserId,
        MismatchedUserId, MissingToken, OtherUserToken, SameUserExternalToken, MissingPrincipalProvider, ConflictingProtectedProvider
    }

    [Test]
    public async Task CancellationAfterCurrentUserDeserializationDoesNotLogOutOrEvictNativeSession()
    {
        await using var fixture = new Fixture();
        fixture.Transport.OrdinaryLogin = true;
        fixture.Transport.InstanceAdmin = true;
        AuthenticationTicket ticket;
        using (HttpResponseMessage login = await fixture.LoginAsync())
        {
            await Assert.That(login.StatusCode).IsEqualTo(HttpStatusCode.OK);
            ticket = fixture.ReadCookieTicket(login);
        }
        await using (AsyncServiceScope scope = fixture.Services.CreateAsyncScope())
            await Assert.That(await scope.ServiceProvider.GetRequiredService<BffAdminClaimsTransformation>().EnrichPrincipalAsync(
                ticket.Principal, ticket.Properties, forceRefresh: true, cancellationToken: CancellationToken)).IsTrue();
        await Assert.That(ticket.Principal.TryGetAdminSubject(out var adminSubject)).IsTrue();
        await Assert.That(ticket.Principal.TryGetCircuitSubject(out var subject)).IsTrue();
        await Assert.That(ticket.Principal.TryGetSessionId(out var session)).IsTrue();
        var cache = fixture.Services.GetRequiredService<IMemoryCache>();
        string cacheKey = $"BffAdminClaims_{adminSubject.PartitionKey}";
        await Assert.That(cache.TryGetValue(cacheKey, out object? originalAuthority)).IsTrue();
        var store = fixture.Services.GetRequiredService<ICircuitTokenStore>();
        await Assert.That(store.Store(subject.PartitionKey, session.PartitionKey, fixture.Transport.AccessToken).Accepted).IsTrue();
        using var aborted = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        bool disposalObserved = false;
        fixture.Transport.CookieDefect = RevokedCookieAuthority.MismatchedUserId;
        fixture.Transport.CurrentUserResponseDisposed = () => { disposalObserved = true; aborted.Cancel(); };
        HttpContext? requestContext = null;

        bool canceledRequest = false;
        try
        {
            HttpContext completed = await fixture.StatusWithRequestAbortAsync(
                ticket: ticket, requestAborted: aborted.Token, observe: context => requestContext = context);
            canceledRequest = completed.Response.StatusCode == StatusCodes.Status499ClientClosedRequest;
        }
        catch (OperationCanceledException) when (aborted.IsCancellationRequested)
        {
            canceledRequest = true;
        }

        await Assert.That(disposalObserved && aborted.IsCancellationRequested).IsTrue();
        await Assert.That(requestContext).IsNotNull();
        string cookieName = fixture.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme).Cookie.Name!;
        await Assert.That(requestContext!.Response.Headers.SetCookie.Any(value => value is not null
            && SetCookieHeaderValue.Parse(value).Name == cookieName)).IsFalse();
        await Assert.That(cache.TryGetValue(cacheKey, out object? retainedAuthority)
            && ReferenceEquals(originalAuthority, retainedAuthority)).IsTrue();
        await Assert.That(canceledRequest).IsTrue();
        await Assert.That(store.Resolve(subject.PartitionKey, session.PartitionKey).Found).IsTrue();
        await Assert.That(store.ResolveByUserId(subject.PartitionKey).Found).IsTrue();
        fixture.Transport.CurrentUserResponseDisposed = null;
        fixture.Transport.CookieDefect = null;
        using HttpResponseMessage stillAccepted = await fixture.StatusWithTicketAsync(ticket);
        using JsonDocument state = JsonDocument.Parse(await stillAccepted.Content.ReadAsStringAsync(CancellationToken));
        await Assert.That(state.RootElement.GetProperty("isAuthenticated").GetBoolean()).IsTrue();
    }

    [Test]
    public async Task AuthenticatedLocalCircuitWithoutStartupHttpContextCannotBypassSessionValidation()
    {
        await using var fixture = new Fixture(suppressStartupHttpContext: true);
        fixture.Transport.OrdinaryLogin = true;
        using HttpResponseMessage login = await fixture.LoginAsync();
        await Assert.That(login.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await using LiveCircuit circuit = await fixture.OpenCircuitAsync(fixture.ReadCookieTicket(login));
        await Assert.That(circuit.Probe.StartupGap).IsNotNull();
        await Assert.That(circuit.Probe.StartupGap!.ObservedAuthenticatedLocalState).IsTrue();
        await Assert.That(circuit.Probe.StartupGap.SuppressedHttpContext).IsTrue();
        int before = circuit.Probe.Dispatches;

        await circuit.NavigateAsync("missing-startup-authority");

        await Assert.That((await circuit.Probe.Authentication.GetAuthenticationStateAsync()).User.Identity?.IsAuthenticated == true).IsFalse();
        await Assert.That(circuit.Probe.Dispatches).IsEqualTo(before);
        await Assert.That(string.IsNullOrEmpty(circuit.Probe.Tokens.AccessToken)).IsTrue();
        await Assert.That(circuit.Probe.User.UserId).IsNull();
        await Assert.That(circuit.Probe.Cookies.CookieHeader).IsNull();
    }

    [Test]
    [Arguments(ChangedCircuitProvider.Missing)]
    [Arguments(ChangedCircuitProvider.Conflicting)]
    public async Task LiveCircuitProviderChangeCannotReuseItsClonedOriginalAuthority(ChangedCircuitProvider change)
    {
        await using var fixture = new Fixture();
        fixture.Transport.OrdinaryLogin = true;
        using HttpResponseMessage login = await fixture.LoginAsync();
        await Assert.That(login.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await using LiveCircuit circuit = await fixture.OpenCircuitAsync(fixture.ReadCookieTicket(login));
        await circuit.NavigateAsync("before-provider-change");
        ClaimsPrincipal original = (await circuit.Probe.Authentication.GetAuthenticationStateAsync()).User;
        await Assert.That(original.TryGetCircuitSubject(out var subject)).IsTrue();
        await Assert.That(original.TryGetSessionId(out var session)).IsTrue();
        var changed = new ClaimsPrincipal(original.Identities.Select(identity => new ClaimsIdentity(identity)));
        foreach (ClaimsIdentity identity in changed.Identities)
            foreach (Claim claim in identity.FindAll("auth_provider").ToArray()) identity.RemoveClaim(claim);
        if (change == ChangedCircuitProvider.Conflicting)
            changed.Identities.Single(identity => identity.IsAuthenticated).AddClaim(new Claim("auth_provider", "keycloak"));
        await Assert.That(changed.TryGetCircuitSubject(out var unchangedSubject) && unchangedSubject == subject).IsTrue();
        await Assert.That(changed.TryGetSessionId(out var unchangedSession) && unchangedSession == session).IsTrue();
        ((IHostEnvironmentAuthenticationStateProvider)circuit.Probe.Authentication)
            .SetAuthenticationState(Task.FromResult(new AuthenticationState(changed)));
        int before = circuit.Probe.Dispatches;

        await circuit.NavigateAsync("after-provider-change");

        await circuit.Probe.Anonymous.Task.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken);
        await Assert.That((await circuit.Probe.Authentication.GetAuthenticationStateAsync()).User.Identity?.IsAuthenticated == true).IsFalse();
        await Assert.That(circuit.Probe.Dispatches).IsEqualTo(before);
        await Assert.That(string.IsNullOrEmpty(circuit.Probe.Tokens.AccessToken)).IsTrue();
        await Assert.That(circuit.Probe.User.UserId).IsNull();
        await Assert.That(circuit.Probe.Cookies.CookieHeader).IsNull();
    }

    [Test]
    [Arguments(ChangedCircuitProvider.Missing)]
    [Arguments(ChangedCircuitProvider.Conflicting)]
    public async Task ProviderChangeDuringCurrentUserProbeCannotDispatchWithPreviouslyCheckedAuthority(ChangedCircuitProvider change)
    {
        await using var fixture = new Fixture();
        fixture.Transport.OrdinaryLogin = true;
        using HttpResponseMessage login = await fixture.LoginAsync();
        await Assert.That(login.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await using LiveCircuit circuit = await fixture.OpenCircuitAsync(fixture.ReadCookieTicket(login));
        await circuit.NavigateAsync("before-awaited-provider-change");
        ClaimsPrincipal original = (await circuit.Probe.Authentication.GetAuthenticationStateAsync()).User;
        await Assert.That(original.TryGetCircuitSubject(out var subject)).IsTrue();
        await Assert.That(original.TryGetSessionId(out var session)).IsTrue();
        var changed = new ClaimsPrincipal(original.Identities.Select(identity => new ClaimsIdentity(identity)));
        foreach (ClaimsIdentity identity in changed.Identities)
            foreach (Claim claim in identity.FindAll("auth_provider").ToArray()) identity.RemoveClaim(claim);
        if (change == ChangedCircuitProvider.Conflicting)
            changed.Identities.Single(identity => identity.IsAuthenticated).AddClaim(new Claim("auth_provider", "keycloak"));
        await Assert.That(changed.TryGetCircuitSubject(out var unchangedSubject) && unchangedSubject == subject).IsTrue();
        await Assert.That(changed.TryGetSessionId(out var unchangedSession) && unchangedSession == session).IsTrue();
        int before = circuit.Probe.Dispatches;
        fixture.Transport.ArmCurrentUserProbeBarrier();
        Task activity = circuit.NavigateAsync("awaited-provider-change");
        try
        {
            Task first = await Task.WhenAny(activity, fixture.Transport.CurrentUserProbeReached).WaitAsync(CancellationToken);
            await Assert.That(ReferenceEquals(first, fixture.Transport.CurrentUserProbeReached)).IsTrue();
            ((IHostEnvironmentAuthenticationStateProvider)circuit.Probe.Authentication)
                .SetAuthenticationState(Task.FromResult(new AuthenticationState(changed)));
        }
        finally
        {
            fixture.Transport.ReleaseCurrentUserProbe();
            await activity;
        }

        await circuit.Probe.Anonymous.Task.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken);
        await Assert.That((await circuit.Probe.Authentication.GetAuthenticationStateAsync()).User.Identity?.IsAuthenticated == true).IsFalse();
        await Assert.That(circuit.Probe.Dispatches).IsEqualTo(before);
        await Assert.That(string.IsNullOrEmpty(circuit.Probe.Tokens.AccessToken)).IsTrue();
        await Assert.That(circuit.Probe.User.UserId).IsNull();
        await Assert.That(circuit.Probe.Cookies.CookieHeader).IsNull();
    }

    [Test]
    public async Task DisposedCircuitGuardCannotDispatchAnAnonymousActivityWhoseAuthenticationReadWasPending()
    {
        await using var fixture = new Fixture(observeCircuitLifetime: true);
        await using LiveCircuit circuit = await fixture.OpenCircuitAsync(ticket: null);
        await Assert.That((await circuit.Probe.Authentication.GetAuthenticationStateAsync()).User.Identity?.IsAuthenticated == true).IsFalse();
        int opened = circuit.Probe.Dispatches;
        await circuit.NavigateAsync("anonymous-lifetime-control");
        await Assert.That(circuit.Probe.Dispatches > opened).IsTrue();
        await Assert.That(circuit.Probe.StartupGap).IsNotNull();
        var pendingState = new TaskCompletionSource<AuthenticationState>(TaskCreationOptions.RunContinuationsAsynchronously);
        ((IHostEnvironmentAuthenticationStateProvider)circuit.Probe.Authentication).SetAuthenticationState(pendingState.Task);
        circuit.Probe.StartupGap!.ArmIncompleteActivityObservation();
        int before = circuit.Probe.Dispatches;
        Task activity = circuit.NavigateAsync("anonymous-pending-authentication");
        try
        {
            Task first = await Task.WhenAny(activity, circuit.Probe.StartupGap.IncompleteActivityObserved).WaitAsync(CancellationToken);
            await Assert.That(ReferenceEquals(first, circuit.Probe.StartupGap.IncompleteActivityObserved)).IsTrue();
            // This exercises the actual service lifetime boundary, not framework circuit teardown.
            circuit.Probe.TokenHandler.Dispose();
        }
        finally
        {
            pendingState.TrySetResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));
            await activity;
        }

        await Assert.That(circuit.Probe.Dispatches).IsEqualTo(before);
        await Assert.That((await circuit.Probe.Authentication.GetAuthenticationStateAsync()).User.Identity?.IsAuthenticated == true).IsFalse();
    }

    [Test]
    public async Task NativeLocalCircuitStartsAuthenticatedAndDispatchesAnotherRealActivity()
    {
        await using var fixture = new Fixture();
        fixture.Transport.OrdinaryLogin = true;
        using HttpResponseMessage login = await fixture.LoginAsync();
        await Assert.That(login.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await using LiveCircuit circuit = await fixture.OpenCircuitAsync(fixture.ReadCookieTicket(login));
        await Assert.That((await circuit.Probe.Authentication.GetAuthenticationStateAsync()).User.Identity?.IsAuthenticated == true).IsTrue();
        int before = circuit.Probe.Dispatches;

        await circuit.NavigateAsync("control");

        await Assert.That(circuit.Probe.Dispatches > before).IsTrue();
        await Assert.That((await circuit.Probe.Authentication.GetAuthenticationStateAsync()).User.Identity?.IsAuthenticated == true).IsTrue();
        await Assert.That(string.Equals(circuit.Probe.Tokens.AccessToken, fixture.Transport.AccessToken, StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RevokedLiveCircuitCannotDispatchOrAdoptAnotherSessionForTheSameUser(bool newerSameUserSession)
    {
        await using var fixture = new Fixture();
        fixture.Transport.OrdinaryLogin = true;
        AuthenticationTicket firstTicket;
        using (HttpResponseMessage login = await fixture.LoginAsync())
        {
            await Assert.That(login.StatusCode).IsEqualTo(HttpStatusCode.OK);
            firstTicket = fixture.ReadCookieTicket(login);
        }
        await using LiveCircuit circuit = await fixture.OpenCircuitAsync(firstTicket);
        await circuit.NavigateAsync("before-revocation");
        await Assert.That((await circuit.Probe.Authentication.GetAuthenticationStateAsync()).User.Identity?.IsAuthenticated == true).IsTrue();
        await Assert.That(firstTicket.Principal.TryGetCircuitSubject(out var subject)).IsTrue();
        await Assert.That(firstTicket.Principal.TryGetSessionId(out var firstSession)).IsTrue();
        var store = fixture.Services.GetRequiredService<ICircuitTokenStore>();
        AuthenticationTicket? secondTicket = null;
        string? secondSession = null;
        string? secondToken = null;
        if (newerSameUserSession)
        {
            secondToken = fixture.Transport.CreateFreshSameUserAccessToken();
            fixture.Transport.LoginAccessToken = secondToken;
            using HttpResponseMessage secondLogin = await fixture.LoginAsync();
            await Assert.That(secondLogin.StatusCode).IsEqualTo(HttpStatusCode.OK);
            secondTicket = fixture.ReadCookieTicket(secondLogin);
            await Assert.That(secondTicket.Principal.TryGetSessionId(out var resolvedSecondSession)).IsTrue();
            secondSession = resolvedSecondSession.PartitionKey;
            await Assert.That(secondSession == firstSession.PartitionKey).IsFalse();
            await Assert.That(store.Store(subject.PartitionKey, secondSession, secondToken).Accepted).IsTrue();
            store.ClearSession(subject.PartitionKey, firstSession.PartitionKey);
            await Assert.That(string.Equals(store.ResolveByUserId(subject.PartitionKey).Token, secondToken, StringComparison.Ordinal)).IsTrue();
        }
        fixture.Transport.RevokedAccessToken = fixture.Transport.AccessToken;
        int dispatchedBefore = circuit.Probe.Dispatches;

        await circuit.NavigateAsync("after-revocation");

        await circuit.Probe.Anonymous.Task.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken);
        await Assert.That((await circuit.Probe.Authentication.GetAuthenticationStateAsync()).User.Identity?.IsAuthenticated == true).IsFalse();
        await Assert.That(circuit.Probe.Dispatches).IsEqualTo(dispatchedBefore);
        await Assert.That(string.IsNullOrEmpty(circuit.Probe.Tokens.AccessToken)).IsTrue();
        await Assert.That(circuit.Probe.User.UserId).IsNull();
        await Assert.That(circuit.Probe.User.SessionId).IsNull();
        await Assert.That(circuit.Probe.Cookies.CookieHeader).IsNull();
        await Assert.That(store.Resolve(subject.PartitionKey, firstSession.PartitionKey).Found).IsFalse();
        await circuit.NavigateAsync("repeat-revoked-activity");
        await Assert.That(circuit.Probe.Dispatches).IsEqualTo(dispatchedBefore);
        await Assert.That(string.IsNullOrEmpty(circuit.Probe.Tokens.AccessToken)).IsTrue();
        if (secondTicket is not null)
        {
            await Assert.That(string.Equals(store.Resolve(subject.PartitionKey, secondSession).Token, secondToken, StringComparison.Ordinal)).IsTrue();
            using HttpResponseMessage secondStillAccepted = await fixture.StatusWithTicketAsync(secondTicket);
            using JsonDocument status = JsonDocument.Parse(await secondStillAccepted.Content.ReadAsStringAsync(CancellationToken));
            await Assert.That(status.RootElement.GetProperty("isAuthenticated").GetBoolean()).IsTrue();
        }
        else await Assert.That(store.ResolveByUserId(subject.PartitionKey).Found).IsFalse();
    }

    [Test]
    public async Task MatchingCurrentNonAdministratorKeepsNativeLocalCookieAuthenticated()
    {
        await using var fixture = new Fixture();
        fixture.Transport.OrdinaryLogin = true;
        using HttpResponseMessage login = await fixture.LoginAsync();
        await Assert.That(login.StatusCode).IsEqualTo(HttpStatusCode.OK);
        AuthenticationTicket ticket = fixture.ReadCookieTicket(login);
        await Assert.That(ticket.Properties.ExpiresUtc > DateTimeOffset.UtcNow).IsTrue();
        await Assert.That(await fixture.IsAuthenticatedAsync()).IsTrue();
        await Assert.That(ticket.Principal.IsInRole("Admin")).IsFalse();
    }

    [Test]
    [Arguments(RevokedCookieAuthority.Unauthorized)]
    [Arguments(RevokedCookieAuthority.TransportFailure)]
    [Arguments(RevokedCookieAuthority.MalformedResponse)]
    [Arguments(RevokedCookieAuthority.ServerError)]
    [Arguments(RevokedCookieAuthority.MissingUserId)]
    [Arguments(RevokedCookieAuthority.EmptyUserId)]
    [Arguments(RevokedCookieAuthority.MismatchedUserId)]
    [Arguments(RevokedCookieAuthority.MissingToken)]
    [Arguments(RevokedCookieAuthority.OtherUserToken)]
    [Arguments(RevokedCookieAuthority.SameUserExternalToken)]
    [Arguments(RevokedCookieAuthority.MissingPrincipalProvider)]
    [Arguments(RevokedCookieAuthority.ConflictingProtectedProvider)]
    public async Task FreshCurrentUserRejectionInvalidatesUnexpiredLocalCookieAndBothTokenPartitions(RevokedCookieAuthority defect)
    {
        await using var fixture = new Fixture();
        fixture.Transport.OrdinaryLogin = true;
        AuthenticationTicket ticket;
        using (HttpResponseMessage login = await fixture.LoginAsync())
        {
            await Assert.That(login.StatusCode).IsEqualTo(HttpStatusCode.OK);
            ticket = fixture.ReadCookieTicket(login);
        }
        await Assert.That(await fixture.IsAuthenticatedAsync()).IsTrue();
        await Assert.That(ticket.Properties.ExpiresUtc > DateTimeOffset.UtcNow).IsTrue();
        await Assert.That(ticket.Principal.TryGetCircuitSubject(out var subject)).IsTrue();
        await Assert.That(ticket.Principal.TryGetSessionId(out var session)).IsTrue();
        var store = fixture.Services.GetRequiredService<ICircuitTokenStore>();
        await Assert.That(store.Store(subject.PartitionKey, session.PartitionKey, fixture.Transport.AccessToken).Accepted).IsTrue();
        await Assert.That(store.Resolve(subject.PartitionKey, session.PartitionKey).Found).IsTrue();
        await Assert.That(store.ResolveByUserId(subject.PartitionKey).Found).IsTrue();
        fixture.Transport.CookieDefect = defect;
        if (defect == RevokedCookieAuthority.MissingPrincipalProvider)
            foreach (ClaimsIdentity identity in ticket.Principal.Identities)
                foreach (Claim claim in identity.FindAll("auth_provider").ToArray()) identity.RemoveClaim(claim);
        if (defect == RevokedCookieAuthority.ConflictingProtectedProvider)
            ticket.Properties.Items[EventBffAuthenticationConstants.AuthenticationProviderPropertyKey] = "keycloak";
        if (defect is RevokedCookieAuthority.MissingToken or RevokedCookieAuthority.OtherUserToken or RevokedCookieAuthority.SameUserExternalToken)
        {
            List<AuthenticationToken> tokens = ticket.Properties.GetTokens().Where(token => token.Name != "access_token").ToList();
            if (defect != RevokedCookieAuthority.MissingToken)
                tokens.Add(new AuthenticationToken
                {
                    Name = "access_token", Value = defect == RevokedCookieAuthority.OtherUserToken
                        ? fixture.Transport.OtherUserAccessToken : fixture.Transport.SameUserExternalAccessToken
                });
            ticket.Properties.StoreTokens(tokens);
        }

        using HttpResponseMessage rejected = await fixture.StatusWithTicketAsync(ticket);

        using JsonDocument body = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync(CancellationToken));
        await Assert.That(body.RootElement.GetProperty("isAuthenticated").GetBoolean()).IsFalse();
        string cookieName = fixture.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme).Cookie.Name!;
        await Assert.That(rejected.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? cookies)
            && cookies.Select(value => SetCookieHeaderValue.Parse(value)).Any(cookie => cookie.Name == cookieName
                && cookie.Value.Length == 0 && cookie.Expires < DateTimeOffset.UtcNow)).IsTrue();
        await Assert.That(store.Resolve(subject.PartitionKey, session.PartitionKey).Found).IsFalse();
        await Assert.That(store.ResolveByUserId(subject.PartitionKey).Found).IsFalse();
    }

    [Test]
    public async Task MissingLocalSessionIdRejectsCookieWithoutClearingAnotherValidSession()
    {
        await using var fixture = new Fixture();
        fixture.Transport.OrdinaryLogin = true;
        AuthenticationTicket first;
        using (HttpResponseMessage login = await fixture.LoginAsync()) first = fixture.ReadCookieTicket(login);
        await Assert.That(first.Principal.TryGetCircuitSubject(out var subject)).IsTrue();
        string secondToken = fixture.Transport.CreateFreshSameUserAccessToken();
        fixture.Transport.LoginAccessToken = secondToken;
        AuthenticationTicket second;
        using (HttpResponseMessage login = await fixture.LoginAsync()) second = fixture.ReadCookieTicket(login);
        await Assert.That(second.Principal.TryGetSessionId(out var session)).IsTrue();
        var store = fixture.Services.GetRequiredService<ICircuitTokenStore>();
        await Assert.That(store.Store(subject.PartitionKey, session.PartitionKey, secondToken).Accepted).IsTrue();
        foreach (ClaimsIdentity identity in first.Principal.Identities)
            foreach (Claim claim in identity.FindAll(JwtRegisteredClaimNames.Sid).ToArray()) identity.RemoveClaim(claim);

        using HttpResponseMessage rejected = await fixture.StatusWithTicketAsync(first);

        using JsonDocument denied = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync(CancellationToken));
        await Assert.That(denied.RootElement.GetProperty("isAuthenticated").GetBoolean()).IsFalse();
        await Assert.That(string.Equals(store.Resolve(subject.PartitionKey, session.PartitionKey).Token, secondToken, StringComparison.Ordinal)).IsTrue();
        using HttpResponseMessage valid = await fixture.StatusWithTicketAsync(second);
        using JsonDocument accepted = JsonDocument.Parse(await valid.Content.ReadAsStringAsync(CancellationToken));
        await Assert.That(accepted.RootElement.GetProperty("isAuthenticated").GetBoolean()).IsTrue();
    }

    [Test]
    public async Task ChallengeLoginIssuesOnlyRestrictedProtectedCookieAndFixedNavigation()
    {
        await using var fixture = new Fixture();
        DateTimeOffset startedAt = DateTimeOffset.UtcNow;

        using HttpResponseMessage response = await fixture.LoginAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        await Assert.That(body.RootElement.GetProperty("redirectUrl").GetString()).IsEqualTo(BffLocalCredentialEndpoints.PasswordChangePath);
        string browserBody = body.RootElement.GetRawText();
        await Assert.That(browserBody.Contains(fixture.Transport.Challenge, StringComparison.Ordinal)
            || browserBody.Contains(fixture.Transport.AccessToken, StringComparison.Ordinal)).IsFalse();
        SetCookieHeaderValue cookie = response.Headers.GetValues("Set-Cookie").Select(value => SetCookieHeaderValue.Parse(value))
            .Single(value => value.Name == LocalCredentialChallengeCookie.CookieName && value.Value.Length > 0);
        await Assert.That(cookie.HttpOnly && cookie.Secure).IsTrue();
        await Assert.That(cookie.SameSite).IsEqualTo(Microsoft.Net.Http.Headers.SameSiteMode.Strict);
        await Assert.That(cookie.Path.ToString()).IsEqualTo(LocalCredentialChallengeCookie.CookiePath);
        await Assert.That(cookie.Expires.HasValue && cookie.Expires > startedAt
            && cookie.Expires <= startedAt.AddMinutes(5)).IsTrue();
        await Assert.That(cookie.Value.ToString().Contains(fixture.Transport.Challenge, StringComparison.Ordinal)).IsFalse();
        await Assert.That(await fixture.IsAuthenticatedAsync()).IsFalse();
    }

    [Test]
    public async Task ChallengeLoginClearsPreviouslyEstablishedOrdinaryCookieSession()
    {
        await using var fixture = new Fixture();
        fixture.Transport.OrdinaryLogin = true;
        AuthenticationTicket ticket;
        using (HttpResponseMessage ordinary = await fixture.LoginAsync())
        {
            await Assert.That(ordinary.StatusCode).IsEqualTo(HttpStatusCode.OK);
            ticket = fixture.ReadCookieTicket(ordinary);
        }
        await Assert.That(await fixture.IsAuthenticatedAsync()).IsTrue();
        await Assert.That(ticket.Principal.TryGetCircuitSubject(out var subject)).IsTrue();
        await Assert.That(ticket.Principal.TryGetSessionId(out var session)).IsTrue();
        var tokenStore = fixture.Services.GetRequiredService<ICircuitTokenStore>();
        await Assert.That(tokenStore.Store(subject.PartitionKey, session.PartitionKey, fixture.Transport.AccessToken).Accepted).IsTrue();
        await Assert.That(tokenStore.Resolve(subject.PartitionKey, session.PartitionKey).Found).IsTrue();
        fixture.Transport.OrdinaryLogin = false;

        using HttpResponseMessage challenge = await fixture.LoginAsync();

        await Assert.That(challenge.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await fixture.IsAuthenticatedAsync()).IsFalse();
        await Assert.That(tokenStore.Resolve(subject.PartitionKey, session.PartitionKey).Found).IsFalse();
        await Assert.That(tokenStore.ResolveByUserId(subject.PartitionKey).Found).IsFalse();
        await Assert.That(challenge.Headers.GetValues("Set-Cookie").Select(value => SetCookieHeaderValue.Parse(value))
            .Any(cookie => cookie.Name == LocalCredentialChallengeCookie.CookieName && cookie.Value.Length > 0)).IsTrue();
    }

    [Test]
    public async Task ReplacementRequiresNativeAntiforgeryValidation()
    {
        await using var fixture = new Fixture();

        using HttpResponseMessage response = await fixture.Client.PostAsJsonAsync(
            BffLocalCredentialEndpoints.ReplacementPath, new { newPassword = NewPassword() }, CancellationToken);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(response.Headers.CacheControl?.Private).IsEqualTo(true);
        await Assert.That(response.Headers.CacheControl?.NoStore).IsEqualTo(true);
        await Assert.That(fixture.Transport.ReplacementObserved).IsFalse();
    }

    [Test]
    public async Task MissingChallengeCookieCannotUseBrowserAuthorizationInstead()
    {
        await using var fixture = new Fixture();
        using HttpRequestMessage request = await fixture.ReplacementRequestAsync(NewPassword());
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + fixture.Transport.AccessToken);

        using HttpResponseMessage response = await fixture.Client.SendAsync(request, CancellationToken);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(response.Headers.CacheControl?.Private).IsEqualTo(true);
        await Assert.That(response.Headers.CacheControl?.NoStore).IsEqualTo(true);
        await Assert.That(fixture.Transport.ReplacementObserved).IsFalse();
        await Assert.That(await fixture.IsAuthenticatedAsync()).IsFalse();
    }

    [Test]
    public async Task UsernameOnlyLoginReplacesRestrictedCredentialBeforeIssuingEmailFreeCookie()
    {
        await using var fixture = new Fixture();
        fixture.Transport.OmitEmail = true;
        fixture.Transport.ReadyAfterReplacement = true;
        using (HttpResponseMessage challenge = await fixture.LoginAsync())
        {
            await Assert.That(challenge.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(await fixture.IsAuthenticatedAsync()).IsFalse();
        }
        using HttpRequestMessage request = await fixture.ReplacementRequestAsync(NewPassword());
        using HttpResponseMessage replacement = await fixture.Client.SendAsync(request, CancellationToken);
        await Assert.That(replacement.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await fixture.IsAuthenticatedAsync()).IsFalse();
        using HttpResponseMessage fresh = await fixture.LoginAsync();
        await Assert.That(fresh.StatusCode).IsEqualTo(HttpStatusCode.OK);
        AuthenticationTicket ticket = fixture.ReadCookieTicket(fresh);
        await Assert.That(ticket.Principal.HasClaim(claim => claim.Type == ClaimTypes.Email || claim.Type == "email")).IsFalse();
        await Assert.That(await fixture.IsAuthenticatedAsync()).IsTrue();
    }

    [Test]
    public async Task ValidReplacementForwardsOnlyPasswordAndProtectedChallengeThenRequiresFreshLogin()
    {
        await using var fixture = new Fixture();
        using (HttpResponseMessage login = await fixture.LoginAsync())
            await Assert.That(login.StatusCode).IsEqualTo(HttpStatusCode.OK);
        string password = NewPassword();
        using HttpRequestMessage request = await fixture.ReplacementRequestAsync(password);
        string browserAuthorization = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + browserAuthorization);
        request.Headers.TryAddWithoutValidation("X-Tenant-Slug", "untrusted-tenant");
        request.Headers.TryAddWithoutValidation("X-Setup-Secret", Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));

        using HttpResponseMessage response = await fixture.Client.SendAsync(request, CancellationToken);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(fixture.Transport.ReplacementObserved).IsTrue();
        await Assert.That(fixture.Transport.ReplacementPath).IsEqualTo("/api/auth/local/credential-replacement");
        await Assert.That(string.Equals(fixture.Transport.ReplacementAuthorization,
            "Bearer " + fixture.Transport.Challenge, StringComparison.Ordinal)).IsTrue();
        await Assert.That(fixture.Transport.LeakedBrowserHeaders).IsFalse();
        using JsonDocument forwarded = JsonDocument.Parse(fixture.Transport.ReplacementBody!);
        await Assert.That(forwarded.RootElement.EnumerateObject().Count()).IsEqualTo(1);
        await Assert.That(string.Equals(forwarded.RootElement.GetProperty("newPassword").GetString(), password, StringComparison.Ordinal)).IsTrue();
        string browserBody = await response.Content.ReadAsStringAsync(CancellationToken);
        await Assert.That(browserBody.Contains(password, StringComparison.Ordinal)
            || browserBody.Contains(fixture.Transport.Challenge, StringComparison.Ordinal)).IsFalse();
        using JsonDocument body = JsonDocument.Parse(browserBody);
        await Assert.That(body.RootElement.GetProperty("redirectUrl").GetString()).IsEqualTo("/login");
        await Assert.That(await fixture.IsAuthenticatedAsync()).IsFalse();
        using HttpRequestMessage replay = await fixture.ReplacementRequestAsync(NewPassword());
        using HttpResponseMessage denied = await fixture.Client.SendAsync(replay, CancellationToken);
        await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    [Arguments(MalformedChallenge.MissingToken)]
    [Arguments(MalformedChallenge.Expired)]
    [Arguments(MalformedChallenge.LifetimeTooLong)]
    [Arguments(MalformedChallenge.ContradictorySuccess)]
    [Arguments(MalformedChallenge.FailureCode)]
    [Arguments(MalformedChallenge.OrdinaryToken)]
    [Arguments(MalformedChallenge.Profile)]
    [Arguments(MalformedChallenge.Roles)]
    public async Task MalformedBackendChallengeCannotEstablishBrowserAuthority(MalformedChallenge defect)
    {
        await using var fixture = new Fixture();
        fixture.Transport.Malformed = defect;

        using HttpResponseMessage response = await fixture.LoginAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(await fixture.IsAuthenticatedAsync()).IsFalse();
        await Assert.That(response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? values)
            && values.Select(value => SetCookieHeaderValue.Parse(value)).Any(cookie => cookie.Name == LocalCredentialChallengeCookie.CookieName
                && cookie.Value.Length > 0 && cookie.Expires > DateTimeOffset.UtcNow)).IsFalse();
    }

    [Test]
    [Arguments(400, 400, "password_rejected", true)]
    [Arguments(401, 401, "replacement_required", false)]
    [Arguments(409, 409, "replacement_conflict", false)]
    [Arguments(500, 503, "replacement_unavailable", true)]
    [Arguments(302, 503, "replacement_unavailable", true)]
    [Arguments(200, 503, "replacement_unavailable", true)]
    public async Task BackendOutcomeControlsCookieRetentionWithoutDisclosingResponseDetails(
        int backendStatus, int expectedStatus, string expectedCode, bool retained)
    {
        await using var fixture = new Fixture();
        using (HttpResponseMessage login = await fixture.LoginAsync())
            await Assert.That(login.StatusCode).IsEqualTo(HttpStatusCode.OK);
        fixture.Transport.ReplacementStatus = (HttpStatusCode)backendStatus;
        using HttpRequestMessage request = await fixture.ReplacementRequestAsync(NewPassword());

        using HttpResponseMessage response = await fixture.Client.SendAsync(request, CancellationToken);

        await Assert.That(response.StatusCode).IsEqualTo((HttpStatusCode)expectedStatus);
        await Assert.That(response.Headers.CacheControl?.Private).IsEqualTo(true);
        await Assert.That(response.Headers.CacheControl?.NoStore).IsEqualTo(true);
        string body = await response.Content.ReadAsStringAsync(CancellationToken);
        await Assert.That(body.Contains(fixture.Transport.ProviderDetail, StringComparison.Ordinal)
            || body.Contains(fixture.Transport.Challenge, StringComparison.Ordinal)).IsFalse();
        using JsonDocument problem = JsonDocument.Parse(body);
        await Assert.That(problem.RootElement.GetProperty("code").GetString()).IsEqualTo(expectedCode);
        await Assert.That(response.Headers.Location).IsNull();
        fixture.Transport.ReplacementStatus = HttpStatusCode.NoContent;
        using HttpRequestMessage retry = await fixture.ReplacementRequestAsync(NewPassword());
        using HttpResponseMessage retried = await fixture.Client.SendAsync(retry, CancellationToken);
        await Assert.That(retried.StatusCode).IsEqualTo(retained ? HttpStatusCode.OK : HttpStatusCode.Unauthorized);
        await Assert.That(await fixture.IsAuthenticatedAsync()).IsFalse();
    }

    [Test]
    [Arguments(InvalidProtectedCookie.Tampered)]
    [Arguments(InvalidProtectedCookie.Expired)]
    public async Task InvalidProtectedCookieCannotReachCredentialApi(InvalidProtectedCookie defect)
    {
        await using var fixture = new Fixture();
        string cookieValue;
        if (defect == InvalidProtectedCookie.Tampered)
        {
            using HttpResponseMessage login = await fixture.LoginAsync();
            await Assert.That(login.StatusCode).IsEqualTo(HttpStatusCode.OK);
            cookieValue = login.Headers.GetValues("Set-Cookie").Select(value => SetCookieHeaderValue.Parse(value))
                .Single(cookie => cookie.Name == LocalCredentialChallengeCookie.CookieName && cookie.Value.Length > 0).Value.ToString() + "corrupted";
        }
        else
        {
            cookieValue = fixture.Services.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("Explore.Blazor.LocalCredentialChallenge.v1").ToTimeLimitedDataProtector()
                .Protect(fixture.Transport.Challenge, DateTimeOffset.UtcNow.AddMinutes(-1));
        }
        using HttpClient isolated = fixture.CreateClient();
        using HttpRequestMessage request = await fixture.ReplacementRequestAsync(NewPassword(), isolated);
        request.Headers.Add("Cookie", LocalCredentialChallengeCookie.CookieName + "=" + cookieValue);

        using HttpResponseMessage response = await isolated.SendAsync(request, CancellationToken);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(response.Headers.CacheControl?.Private).IsEqualTo(true);
        await Assert.That(response.Headers.CacheControl?.NoStore).IsEqualTo(true);
        await Assert.That(fixture.Transport.ReplacementObserved).IsFalse();
    }

    [Test]
    [Arguments(InvalidBrowserBody.MissingPassword)]
    [Arguments(InvalidBrowserBody.BlankPassword)]
    [Arguments(InvalidBrowserBody.TooLongPassword)]
    [Arguments(InvalidBrowserBody.UnknownAuthority)]
    public async Task InvalidBrowserRequestCannotSupplyAuthorityAndLeavesCookieReusable(InvalidBrowserBody defect)
    {
        await using var fixture = new Fixture();
        using (HttpResponseMessage login = await fixture.LoginAsync())
            await Assert.That(login.StatusCode).IsEqualTo(HttpStatusCode.OK);
        object body = defect switch
        {
            InvalidBrowserBody.MissingPassword => new { },
            InvalidBrowserBody.BlankPassword => new { newPassword = " " },
            InvalidBrowserBody.TooLongPassword => new { newPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(65)) },
            InvalidBrowserBody.UnknownAuthority => new { newPassword = NewPassword(), token = fixture.Transport.AccessToken },
            _ => throw new ArgumentOutOfRangeException(nameof(defect))
        };
        using HttpRequestMessage request = await fixture.ReplacementRequestAsync(NewPassword());
        request.Content = JsonContent.Create(body);

        using HttpResponseMessage response = await fixture.Client.SendAsync(request, CancellationToken);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(response.Headers.CacheControl?.Private).IsEqualTo(true);
        await Assert.That(response.Headers.CacheControl?.NoStore).IsEqualTo(true);
        await Assert.That(fixture.Transport.ReplacementObserved).IsFalse();
        using HttpRequestMessage corrected = await fixture.ReplacementRequestAsync(NewPassword());
        using HttpResponseMessage retried = await fixture.Client.SendAsync(corrected, CancellationToken);
        await Assert.That(retried.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    [Arguments(ChangedAdmission.OtherProvider)]
    [Arguments(ChangedAdmission.MissingProvider)]
    [Arguments(ChangedAdmission.NullProvider)]
    [Arguments(ChangedAdmission.ProviderUnavailable)]
    [Arguments(ChangedAdmission.OnboardingChanged)]
    [Arguments(ChangedAdmission.OnboardingUnavailable)]
    public async Task ReplacementRechecksCurrentAdmissionRatherThanCachedLoginState(ChangedAdmission change)
    {
        await using var fixture = new Fixture(deploymentLocalOverride: false);
        using (HttpResponseMessage login = await fixture.LoginAsync())
            await Assert.That(login.StatusCode).IsEqualTo(HttpStatusCode.OK);
        fixture.Transport.AdmissionChange = change;
        using HttpRequestMessage request = await fixture.ReplacementRequestAsync(NewPassword());

        using HttpResponseMessage response = await fixture.Client.SendAsync(request, CancellationToken);

        await Assert.That(response.IsSuccessStatusCode).IsFalse();
        await Assert.That(fixture.Transport.ReplacementObserved).IsFalse();
        await Assert.That(fixture.Transport.CurrentAdmissionObserved).IsTrue();
        await Assert.That(await fixture.IsAuthenticatedAsync()).IsFalse();
    }

    private static CancellationToken CancellationToken => TestContext.Current!.Execution.CancellationToken;
    private static string NewPassword() => $"Aa1!{Convert.ToHexString(RandomNumberGenerator.GetBytes(24))}";

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly BlazorBffWebApplicationFactory _root = new();
        private readonly WebApplicationFactory<Program> _factory;
        internal ApiTransport Transport { get; } = new();
        internal HttpClient Client { get; }
        internal IServiceProvider Services => _factory.Services;
        private readonly TaskCompletionSource<CircuitProbe> _openedCircuit = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Fixture(bool deploymentLocalOverride = true, bool suppressStartupHttpContext = false, bool observeCircuitLifetime = false)
        {
            _factory = _root.WithWebHostBuilder(builder =>
            {
                if (deploymentLocalOverride) builder.UseSetting("Authentication:Provider", "local");
                builder.ConfigureTestServices(services =>
                {
                    services.PostConfigure<AuthenticationOptions>(options =>
                    {
                        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                        options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                        options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                        options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                    });
                    services.PostConfigure<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme, options =>
                    {
                        options.ForwardAuthenticate = null; options.ForwardChallenge = null;
                        options.ForwardDefault = null; options.ForwardDefaultSelector = null;
                    });
                    services.AddSingleton<IHttpMessageHandlerBuilderFilter>(new ApiTransportFilter(Transport));
                    services.AddHttpClient("ReplacementTestApi", client => client.BaseAddress = new Uri("https://api.example.test"));
                    services.RemoveAll<ILocalAuthClient>();
                    services.AddScoped<ILocalAuthClient>(provider => new LocalAuthClient(
                        provider.GetRequiredService<IHttpClientFactory>().CreateClient("ReplacementTestApi")));
                    services.RemoveAll<IInstanceOnboardingClient>();
                    services.AddScoped<IInstanceOnboardingClient>(provider => new InstanceOnboardingClient(
                        provider.GetRequiredService<IHttpClientFactory>().CreateClient("ReplacementTestApi")));
                    services.RemoveAll<IBffOnboardingStatusProvider>();
                    services.AddSingleton<IBffOnboardingStatusProvider, BffOnboardingStatusProvider>();
                    services.RemoveAll<IDynamicAuthSchemeManager>();
                    services.AddSingleton<IDynamicAuthSchemeManager, DynamicAuthSchemeManager>();
                    services.AddScoped<BffAdminClaimsTransformation>();
                    if (suppressStartupHttpContext || observeCircuitLifetime)
                    {
                        services.AddScoped<StartupHttpContextGap>(provider => new StartupHttpContextGap(
                            accessor: provider.GetRequiredService<IHttpContextAccessor>(),
                            authentication: provider.GetRequiredService<AuthenticationStateProvider>(), suppressContext: suppressStartupHttpContext));
                        services.AddScoped<CircuitHandler>(provider => provider.GetRequiredService<StartupHttpContextGap>());
                    }
                    services.AddScoped<CircuitHandler>(provider => new CircuitProbe(
                        authentication: provider.GetRequiredService<AuthenticationStateProvider>(),
                        tokens: provider.GetRequiredService<ICircuitAccessTokenService>(),
                        user: provider.GetRequiredService<ICircuitUserContext>(),
                        cookies: provider.GetRequiredService<IBffAuthCookieStore>(), opened: _openedCircuit,
                        startupGap: provider.GetService<StartupHttpContextGap>(), scopedServices: provider));
                });
            });
            Client = CreateClient();
        }

        internal HttpClient CreateClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = true
            });

        internal AuthenticationTicket ReadCookieTicket(HttpResponseMessage response)
        {
            CookieAuthenticationOptions options = Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
                .Get(CookieAuthenticationDefaults.AuthenticationScheme);
            string value = response.Headers.GetValues("Set-Cookie").Select(cookie => SetCookieHeaderValue.Parse(cookie))
                .Single(cookie => cookie.Name == options.Cookie.Name).Value.ToString();
            return options.TicketDataFormat.Unprotect(value)
                ?? throw new InvalidOperationException("The native cookie ticket was unavailable.");
        }

        internal async Task<HttpResponseMessage> StatusWithTicketAsync(AuthenticationTicket ticket)
        {
            CookieAuthenticationOptions options = Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
                .Get(CookieAuthenticationDefaults.AuthenticationScheme);
            using HttpClient client = CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, "/auth/status");
            request.Headers.Add("Cookie", options.Cookie.Name + "=" + options.TicketDataFormat.Protect(ticket));
            return await client.SendAsync(request, CancellationToken);
        }

        internal Task<HttpContext> StatusWithRequestAbortAsync(AuthenticationTicket ticket, CancellationToken requestAborted, Action<HttpContext> observe)
        {
            CookieAuthenticationOptions options = Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
                .Get(CookieAuthenticationDefaults.AuthenticationScheme);
            return _factory.Server.SendAsync(context =>
            {
                context.Request.Method = HttpMethods.Get;
                context.Request.Scheme = "https";
                context.Request.Host = new HostString("localhost");
                context.Request.Path = "/auth/status";
                context.Request.Headers.Cookie = options.Cookie.Name + "=" + options.TicketDataFormat.Protect(ticket);
                context.RequestAborted = requestAborted;
                observe(context);
            }, CancellationToken);
        }

        internal async Task<LiveCircuit> OpenCircuitAsync(AuthenticationTicket? ticket)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            CookieAuthenticationOptions options = Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
                .Get(CookieAuthenticationDefaults.AuthenticationScheme);
            string? cookie = ticket is null ? null : options.Cookie.Name + "=" + options.TicketDataFormat.Protect(ticket);
            using HttpClient client = CreateClient();
            if (cookie is not null) client.DefaultRequestHeaders.Add("Cookie", cookie);
            string html = await client.GetStringAsync("/", timeout.Token);
            var descriptors = new List<string>();
            foreach (Match marker in Regex.Matches(html, "<!--Blazor:(?<marker>\\{.*?\\})-->", RegexOptions.CultureInvariant))
            {
                using JsonDocument document = JsonDocument.Parse(marker.Groups["marker"].Value);
                if (document.RootElement.TryGetProperty("type", out JsonElement type) && type.GetString() == "server"
                    && document.RootElement.TryGetProperty("descriptor", out _)) descriptors.Add(marker.Groups["marker"].Value);
            }
            await Assert.That(descriptors.Count > 0).IsTrue();
            using HttpResponseMessage response = await client.PostAsync("/_blazor/negotiate?negotiateVersion=1", content: null, timeout.Token);
            response.EnsureSuccessStatusCode();
            using JsonDocument negotiate = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            string connection = negotiate.RootElement.GetProperty("connectionToken").GetString()!;
            WebSocketClient webSocketClient = _factory.Server.CreateWebSocketClient();
            if (cookie is not null) webSocketClient.ConfigureRequest = request => request.Headers.Cookie = cookie;
            WebSocket socket = await webSocketClient.ConnectAsync(new Uri($"wss://localhost/_blazor?id={Uri.EscapeDataString(connection)}"), timeout.Token);
            try
            {
                await socket.SendAsync(Encoding.UTF8.GetBytes("{\"protocol\":\"blazorpack\",\"version\":1}\u001e"),
                    WebSocketMessageType.Text, true, timeout.Token);
                _ = await LiveCircuit.ReceiveAsync(socket, timeout.Token);
                IHubProtocol protocol = Services.GetServices<IHubProtocol>().Single(candidate => candidate.Name == "blazorpack");
                await LiveCircuit.InvokeAsync(socket: socket, protocol: protocol, method: "StartCircuit",
                    arguments: ["https://localhost/", "https://localhost/", $"[{string.Join(',', descriptors)}]", string.Empty],
                    cancellationToken: timeout.Token);
                CircuitProbe probe = await _openedCircuit.Task.WaitAsync(timeout.Token);
                return new LiveCircuit(socket: socket, protocol: protocol, probe: probe);
            }
            catch { socket.Dispose(); throw; }
        }

        internal async Task<HttpResponseMessage> LoginAsync()
        {
            string csrf = await CsrfAsync();
            using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/auth/local/login")
            {
                Content = JsonContent.Create(new
                {
                    identifier = $"browser-{Guid.CreateVersion7():N}", password = NewPassword(),
                    isPersistent = true, returnUrl = "https://untrusted.example.test/redirect"
                })
            };
            request.Headers.Add("X-CSRF-TOKEN", csrf);
            return await Client.SendAsync(request, CancellationToken);
        }

        internal async Task<HttpRequestMessage> ReplacementRequestAsync(string password, HttpClient? client = null)
        {
            string csrf = await CsrfAsync(client);
            var request = new HttpRequestMessage(HttpMethod.Post, BffLocalCredentialEndpoints.ReplacementPath)
            {
                Content = JsonContent.Create(new { newPassword = password })
            };
            request.Headers.Add("X-CSRF-TOKEN", csrf);
            return request;
        }

        private async Task<string> CsrfAsync(HttpClient? client = null)
        {
            using HttpResponseMessage response = await (client ?? Client).GetAsync("/auth/status", CancellationToken);
            SetCookieHeaderValue cookie = response.Headers.GetValues("Set-Cookie").Select(value => SetCookieHeaderValue.Parse(value))
                .Single(value => value.Name == "XSRF-TOKEN");
            return Uri.UnescapeDataString(cookie.Value.ToString());
        }

        internal async Task<bool> IsAuthenticatedAsync()
        {
            using HttpResponseMessage response = await Client.GetAsync("/auth/status", CancellationToken);
            using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
            return body.RootElement.GetProperty("isAuthenticated").GetBoolean();
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _factory.DisposeAsync();
            await _root.DisposeAsync();
        }
    }

    private sealed class StartupHttpContextGap(
        IHttpContextAccessor accessor, AuthenticationStateProvider authentication, bool suppressContext) : CircuitHandler
    {
        private HttpContext? _saved;
        private int _observeNextActivity;
        private readonly TaskCompletionSource _incompleteActivity = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task IncompleteActivityObserved => _incompleteActivity.Task;
        internal void ArmIncompleteActivityObservation() => Interlocked.Exchange(ref _observeNextActivity, 1);
        internal bool ObservedAuthenticatedLocalState { get; private set; }
        internal bool SuppressedHttpContext { get; private set; }
        public override int Order => int.MinValue;
        public override async Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
        {
            if (!suppressContext) return;
            ClaimsPrincipal principal = (await authentication.GetAuthenticationStateAsync()).User;
            ObservedAuthenticatedLocalState = principal.Identity?.IsAuthenticated == true && principal.HasClaim("auth_provider", "local");
            _saved = accessor.HttpContext;
            SuppressedHttpContext = _saved is not null;
            accessor.HttpContext = null;
        }
        internal void Restore()
        {
            if (suppressContext) accessor.HttpContext = _saved;
        }
        public override Func<CircuitInboundActivityContext, Task> CreateInboundActivityHandler(Func<CircuitInboundActivityContext, Task> next)
            => async activity =>
            {
                Task pending = next(activity);
                if (Interlocked.Exchange(ref _observeNextActivity, 0) == 1 && !pending.IsCompleted)
                    _incompleteActivity.TrySetResult();
                await pending;
            };
    }

    private sealed class CircuitProbe(
        AuthenticationStateProvider authentication,
        ICircuitAccessTokenService tokens,
        ICircuitUserContext user,
        IBffAuthCookieStore cookies,
        TaskCompletionSource<CircuitProbe> opened,
        StartupHttpContextGap? startupGap,
        IServiceProvider scopedServices) : CircuitHandler, IDisposable
    {
        private int _dispatches;
        internal AuthenticationStateProvider Authentication => authentication;
        internal ICircuitAccessTokenService Tokens => tokens;
        internal ICircuitUserContext User => user;
        internal IBffAuthCookieStore Cookies => cookies;
        internal StartupHttpContextGap? StartupGap => startupGap;
        internal TokenCircuitHandler TokenHandler { get; private set; } = null!;
        internal int Dispatches => Volatile.Read(ref _dispatches);
        internal TaskCompletionSource Anonymous { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override int Order => int.MaxValue;

        public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
        {
            startupGap?.Restore();
            TokenHandler = scopedServices.GetServices<CircuitHandler>().OfType<TokenCircuitHandler>().Single();
            authentication.AuthenticationStateChanged += OnAuthenticationChanged;
            opened.TrySetResult(this);
            return Task.CompletedTask;
        }

        private async void OnAuthenticationChanged(Task<AuthenticationState> state)
        {
            if ((await state).User.Identity?.IsAuthenticated != true) Anonymous.TrySetResult();
        }

        public override Func<CircuitInboundActivityContext, Task> CreateInboundActivityHandler(Func<CircuitInboundActivityContext, Task> next)
            => async activity =>
            {
                Interlocked.Increment(ref _dispatches);
                await next(activity);
            };

        public void Dispose() => authentication.AuthenticationStateChanged -= OnAuthenticationChanged;
    }

    private sealed class LiveCircuit(WebSocket socket, IHubProtocol protocol, CircuitProbe probe) : IAsyncDisposable
    {
        internal CircuitProbe Probe => probe;
        internal async Task NavigateAsync(string fragment)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            await InvokeAsync(socket: socket, protocol: protocol, method: "OnLocationChanged",
                arguments: [$"https://localhost/#{fragment}", string.Empty, false], cancellationToken: timeout.Token);
        }

        internal static async Task InvokeAsync(WebSocket socket, IHubProtocol protocol, string method, object?[] arguments,
            CancellationToken cancellationToken)
        {
            string invocationId = Guid.CreateVersion7().ToString("N");
            var writer = new ArrayBufferWriter<byte>();
            protocol.WriteMessage(new InvocationMessage(invocationId, method, arguments), writer);
            await socket.SendAsync(writer.WrittenMemory, WebSocketMessageType.Binary, true, cancellationToken);
            CompletionMessage? completion = null;
            while (completion is null)
            {
                byte[] bytes = await ReceiveAsync(socket, cancellationToken);
                var sequence = new ReadOnlySequence<byte>(bytes);
                while (protocol.TryParseMessage(ref sequence, CircuitInvocationBinder.Instance, out HubMessage? message))
                    if (message is CompletionMessage candidate && candidate.InvocationId == invocationId) completion = candidate;
            }
            await Assert.That(completion.Error).IsNull().Because("The real Blazor hub invocation must complete without a harness error.");
        }

        internal static async Task<byte[]> ReceiveAsync(WebSocket socket, CancellationToken cancellationToken)
        {
            var writer = new ArrayBufferWriter<byte>();
            var buffer = new byte[4096];
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close) throw new InvalidOperationException("The native circuit closed before completing its activity.");
                writer.Write(buffer.AsSpan(0, result.Count));
            } while (!result.EndOfMessage);
            return writer.WrittenSpan.ToArray();
        }

        public ValueTask DisposeAsync()
        {
            socket.Abort();
            socket.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CircuitInvocationBinder : IInvocationBinder
    {
        internal static CircuitInvocationBinder Instance { get; } = new();
        public IReadOnlyList<Type> GetParameterTypes(string methodName) => [];
        public Type GetReturnType(string invocationId) => typeof(string);
        public Type GetStreamItemType(string streamId) => typeof(object);
    }

    private sealed class ApiTransportFilter(ApiTransport transport) : IHttpMessageHandlerBuilderFilter
    {
        public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) => builder =>
        {
            next(builder);
            builder.PrimaryHandler = transport;
        };
    }

    private sealed class ApiTransport : HttpMessageHandler
    {
        private enum TokenProvider { Local, Keycloak }
        private int _pauseNextCurrentUserProbe;
        private TaskCompletionSource _currentUserProbeReached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource _continueCurrentUserProbe = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task CurrentUserProbeReached => _currentUserProbeReached.Task;
        internal void ArmCurrentUserProbeBarrier()
        {
            _currentUserProbeReached = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _continueCurrentUserProbe = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Interlocked.Exchange(ref _pauseNextCurrentUserProbe, 1);
        }
        internal void ReleaseCurrentUserProbe() => _continueCurrentUserProbe.TrySetResult();
        private readonly Guid _userId = Guid.CreateVersion7();
        private readonly Guid _otherUserId = Guid.CreateVersion7();
        internal string Challenge { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(48));
        internal string AccessToken { get; }
        internal string OtherUserAccessToken { get; }
        internal string SameUserExternalAccessToken { get; }
        internal string? LoginAccessToken { get; set; }
        internal string? RevokedAccessToken { get; set; }
        internal bool InstanceAdmin { get; set; }
        internal Action? CurrentUserResponseDisposed { get; set; }
        internal string CreateFreshSameUserAccessToken() => CreateAccessToken(userId: _userId, provider: TokenProvider.Local);
        internal RevokedCookieAuthority? CookieDefect { get; set; }
        internal bool OrdinaryLogin { get; set; }
        internal bool OmitEmail { get; set; }
        internal bool ReadyAfterReplacement { get; set; }
        internal MalformedChallenge? Malformed { get; set; }
        internal bool ReplacementObserved { get; private set; }
        internal string? ReplacementPath { get; private set; }
        internal string? ReplacementAuthorization { get; private set; }
        internal string? ReplacementBody { get; private set; }
        internal bool LeakedBrowserHeaders { get; private set; }
        internal HttpStatusCode ReplacementStatus { get; set; } = HttpStatusCode.NoContent;
        internal string ProviderDetail { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        internal ChangedAdmission? AdmissionChange { get; set; }
        internal bool CurrentAdmissionObserved { get; private set; }

        internal ApiTransport()
        {
            AccessToken = CreateAccessToken(userId: _userId, provider: TokenProvider.Local);
            OtherUserAccessToken = CreateAccessToken(userId: _otherUserId, provider: TokenProvider.Local);
            SameUserExternalAccessToken = CreateAccessToken(userId: _userId, provider: TokenProvider.Keycloak);
        }

        private static string CreateAccessToken(Guid userId, TokenProvider provider)
        {
            bool local = provider == TokenProvider.Local;
            var token = new JwtSecurityToken(issuer: local ? "islamu-event-local" : "https://identity.example.test/realms/event", audience: "islamu-event-api",
                claims: [new Claim("sub", userId.ToString("D")), new Claim("auth_provider", local ? "local" : "keycloak"),
                    new Claim("local_session_stamp", Convert.ToHexString(RandomNumberGenerator.GetBytes(32))),
                    new Claim("email_verified", "true", ClaimValueTypes.Boolean)],
                notBefore: DateTime.UtcNow.AddSeconds(-1), expires: DateTime.UtcNow.AddMinutes(30),
                signingCredentials: new SigningCredentials(new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(64)), SecurityAlgorithms.HmacSha256));
            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string path = request.RequestUri!.AbsolutePath;
            if (path.Equals("/api/user", StringComparison.OrdinalIgnoreCase))
            {
                if (Interlocked.Exchange(ref _pauseNextCurrentUserProbe, 0) == 1)
                {
                    _currentUserProbeReached.TrySetResult();
                    await _continueCurrentUserProbe.Task.WaitAsync(cancellationToken);
                }
                if (RevokedAccessToken is not null && string.Equals(request.Headers.Authorization?.Parameter, RevokedAccessToken, StringComparison.Ordinal))
                    return new HttpResponseMessage(HttpStatusCode.Unauthorized);
                if (CookieDefect == RevokedCookieAuthority.TransportFailure) throw new HttpRequestException("Current-user transport unavailable.");
                if (CookieDefect == RevokedCookieAuthority.Unauthorized) return new HttpResponseMessage(HttpStatusCode.Unauthorized);
                if (CookieDefect == RevokedCookieAuthority.ServerError) return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                if (CookieDefect == RevokedCookieAuthority.MalformedResponse)
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("invalid-json", System.Text.Encoding.UTF8, "application/hal+json") };
                Guid? userId = CookieDefect switch
                {
                    RevokedCookieAuthority.MissingUserId => null,
                    RevokedCookieAuthority.EmptyUserId => Guid.Empty,
                    RevokedCookieAuthority.MismatchedUserId or RevokedCookieAuthority.OtherUserToken => _otherUserId,
                    _ => _userId
                };
                var payload = new
                {
                    id = userId, email = OmitEmail ? null : $"session-{_userId:N}@example.test", firstName = "Local", lastName = "Browser",
                    emailVerified = true, _links = new { self = new { href = "/api/user" } }
                };
                if (CurrentUserResponseDisposed is { } disposed)
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new DisposalSignalingContent(JsonSerializer.SerializeToUtf8Bytes(payload), disposed)
                    };
                return Json(payload);
            }
            if (path.Equals("/api/auth/local/login", StringComparison.OrdinalIgnoreCase))
            {
                using var login = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                if (!login.RootElement.TryGetProperty("identifier", out var identifier)
                    || string.IsNullOrWhiteSpace(identifier.GetString()) || login.RootElement.TryGetProperty("email", out _))
                    return new HttpResponseMessage(HttpStatusCode.BadRequest);
                if (OrdinaryLogin)
                    return Json(new
                    {
                        success = true, userId = _userId, email = OmitEmail ? null : $"session-{_userId:N}@example.test",
                        firstName = "Local", lastName = "Browser", emailVerified = true, roles = Array.Empty<string>(),
                        token = LoginAccessToken ?? AccessToken, expiresAt = DateTimeOffset.UtcNow.AddMinutes(30)
                    });
                DateTimeOffset expiry = Malformed switch
                {
                    MalformedChallenge.Expired => DateTimeOffset.UtcNow.AddMinutes(-1),
                    MalformedChallenge.LifetimeTooLong => DateTimeOffset.UtcNow.AddMinutes(6),
                    _ => DateTimeOffset.UtcNow.AddMinutes(4)
                };
                return Json(new
                {
                    success = Malformed == MalformedChallenge.ContradictorySuccess,
                    failureCode = Malformed == MalformedChallenge.FailureCode ? "invalid_credentials" : string.Empty,
                    token = Malformed == MalformedChallenge.OrdinaryToken ? AccessToken : null,
                    email = Malformed == MalformedChallenge.Profile ? $"profile-{_userId:N}@example.test" : null,
                    roles = Malformed == MalformedChallenge.Roles ? new[] { "Admin" } : Array.Empty<string>(), emailVerified = false,
                    replacementChallenge = new { token = Malformed == MalformedChallenge.MissingToken ? null : Challenge, expiresAt = expiry }
                });
            }
            if (path.Equals("/api/auth/local/credential-replacement", StringComparison.OrdinalIgnoreCase))
            {
                ReplacementObserved = true;
                ReplacementPath = path;
                ReplacementAuthorization = request.Headers.Authorization?.ToString();
                ReplacementBody = await request.Content!.ReadAsStringAsync(cancellationToken);
                LeakedBrowserHeaders = request.Headers.Contains("Cookie") || request.Headers.Contains("X-Tenant-Slug")
                    || request.Headers.Contains("X-Setup-Secret") || request.Headers.Contains("X-CSRF-TOKEN");
                if (ReplacementStatus == HttpStatusCode.NoContent)
                {
                    if (ReadyAfterReplacement) OrdinaryLogin = true;
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                }
                var reply = new HttpResponseMessage(ReplacementStatus)
                {
                    Content = JsonContent.Create(new { detail = ProviderDetail, token = Challenge })
                };
                if (ReplacementStatus == HttpStatusCode.Found) reply.Headers.Location = new Uri("https://untrusted.example.test/redirect");
                return reply;
            }
            if (path.Equals("/api/InstanceOnboarding/status", StringComparison.OrdinalIgnoreCase))
            {
                if (AdmissionChange == ChangedAdmission.OnboardingUnavailable)
                {
                    CurrentAdmissionObserved = true;
                    return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                }
                if (AdmissionChange == ChangedAdmission.OnboardingChanged)
                {
                    CurrentAdmissionObserved = true;
                    return Json(new { isCompleted = false, state = "ConfiguredAdministratorPending", mode = "ConfiguredAdministrator", provider = "Keycloak", generation = 2 });
                }
                return Json(new { isCompleted = true, state = "Completed", mode = "Interactive", generation = 1, selectedDeploymentMode = "SingleTenant" });
            }
            if (path.Equals("/api/instanceonboarding/auth-provider-configuration", StringComparison.OrdinalIgnoreCase))
            {
                if (AdmissionChange.HasValue) CurrentAdmissionObserved = true;
                return AdmissionChange switch
                {
                    ChangedAdmission.OtherProvider => Json(new { primaryProviderId = 1 }),
                    ChangedAdmission.MissingProvider => Json(new { }),
                    ChangedAdmission.NullProvider => Json(new { primaryProviderId = (int?)null }),
                    ChangedAdmission.ProviderUnavailable => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
                    _ => Json(new { primaryProviderId = 4 })
                };
            }
            if (path.EndsWith("/admin-authority", StringComparison.OrdinalIgnoreCase))
                return Json(new { isInstanceAdmin = InstanceAdmin, hasAnyAuthority = InstanceAdmin,
                    tenantAdminIds = Array.Empty<Guid>(), organizationAdminIds = Array.Empty<Guid>(), groupAdminIds = Array.Empty<Guid>() });
            if (path.EndsWith("/sync", StringComparison.OrdinalIgnoreCase))
                return Json(new { isSuccess = true, id = _userId });
            return Json(new { primaryProviderId = 4, atprotoLoginEnabled = false });
        }

        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }

    private sealed class DisposalSignalingContent(byte[] body, Action disposed) : ByteArrayContent(body)
    {
        private int _signaled;
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && Interlocked.Exchange(ref _signaled, 1) == 0) disposed();
        }
    }
}
