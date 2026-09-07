using CarpaNet;
using CarpaNet.Http;
using CarpaNet.OAuth;
using CarpaNet.OAuth.Storage;
using Explore.Atproto.Transport;

namespace Explore.Infrastructure.Services.Federation;

public sealed class AtprotoCoreClientFactory
{
    private readonly AtprotoOAuthClientFactory _oauthFactory;
    private readonly IAtprotoCorePrimaryHandlerFactory _primaryHandlerFactory;

    public AtprotoCoreClientFactory(AtprotoOAuthClientFactory oauthFactory)
        : this(oauthFactory, new ProductionAtprotoCorePrimaryHandlerFactory())
    {
    }

    internal AtprotoCoreClientFactory(
        AtprotoOAuthClientFactory oauthFactory,
        IAtprotoCorePrimaryHandlerFactory primaryHandlerFactory)
    {
        _oauthFactory = oauthFactory;
        _primaryHandlerFactory = primaryHandlerFactory;
    }

    public async Task<AtprotoCoreClientLease> CreateAsync(
        string subjectDid,
        string pinnedKeyId,
        IOAuthSessionStore sessionStore,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectDid);
        ArgumentNullException.ThrowIfNull(sessionStore);
        var (ring, identity, policy) = await _oauthFactory
            .ResolveReadyContextAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!ring.HasKey(pinnedKeyId))
        {
            throw new InvalidOperationException("ATProto OAuth session signing key is unavailable.");
        }

        var oauthHttpClient = InfrastructureAtprotoOAuthTransportFactory.Create(
            policy,
            ring,
            identity,
            pinnedKeyId,
            _primaryHandlerFactory.CreateOAuthPrimary(policy));
        var discovery = new AuthorizationServerDiscovery(oauthHttpClient);
        var tokenProvider = new DPoPTokenProvider(
            oauthHttpClient,
            sessionStore,
            discovery,
            clientId: identity.ClientId,
            redirectUri: identity.CallbackUri,
            scope: InfrastructureAtprotoOAuthTransportFactory.RequiredScope);
        try
        {
            if (!await tokenProvider.RestoreSessionAsync(subjectDid, cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException("ATProto OAuth session is unavailable.");
            }

            var pdsUri = tokenProvider.PdsUrl
                ?? throw new InvalidOperationException("ATProto PDS endpoint is unavailable.");
            if (!string.IsNullOrEmpty(pdsUri.Query)
                || pdsUri.AbsolutePath != "/")
            {
                throw new InvalidOperationException("ATProto PDS endpoint is invalid.");
            }

            policy.ValidateUri(pdsUri);
            var corePrimary = _primaryHandlerFactory.CreatePdsPrimary(policy);
            var coreAuth = new ATProtoDPoPAuthHandler(
                tokenProvider,
                new AtprotoBoundedResponseHandler(4 * 1024 * 1024, corePrimary));
            var coreHttpClient = new HttpClient(coreAuth, disposeHandler: true)
            {
                Timeout = TimeSpan.FromSeconds(20)
            };
            try
            {
                var jsonOptions = new System.Text.Json.JsonSerializerOptions(
                    global::CarpaNet.ATProtoClientFactory.CreateJsonOptions());
                jsonOptions.TypeInfoResolverChain.Insert(0, InfrastructureAtprotoJsonContext.Default);
                var client = new ATProtoClient(new ATProtoClientOptions
                {
                    BaseUrl = pdsUri,
                    HttpClient = coreHttpClient,
                    TokenProvider = tokenProvider,
                    JsonOptions = jsonOptions,
                    CborContext = global::CarpaNet.Cbor.ATProtoCborContext.Default,
                    CreateIdentityResolver = false,
                    AutoRetryOnAuthFailure = false,
                    EnableRateLimitHandler = false
                });
                return new(client, coreHttpClient, tokenProvider, discovery, oauthHttpClient);
            }
            catch
            {
                coreHttpClient.Dispose();
                throw;
            }
        }
        catch
        {
            tokenProvider.Dispose();
            discovery.Dispose();
            oauthHttpClient.Dispose();
            throw;
        }
    }
}

internal interface IAtprotoCorePrimaryHandlerFactory
{
    HttpMessageHandler CreateOAuthPrimary(AtprotoOutboundPolicy policy);
    HttpMessageHandler CreatePdsPrimary(AtprotoOutboundPolicy policy);
}

internal sealed class ProductionAtprotoCorePrimaryHandlerFactory : IAtprotoCorePrimaryHandlerFactory
{
    public HttpMessageHandler CreateOAuthPrimary(AtprotoOutboundPolicy policy) =>
        AtprotoHardenedHttpClient.CreatePrimaryHandler(policy, TimeSpan.FromSeconds(5));

    public HttpMessageHandler CreatePdsPrimary(AtprotoOutboundPolicy policy) =>
        AtprotoHardenedHttpClient.CreatePrimaryHandler(policy, TimeSpan.FromSeconds(5));
}

public sealed class AtprotoCoreClientLease(
    ATProtoClient client,
    HttpClient coreHttpClient,
    DPoPTokenProvider tokenProvider,
    AuthorizationServerDiscovery discovery,
    HttpClient oauthHttpClient) : IDisposable
{
    public ATProtoClient Client { get; } = client;

    public Task RefreshAsync(CancellationToken cancellationToken) =>
        tokenProvider.RefreshAsync(cancellationToken);

    public void Dispose()
    {
        Client.Dispose();
        coreHttpClient.Dispose();
        tokenProvider.Dispose();
        discovery.Dispose();
        oauthHttpClient.Dispose();
    }
}
