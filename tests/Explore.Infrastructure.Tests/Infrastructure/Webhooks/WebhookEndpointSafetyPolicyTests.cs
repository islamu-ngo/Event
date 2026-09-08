using Explore.Infrastructure.Configuration;
using Explore.Infrastructure.Webhooks;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Tests.Infrastructure.Webhooks;

public sealed class WebhookEndpointSafetyPolicyTests
{
    [Test]
    [Arguments("http://localhost/hook")]
    [Arguments("http://127.0.0.1/hook")]
    [Arguments("http://[::1]/hook")]
    [Arguments("http://10.1.2.3/hook")]
    [Arguments("http://172.16.1.2/hook")]
    [Arguments("http://192.168.1.2/hook")]
    [Arguments("http://169.254.169.254/latest/meta-data")]
    public async Task ValidateAsync_WhenEndpointTargetsInternalNetwork_BlocksUrl(string url)
    {
        var policy = CreatePolicy(new WebhookOptions());

        var result = await policy.ValidateAsync(new Uri(url), CancellationToken.None);

        await Assert.That(result.IsAllowed).IsFalse();
        await Assert.That(result.FailureCategory).IsNotNull();
    }

    [Test]
    public async Task ValidateAsync_WhenEndpointTargetsPublicIp_AllowsUrl()
    {
        var policy = CreatePolicy(new WebhookOptions());

        var result = await policy.ValidateAsync(new Uri("https://1.1.1.1/webhooks"), CancellationToken.None);

        await Assert.That(result.IsAllowed).IsTrue();
    }

    [Test]
    public async Task ValidateAsync_WhenPrivateCidrAllowListed_AllowsMatchingPrivateAddress()
    {
        var policy = CreatePolicy(new WebhookOptions
        {
            Local = new WebhookLocalOptions
            {
                AllowedPrivateCidrs = ["10.0.0.0/8"]
            }
        });

        var result = await policy.ValidateAsync(new Uri("https://10.1.2.3/webhooks"), CancellationToken.None);

        await Assert.That(result.IsAllowed).IsTrue();
    }

    [Test]
    public async Task ValidateAsync_WhenCloudMetadataAllowListed_StillBlocksMetadataAddress()
    {
        var policy = CreatePolicy(new WebhookOptions
        {
            Local = new WebhookLocalOptions
            {
                AllowedPrivateCidrs = ["169.254.0.0/16"]
            }
        });

        var result = await policy.ValidateAsync(new Uri("http://169.254.169.254/latest/meta-data"), CancellationToken.None);

        await Assert.That(result.IsAllowed).IsFalse();
        await Assert.That(result.FailureCategory).IsEqualTo("cloud_metadata_blocked");
    }

    private static WebhookEndpointSafetyPolicy CreatePolicy(WebhookOptions options) =>
        new(new StaticOptionsMonitor<WebhookOptions>(options));

    private sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
    {
        public StaticOptionsMonitor(T currentValue)
        {
            CurrentValue = currentValue;
        }

        public T CurrentValue { get; }

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
