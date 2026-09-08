using CarpaNet.Identity;
using Explore.Atproto.Transport;

namespace Explore.Blazor.Services.Auth;

public interface IAtprotoOAuthTransportFactory
{
    HttpMessageHandler CreatePrimaryHandler(AtprotoOutboundPolicy policy, TimeSpan connectTimeout);

    IDnsResolver CreateDnsResolver();
}

public sealed class AtprotoOAuthTransportFactory : IAtprotoOAuthTransportFactory
{
    public HttpMessageHandler CreatePrimaryHandler(AtprotoOutboundPolicy policy, TimeSpan connectTimeout) =>
        AtprotoHardenedHttpClient.CreatePrimaryHandler(policy, connectTimeout);

    public IDnsResolver CreateDnsResolver() => new DefaultDnsResolver();
}
