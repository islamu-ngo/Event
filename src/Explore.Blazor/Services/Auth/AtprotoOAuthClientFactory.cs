using System.Text.Json;
using CarpaNet.Identity;
using CarpaNet.OAuth;
using CarpaNet.OAuth.Storage;
using Explore.Atproto.Transport;
using Explore.Blazor.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Explore.Blazor.Services.Auth;

public sealed record AtprotoOAuthReadiness(bool IsReady, string? FailureCode)
{
    public static AtprotoOAuthReadiness Ready { get; } = new(true, null);
}

public sealed class AtprotoOAuthClientFactory(
    AtprotoClientKeyProvider keyProvider,
    IOptions<AtprotoAuthenticationOptions> configuredOptions,
    IWebHostEnvironment environment,
    IServiceProviderIsService serviceAvailability,
    IAtprotoOAuthTransportFactory transportFactory) : IDisposable
{
    private const string RequiredScope = "atproto transition:generic";
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);
    private const int MaximumResponseBytes = 1024 * 1024;
    private readonly AtprotoIdentityCache _identityCache = new();

    public AtprotoOAuthReadiness GetReadiness()
    {
        var options = configuredOptions.Value;
        if (!TryGetClientIdentity(options, environment, out _))
        {
            return new(false, "invalid_public_url_or_callback");
        }

        if (!keyProvider.IsReady)
        {
            return new(false, keyProvider.FailureCode ?? "invalid_key_ring");
        }

        if (!serviceAvailability.IsService(typeof(IOAuthStateStore)))
        {
            return new(false, "state_store_unavailable");
        }

        if (!serviceAvailability.IsService(typeof(IOAuthSessionStore)))
        {
            return new(false, "session_store_unavailable");
        }

        return AtprotoOAuthReadiness.Ready;
    }

    public AtprotoOAuthSessionLease CreateForNewFlow(
        IOAuthStateStore stateStore,
        IOAuthSessionStore sessionStore) =>
        CreateForPinnedKey(keyProvider.ActiveKeyId ?? string.Empty, stateStore, sessionStore);

    public AtprotoOAuthSessionLease CreateForPinnedKey(
        string pinnedKeyId,
        IOAuthStateStore stateStore,
        IOAuthSessionStore sessionStore)
    {
        ArgumentNullException.ThrowIfNull(stateStore);
        ArgumentNullException.ThrowIfNull(sessionStore);
        var readiness = GetReadiness();
        if (!readiness.IsReady)
        {
            throw new InvalidOperationException(
                $"ATProto OAuth is not ready ({readiness.FailureCode ?? "unknown"}).");
        }

        if (!keyProvider.HasKey(pinnedKeyId))
        {
            throw new InvalidOperationException("ATProto OAuth session signing key is unavailable.");
        }

        var options = configuredOptions.Value;
        _ = TryGetClientIdentity(options, environment, out var identity);
        var policy = CreatePolicy(environment, options);
        var registry = new AtprotoAuthorizationServerRegistry();
        var primary = transportFactory.CreatePrimaryHandler(policy, ConnectTimeout);
        var bounded = new AtprotoBoundedResponseHandler(MaximumResponseBytes, primary);
        var metadata = new AtprotoAuthorizationServerMetadataHandler(registry, policy, bounded);
        var assertions = new AtprotoPrivateKeyJwtHandler(
            registry,
            keyProvider,
            identity.ClientId,
            identity.CallbackUri,
            RequiredScope,
            pinnedKeyId,
            metadata);
        var httpClient = new HttpClient(assertions, disposeHandler: true) { Timeout = RequestTimeout };
        var identityResolver = IdentityResolver.CreateWithCache(
            _identityCache,
            httpClient,
            dnsResolver: transportFactory.CreateDnsResolver());
        var config = new OAuthClientConfig
        {
            ClientId = identity.ClientId,
            RedirectUri = identity.CallbackUri,
            Scope = RequiredScope,
            HttpClient = httpClient,
            StateStore = stateStore,
            SessionStore = sessionStore,
            JsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web),
            StateExpiration = TimeSpan.FromSeconds(Math.Clamp(options.StateLifetimeSeconds, 30, 600)),
            IdentityResolver = identityResolver
        };

        return new(new OAuthSession(config), httpClient, identityResolver, pinnedKeyId);
    }

    private static bool TryGetClientIdentity(
        AtprotoAuthenticationOptions options,
        IWebHostEnvironment environment,
        out AtprotoClientIdentity identity)
    {
        return AtprotoClientIdentityFactory.TryCreate(
            options.PublicUrl,
            options.CallbackPath,
            CreatePolicy(environment, options),
            out identity);
    }

    private static AtprotoOutboundPolicy CreatePolicy(
        IWebHostEnvironment environment,
        AtprotoAuthenticationOptions options) => new(
        string.Equals(environment.EnvironmentName, Environments.Development, StringComparison.Ordinal)
        && options.AllowDevelopmentLoopback);

    public void Dispose() => _identityCache.Dispose();
}

public sealed class AtprotoOAuthSessionLease(
    OAuthSession session,
    HttpClient httpClient,
    IdentityResolver identityResolver,
    string pinnedKeyId) : IDisposable
{
    public OAuthSession Session { get; } = session;
    public string PinnedKeyId { get; } = pinnedKeyId;

    public async Task<AtprotoResolvedIdentity> ResolveIdentityAsync(
        string handle,
        CancellationToken cancellationToken)
    {
        var document = await identityResolver.ResolveAsync(handle, cancellationToken).ConfigureAwait(false);
        var pdsServices = document.Service?
            .Where(service => string.Equals(service.Id, "#atproto_pds", StringComparison.Ordinal))
            .ToArray() ?? [];
        if (pdsServices.Length != 1
            || !string.Equals(
                pdsServices[0].Type,
                "AtprotoPersonalDataServer",
                StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(document.Id)
            || string.IsNullOrWhiteSpace(document.PdsEndpoint)
            || !Uri.TryCreate(document.PdsEndpoint, UriKind.Absolute, out var pdsUri))
        {
            throw new InvalidOperationException("ATProto identity has no canonical PDS binding.");
        }

        return new(document.Id, pdsUri);
    }

    public void Dispose()
    {
        Session.Dispose();
        httpClient.Dispose();
    }
}

public sealed record AtprotoResolvedIdentity(string Did, Uri PdsUri);
