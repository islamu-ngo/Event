using Explore.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Webhooks;

public sealed class WebhookRetryScheduler(IOptionsMonitor<WebhookOptions>? options = null)
{
    private static readonly WebhookLocalOptions DefaultOptions = new();

    public int MaxScheduledAttempts => CurrentOptions.MaxAttempts;

    public TimeSpan GetDelay(int attemptNumber, TimeSpan? retryAfter = null)
    {
        if (attemptNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(attemptNumber), "Attempt number must be greater than zero.");
        }

        if (attemptNumber == 1)
        {
            return TimeSpan.Zero;
        }

        var local = CurrentOptions;
        var exponent = Math.Min(attemptNumber - 2, 30);
        var exponentialSeconds = local.InitialRetryDelaySeconds * Math.Pow(2, exponent);
        var backoffCeiling = TimeSpan.FromSeconds(Math.Min(exponentialSeconds, local.MaxRetryDelaySeconds));
        var jitter = TimeSpan.FromTicks(Random.Shared.NextInt64(backoffCeiling.Ticks + 1));
        var boundedRetryAfter = retryAfter is { } requested && requested > TimeSpan.Zero
            ? TimeSpan.FromSeconds(Math.Min(
                requested.TotalSeconds,
                Math.Min(local.MaxRetryAfterSeconds, local.MaxRetryDelaySeconds)))
            : TimeSpan.Zero;

        return jitter >= boundedRetryAfter ? jitter : boundedRetryAfter;
    }

    public DateTime GetScheduledAtUtc(
        int attemptNumber,
        DateTime nowUtc,
        TimeSpan? retryAfter = null) =>
        nowUtc.Add(GetDelay(attemptNumber, retryAfter));

    public bool CanScheduleAttempt(int attemptNumber, int endpointMaxAttempts) =>
        attemptNumber >= 1
        && attemptNumber <= endpointMaxAttempts
        && attemptNumber <= MaxScheduledAttempts;

    private WebhookLocalOptions CurrentOptions => options?.CurrentValue.Local ?? DefaultOptions;
}
