using System.Net;
using Explore.Infrastructure.WebPush;

namespace Explore.Infrastructure.Tests.Infrastructure.WebPush;

public sealed class WebPushSafeConnectorTests
{
    [Test]
    public async Task ConnectAsync_WhenHostRebindsToPrivateAddress_RejectsBeforeConnecting()
    {
        var policy = new WebPushEndpointSafetyPolicy((_, _) =>
            Task.FromResult(new[] { IPAddress.Loopback }));

        var act = () => WebPushSafeConnector.ConnectAsync(
            policy,
            new DnsEndPoint("push.example.com", 443),
            CancellationToken.None).AsTask();

        await Assert.That(act).Throws<HttpRequestException>();
    }

    [Test]
    public async Task ResolveHostAsync_WhenDnsMixesPublicAndPrivateAddresses_RejectsEntireSet()
    {
        var policy = new WebPushEndpointSafetyPolicy((_, _) =>
            Task.FromResult(new[] { IPAddress.Parse("203.0.113.10"), IPAddress.Loopback }));

        var result = await policy.ResolveHostAsync("push.example.com", CancellationToken.None);

        await Assert.That(result.IsAllowed).IsFalse();
        await Assert.That(result.IsRetryable).IsFalse();
        await Assert.That(result.Addresses).IsEmpty();
    }
}
