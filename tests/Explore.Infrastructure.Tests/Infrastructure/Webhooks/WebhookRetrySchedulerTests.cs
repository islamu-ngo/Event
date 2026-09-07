using Explore.Infrastructure.Configuration;
using Explore.Infrastructure.Webhooks;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Tests.Infrastructure.Webhooks;

public sealed class WebhookRetrySchedulerTests
{
    [Test]
    public async Task GetDelay_StaysWithinConfiguredExponentialFullJitterCeiling()
    {
        var scheduler = new WebhookRetryScheduler();

        await Assert.That(scheduler.GetDelay(1)).IsEqualTo(TimeSpan.Zero);

        var expectedCeilings = new[]
        {
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(60),
            TimeSpan.FromSeconds(120),
            TimeSpan.FromSeconds(240),
            TimeSpan.FromSeconds(480),
            TimeSpan.FromSeconds(960),
            TimeSpan.FromSeconds(1920)
        };

        for (var attemptNumber = 2; attemptNumber <= 8; attemptNumber++)
        {
            var delay = scheduler.GetDelay(attemptNumber);

            await Assert.That(delay).IsGreaterThanOrEqualTo(TimeSpan.Zero);
            await Assert.That(delay).IsLessThanOrEqualTo(expectedCeilings[attemptNumber - 2]);
        }
    }

    [Test]
    public async Task CanScheduleAttempt_RespectsEndpointAndSchedulerMaximums()
    {
        var scheduler = new WebhookRetryScheduler();

        await Assert.That(scheduler.CanScheduleAttempt(8, endpointMaxAttempts: 8)).IsTrue();
        await Assert.That(scheduler.CanScheduleAttempt(9, endpointMaxAttempts: 20)).IsFalse();
        await Assert.That(scheduler.CanScheduleAttempt(4, endpointMaxAttempts: 3)).IsFalse();
    }

    [Test]
    public async Task GetDelay_BoundsRetryAfterAndIgnoresNonPositiveValues()
    {
        var scheduler = new WebhookRetryScheduler(new StaticOptionsMonitor<WebhookOptions>(new WebhookOptions
        {
            Local = new WebhookLocalOptions
            {
                InitialRetryDelaySeconds = 1,
                MaxRetryDelaySeconds = 120,
                MaxRetryAfterSeconds = 30
            }
        }));

        var bounded = scheduler.GetDelay(2, TimeSpan.FromHours(1));
        var ignored = scheduler.GetDelay(2, TimeSpan.FromSeconds(-1));

        await Assert.That(bounded).IsEqualTo(TimeSpan.FromSeconds(30));
        await Assert.That(ignored).IsGreaterThanOrEqualTo(TimeSpan.Zero);
        await Assert.That(ignored).IsLessThanOrEqualTo(TimeSpan.FromSeconds(1));
    }

    private sealed class StaticOptionsMonitor<T>(T currentValue) : IOptionsMonitor<T>
    {
        public T CurrentValue => currentValue;

        public T Get(string? name) => currentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
