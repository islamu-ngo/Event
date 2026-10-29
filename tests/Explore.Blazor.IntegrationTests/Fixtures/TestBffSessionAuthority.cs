using System.Security.Claims;
using System.Security.Cryptography;
using Event.Web.BffHosting.Security;
using Explore.Blazor.Services.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Explore.Blazor.IntegrationTests.Fixtures;

/// <summary>
/// Explicit in-process fixture authority. This exercises cookie references and session
/// lifetime, but does not establish Redis availability or distributed concurrency behavior.
/// </summary>
internal sealed class TestBffSessionAuthority(TimeProvider? clock = null) : ITicketStore, IBffSessionAuthority
{
    private static readonly string[] ImmutableClaims = ["auth_provider", "did", "tenant_id", "tenant_slug"];
    private readonly TimeProvider clock = clock ?? TimeProvider.System;
    private readonly Dictionary<string, AuthenticationTicket> tickets = new(StringComparer.Ordinal);
    private readonly object gate = new();

    public Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        lock (gate)
        {
            if (ticket.Properties.Items.ContainsKey(BffSessionIdentity.HandleProperty)
                || ticket.Properties.Items.ContainsKey(BffSessionIdentity.RevisionProperty)
                || !ticket.Principal.TryGetCircuitSubject(out _)
                || ImmutableClaims.Any(type => ticket.Principal.FindAll(type).Skip(1).Any())
                || !IsLive(ticket))
                throw Rejected();

            var handle = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            foreach (var identity in ticket.Principal.Identities)
                foreach (var claim in identity.FindAll(BffSessionIdentity.HandleClaim).ToArray())
                    identity.RemoveClaim(claim);
            var target = ticket.Principal.Identities.First(identity => identity.IsAuthenticated);
            target.AddClaim(new Claim(BffSessionIdentity.HandleClaim, handle));
            ticket.Properties.Items[BffSessionIdentity.HandleProperty] = handle;
            ticket.Properties.Items[BffSessionIdentity.RevisionProperty] = NewRevision();
            tickets.Add(handle, Copy(ticket));
            return Task.FromResult(handle);
        }
    }

    public Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        lock (gate)
        {
            if (!tickets.TryGetValue(key, out var current) || !IsLive(current) || !IsLive(ticket)
                || !MatchesBinding(key, current, ticket)
                || !current.Properties.Items.TryGetValue(BffSessionIdentity.RevisionProperty, out var revision)
                || !ticket.Properties.Items.TryGetValue(BffSessionIdentity.RevisionProperty, out var candidateRevision)
                || string.IsNullOrEmpty(revision) || revision != candidateRevision
                || ImmutableClaims.Any(type => !SameSingleton(current.Principal, ticket.Principal, type)))
                throw Rejected();

            var candidate = Copy(ticket);
            var nextRevision = NewRevision();
            candidate.Properties.Items[BffSessionIdentity.RevisionProperty] = nextRevision;
            tickets[key] = candidate;
            ticket.Properties.Items[BffSessionIdentity.RevisionProperty] = nextRevision;
            return Task.CompletedTask;
        }
    }

    public Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        lock (gate)
        {
            if (!tickets.TryGetValue(key, out var ticket) || !IsLive(ticket)
                || !BffSessionIdentity.TryGetBinding(ticket.Principal, out var binding)
                || binding.Handle != key)
                return Task.FromResult<AuthenticationTicket?>(null);
            return Task.FromResult<AuthenticationTicket?>(Copy(ticket));
        }
    }

    public Task RemoveAsync(string key)
    {
        lock (gate)
            tickets.Remove(key);
        return Task.CompletedTask;
    }

    public async Task<bool> IsActiveAsync(BffSessionBinding binding, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var ticket = await RetrieveAsync(binding.Handle);
        return ticket is not null
            && BffSessionIdentity.TryGetBinding(ticket.Principal, out var current)
            && current == binding;
    }

    public Task<string> StoreAsync(AuthenticationTicket ticket, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return StoreAsync(ticket);
    }

    public Task RenewAsync(string key, AuthenticationTicket ticket, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return RenewAsync(key, ticket);
    }

    public Task<AuthenticationTicket?> RetrieveAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return RetrieveAsync(key);
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return RemoveAsync(key);
    }

    public Task<string> StoreAsync(AuthenticationTicket ticket, HttpContext context, CancellationToken cancellationToken)
    {
        context.RequestAborted.ThrowIfCancellationRequested();
        return StoreAsync(ticket, cancellationToken);
    }

    public Task RenewAsync(string key, AuthenticationTicket ticket, HttpContext context, CancellationToken cancellationToken)
    {
        context.RequestAborted.ThrowIfCancellationRequested();
        return RenewAsync(key, ticket, cancellationToken);
    }

    public Task<AuthenticationTicket?> RetrieveAsync(string key, HttpContext context, CancellationToken cancellationToken)
    {
        context.RequestAborted.ThrowIfCancellationRequested();
        return RetrieveAsync(key, cancellationToken);
    }

    public Task RemoveAsync(string key, HttpContext context, CancellationToken cancellationToken)
    {
        context.RequestAborted.ThrowIfCancellationRequested();
        return RemoveAsync(key, cancellationToken);
    }

    private bool IsLive(AuthenticationTicket ticket) => ticket.Properties.ExpiresUtc is { } expiry
        && expiry > clock.GetUtcNow();

    private static bool MatchesBinding(string key, AuthenticationTicket current, AuthenticationTicket candidate)
        => current.AuthenticationScheme == candidate.AuthenticationScheme
           && BffSessionIdentity.TryGetBinding(current.Principal, out var oldBinding)
           && BffSessionIdentity.TryGetBinding(candidate.Principal, out var newBinding)
           && oldBinding == newBinding && newBinding.Handle == key
           && candidate.Properties.Items.TryGetValue(BffSessionIdentity.HandleProperty, out var handle)
           && handle == key;

    private static bool SameSingleton(ClaimsPrincipal current, ClaimsPrincipal candidate, string type)
    {
        var before = current.FindAll(type).Select(claim => claim.Value).ToArray();
        var after = candidate.FindAll(type).Select(claim => claim.Value).ToArray();
        return before.Length <= 1 && after.Length <= 1 && before.SequenceEqual(after, StringComparer.Ordinal);
    }

    private static AuthenticationTicket Copy(AuthenticationTicket ticket)
        => TicketSerializer.Default.Deserialize(TicketSerializer.Default.Serialize(ticket))
           ?? throw Rejected();

    private static string NewRevision() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private static InvalidOperationException Rejected() => new("test_session_rejected");
}

