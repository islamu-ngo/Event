using Explore.Atproto.Transport;

namespace Explore.Infrastructure.Services.Federation;

internal static class InfrastructureAtprotoOAuthTransportFactory
{
    public const string RequiredScope = "atproto transition:generic";

    public static HttpClient Create(
        AtprotoOutboundPolicy policy,
        InfrastructureAtprotoKeyRing ring,
        AtprotoClientIdentity identity,
        string pinnedKeyId)
        => Create(
            policy,
            ring,
            identity,
            pinnedKeyId,
            AtprotoHardenedHttpClient.CreatePrimaryHandler(policy, TimeSpan.FromSeconds(5)));

    internal static HttpClient Create(
        AtprotoOutboundPolicy policy,
        InfrastructureAtprotoKeyRing ring,
        AtprotoClientIdentity identity,
        string pinnedKeyId,
        HttpMessageHandler primary)
    {
        var registry = new AtprotoAuthorizationServerRegistry();
        var bounded = new AtprotoBoundedResponseHandler(1024 * 1024, primary);
        var metadata = new AtprotoAuthorizationServerMetadataHandler(registry, policy, bounded);
        var assertions = new InfrastructureAtprotoPrivateKeyJwtHandler(
            registry,
            ring,
            identity.ClientId,
            identity.CallbackUri,
            RequiredScope,
            pinnedKeyId,
            metadata);
        return new HttpClient(assertions, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
    }
}
