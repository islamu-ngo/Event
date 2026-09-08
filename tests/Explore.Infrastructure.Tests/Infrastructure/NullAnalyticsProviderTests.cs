using Explore.Infrastructure.Analytics;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure;

public class NullAnalyticsProviderTests
{
    [Test]
    public async Task Methods_DoNotThrow()
    {
        var logger = Substitute.For<ILogger<NullAnalyticsProvider>>();
        var provider = new NullAnalyticsProvider(logger);

        await provider.IdentifyAsync("user-1");
        await provider.TrackAsync("user-1", "TestEvent");
        await provider.PageViewAsync("user-1", "/home");
        await provider.GroupIdentifyAsync("tenant", "tenant-1");

        await Assert.That(true).IsTrue();
    }

    [Test]
    public async Task IsFeatureEnabledAsync_ReturnsFalse()
    {
        var logger = Substitute.For<ILogger<NullAnalyticsProvider>>();
        var provider = new NullAnalyticsProvider(logger);

        var defaultFalse = await provider.IsFeatureEnabledAsync("flag-a", "user-1");

        await Assert.That(defaultFalse).IsFalse();
    }
}