internal static class TestBffSessionAuthorityRegistration
{
    public static void AddTestBffSessionAuthority(this IServiceCollection services,
        TestBffSessionAuthority? authority = null,
        string scheme = CookieAuthenticationDefaults.AuthenticationScheme)
    {
        authority ??= new TestBffSessionAuthority();
        services.RemoveAll<TestBffSessionAuthority>();
        services.RemoveAll<ITicketStore>();
        services.RemoveAll<IBffSessionAuthority>();
        services.AddSingleton(authority);
        services.AddSingleton<ITicketStore>(authority);
        services.AddSingleton<IBffSessionAuthority>(authority);
        services.AddOptions<CookieAuthenticationOptions>(scheme)
            .PostConfigure<ITicketStore>((options, store) => options.SessionStore = store);
    }

    public static async Task<AuthenticationTicket?> ReadStoredTicketAsync(
        this CookieAuthenticationOptions options, HttpContext context)
    {
        var cookie = options.CookieManager.GetRequestCookie(context, options.Cookie.Name!);
        var reference = cookie is null ? null : options.TicketDataFormat.Unprotect(cookie);
        var handle = reference?.Principal.FindFirst("Microsoft.AspNetCore.Authentication.Cookies-SessionId")?.Value;
        return handle is null || options.SessionStore is null ? null
            : await options.SessionStore.RetrieveAsync(handle);
    }

    /// <summary>Issues a fresh synthetic credential through the native cookie handler.</summary>
    public static async Task<string> IssueFreshCookieAsync(IServiceProvider services,
        AuthenticationTicket ticket, string scheme = CookieAuthenticationDefaults.AuthenticationScheme)
    {
        var principal = new ClaimsPrincipal(ticket.Principal.Identities.Select(identity => identity.Clone()));
        foreach (var identity in principal.Identities)
            foreach (var claim in identity.FindAll(BffSessionIdentity.HandleClaim).ToArray())
                identity.RemoveClaim(claim);
        var properties = new AuthenticationProperties(new Dictionary<string, string?>(ticket.Properties.Items));
        properties.Items.Remove(BffSessionIdentity.HandleProperty);
        properties.Items.Remove(BffSessionIdentity.RevisionProperty);
        using var scope = services.CreateScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        await context.SignInAsync(scheme, principal, properties);
        return context.Response.Headers.SetCookie.ToString().Split(';', 2)[0];
    }
}
