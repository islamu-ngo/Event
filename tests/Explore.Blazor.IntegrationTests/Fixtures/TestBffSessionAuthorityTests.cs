using System.Security.Claims;
using Explore.Blazor.Services.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Explore.Blazor.IntegrationTests.Fixtures;

public sealed class TestBffSessionAuthorityTests
{
    [Test]
    public async Task FreshNativeSignInPreservesOriginalTicketAndReferenceSession()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        services.AddAuthentication("Cookies").AddCookie("Cookies");
        var authority = new TestBffSessionAuthority();
        services.AddTestBffSessionAuthority(authority);
        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get("Cookies");
        string originalCookie = await TestBffSessionAuthorityRegistration.IssueFreshCookieAsync(
            provider, Ticket(DateTimeOffset.UtcNow.AddMinutes(5)));
        var originalContext = new DefaultHttpContext();
        originalContext.Request.Headers.Cookie = originalCookie;
        var original = await options.ReadStoredTicketAsync(originalContext)
            ?? throw new InvalidOperationException("fixture_original_session_missing");
        var originalBinding = Binding(original);
        string? originalRevision = original.Properties.Items[BffSessionIdentity.RevisionProperty];
        string? originalHandle = original.Properties.Items[BffSessionIdentity.HandleProperty];

        string freshCookie = await TestBffSessionAuthorityRegistration.IssueFreshCookieAsync(provider, original);

        await Assert.That(Binding(original) == originalBinding).IsTrue();
        await Assert.That(original.Properties.Items[BffSessionIdentity.RevisionProperty] == originalRevision).IsTrue();
        await Assert.That(original.Properties.Items[BffSessionIdentity.HandleProperty] == originalHandle).IsTrue();
        var freshContext = new DefaultHttpContext();
        freshContext.Request.Headers.Cookie = freshCookie;
        var fresh = await options.ReadStoredTicketAsync(freshContext)
            ?? throw new InvalidOperationException("fixture_fresh_session_missing");
        await Assert.That(Binding(fresh) != originalBinding).IsTrue();
        var retained = await options.ReadStoredTicketAsync(originalContext)
            ?? throw new InvalidOperationException("fixture_retained_session_missing");
        await Assert.That(Binding(retained) == originalBinding).IsTrue();
        await Assert.That(await authority.IsActiveAsync(originalBinding, CancellationToken.None)).IsTrue();
        await Assert.That(await authority.IsActiveAsync(Binding(fresh), CancellationToken.None)).IsTrue();
    }

    [Test]
    public async Task RemovedSessionCannotBeRetrievedRenewedOrRecreatedFromRetainedTicket()
    {
        var authority = new TestBffSessionAuthority();
        var ticket = Ticket(DateTimeOffset.UtcNow.AddMinutes(5));
        var handle = await authority.StoreAsync(ticket);
        var retained = (await authority.RetrieveAsync(handle))!;
        await authority.RemoveAsync(handle);
        await Assert.That(await authority.RetrieveAsync(handle)).IsNull();
        await Assert.ThrowsAsync<InvalidOperationException>(() => authority.RenewAsync(handle, retained));
        await Assert.ThrowsAsync<InvalidOperationException>(() => authority.StoreAsync(retained));
        await Assert.That(await authority.IsActiveAsync(Binding(ticket), CancellationToken.None)).IsFalse();
    }

    [Test]
    public async Task OlderRevisionCannotOverwriteNewTokensAndRetrievedTicketsAreIsolated()
    {
        var authority = new TestBffSessionAuthority();
        var handle = await authority.StoreAsync(Ticket(DateTimeOffset.UtcNow.AddMinutes(5)));
        var first = (await authority.RetrieveAsync(handle))!;
        var stale = (await authority.RetrieveAsync(handle))!;
        first.Properties.Items["fixture-state"] = "new";
        await authority.RenewAsync(handle, first);
        stale.Properties.Items["fixture-state"] = "stale";
        await Assert.ThrowsAsync<InvalidOperationException>(() => authority.RenewAsync(handle, stale));
        first.Properties.Items["fixture-state"] = "uncommitted";
        var actual = (await authority.RetrieveAsync(handle))!;
        await Assert.That(actual.Properties.Items["fixture-state"]).IsEqualTo("new");
    }

    [Test]
    public async Task ExpiryAndChangedOwnerOrProviderFailClosed()
    {
        var clock = new FixtureClock();
        var authority = new TestBffSessionAuthority(clock);
        var handle = await authority.StoreAsync(Ticket(clock.GetUtcNow().AddMinutes(1)));
        var changedOwner = (await authority.RetrieveAsync(handle))!;
        var identity = (ClaimsIdentity)changedOwner.Principal.Identity!;
        identity.RemoveClaim(identity.FindFirst(ClaimTypes.NameIdentifier)!);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, Guid.CreateVersion7().ToString()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => authority.RenewAsync(handle, changedOwner));
        var changedProvider = (await authority.RetrieveAsync(handle))!;
        ((ClaimsIdentity)changedProvider.Principal.Identity!).AddClaim(new Claim("auth_provider", "other"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => authority.RenewAsync(handle, changedProvider));
        var retained = (await authority.RetrieveAsync(handle))!;
        clock.Advance(TimeSpan.FromMinutes(1));
        await Assert.That(await authority.RetrieveAsync(handle)).IsNull();
        await Assert.ThrowsAsync<InvalidOperationException>(() => authority.RenewAsync(handle, retained));
        await Assert.That(await authority.IsActiveAsync(Binding(retained), CancellationToken.None)).IsFalse();
    }

    private static AuthenticationTicket Ticket(DateTimeOffset expiry) => new(
        new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, Guid.CreateVersion7().ToString())], "Cookies")),
        new AuthenticationProperties { ExpiresUtc = expiry }, "Cookies");

    private static BffSessionBinding Binding(AuthenticationTicket ticket)
        => BffSessionIdentity.TryGetBinding(ticket.Principal, out var binding)
            ? binding : throw new InvalidOperationException("fixture_binding_missing");

    private sealed class FixtureClock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan duration) => now += duration;
    }
}
