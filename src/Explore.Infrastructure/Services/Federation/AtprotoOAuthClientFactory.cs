using CarpaNet.OAuth;
using CarpaNet.OAuth.Storage;
using Explore.Application.Contracts.Secrets;
using Explore.Atproto.Transport;
using Explore.Domain.Secrets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Services.Federation;

public sealed class AtprotoOAuthClientFactory(
    ISecretResolver secretResolver,
    IOptions<AtprotoInfrastructureOptions> configuredOptions,
    IHostEnvironment environment)
{
    private readonly Func<AtprotoOutboundPolicy, HttpMessageHandler> _primaryHandlerFactory =
        policy => AtprotoHardenedHttpClient.CreatePrimaryHandler(policy, TimeSpan.FromSeconds(5));

    internal AtprotoOAuthClientFactory(
        ISecretResolver secretResolver,
        IOptions<AtprotoInfrastructureOptions> configuredOptions,
        IHostEnvironment environment,
        Func<AtprotoOutboundPolicy, HttpMessageHandler> primaryHandlerFactory)
        : this(secretResolver, configuredOptions, environment)
    {
        _primaryHandlerFactory = primaryHandlerFactory;
    }

    public async Task<AtprotoInfrastructureReadiness> GetReadinessAsync(
        CancellationToken cancellationToken)
    {
        if (!TryGetIdentity(out _))
        {
            return new(false, "invalid_public_url_or_callback");
        }

        try
        {
            var ring = await ResolveKeyRingAsync(cancellationToken).ConfigureAwait(false);
            return ring.IsReady
                ? new(true, null)
                : new(false, "key_ring_unavailable");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new(false, "secret_resolver_unavailable");
        }
    }

    public async Task<AtprotoInfrastructureOAuthLease> CreateAsync(
        string pinnedKeyId,
        IOAuthStateStore stateStore,
        IOAuthSessionStore sessionStore,
        CancellationToken cancellationToken) =>
        await CreateAsync(
            pinnedKeyId,
            stateStore,
            sessionStore,
            revocationObserver: null,
            cancellationToken).ConfigureAwait(false);

    internal async Task<AtprotoInfrastructureOAuthLease> CreateAsync(
        string pinnedKeyId,
        IOAuthStateStore stateStore,
        IOAuthSessionStore sessionStore,
        AtprotoRevocationObserver? revocationObserver,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stateStore);
        ArgumentNullException.ThrowIfNull(sessionStore);
        var (ring, identity, policy) = await ResolveReadyContextAsync(cancellationToken).ConfigureAwait(false);
        if (!ring.HasKey(pinnedKeyId))
        {
            throw new InvalidOperationException("ATProto Infrastructure OAuth is not ready.");
        }

        HttpMessageHandler primary = _primaryHandlerFactory(policy);
        if (revocationObserver is not null)
        {
            primary = new AtprotoRevocationObserverHandler(revocationObserver, primary);
        }

        var httpClient = InfrastructureAtprotoOAuthTransportFactory.Create(
            policy,
            ring,
            identity,
            pinnedKeyId,
            primary);
        try
        {
            var config = new OAuthClientConfig
            {
                ClientId = identity.ClientId,
                RedirectUri = identity.CallbackUri,
                Scope = InfrastructureAtprotoOAuthTransportFactory.RequiredScope,
                HttpClient = httpClient,
                StateStore = stateStore,
                SessionStore = sessionStore,
                JsonOptions = global::CarpaNet.ATProtoClientFactory.CreateJsonOptions()
            };
            return new(new OAuthSession(config), httpClient, pinnedKeyId);
        }
        catch
        {
            httpClient.Dispose();
            throw;
        }
    }

    internal async Task<(InfrastructureAtprotoKeyRing Ring, AtprotoClientIdentity Identity, AtprotoOutboundPolicy Policy)>
        ResolveReadyContextAsync(CancellationToken cancellationToken)
    {
        try
        {
            var ring = await ResolveKeyRingAsync(cancellationToken).ConfigureAwait(false);
            if (!ring.IsReady || !TryGetIdentity(out var identity))
            {
                throw new InvalidOperationException("ATProto Infrastructure OAuth is not ready.");
            }

            return (ring, identity, CreatePolicy());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch
        {
            throw new InvalidOperationException("ATProto Infrastructure OAuth is not ready.");
        }
    }

    private async Task<InfrastructureAtprotoKeyRing> ResolveKeyRingAsync(
        CancellationToken cancellationToken)
    {
        var resolved = await secretResolver.ResolveAsync(
            SecretDefinitionRegistry.Keys.Atproto.OAuthClientPrivateJwks,
            tenantId: null,
            cancellationToken).ConfigureAwait(false);
        return InfrastructureAtprotoKeyRing.Parse(resolved.Value);
    }

    private bool TryGetIdentity(out AtprotoClientIdentity identity) =>
        AtprotoClientIdentityFactory.TryCreate(
            configuredOptions.Value.PublicUrl,
            configuredOptions.Value.CallbackPath,
            CreatePolicy(),
            out identity);

    private AtprotoOutboundPolicy CreatePolicy() => new(
        string.Equals(environment.EnvironmentName, Environments.Development, StringComparison.Ordinal)
        && configuredOptions.Value.AllowDevelopmentLoopback);
}

public sealed class AtprotoInfrastructureOAuthLease(
    OAuthSession session,
    HttpClient httpClient,
    string pinnedKeyId) : IDisposable
{
    public OAuthSession Session { get; } = session;
    public string PinnedKeyId { get; } = pinnedKeyId;

    public void Dispose()
    {
        Session.Dispose();
        httpClient.Dispose();
    }
}
