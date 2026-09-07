using System.Net;

namespace Explore.Infrastructure.Localization.Resilience;

/// <summary>
/// Reads the <c>X-RateLimit-Reset</c> header from a Weblate 429 response.
/// <para>
/// The header contains a Unix timestamp (seconds) indicating when the rate limit resets.
/// The computed delay is capped at 60 seconds.
/// </para>
/// This is a stateless helper, NOT a DelegatingHandler.
/// </summary>
public static class WeblateRateLimitReader
{
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan MinDelay = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Reads the retry delay from the <c>X-RateLimit-Reset</c> header.
    /// Returns <c>null</c> on non-429 responses or missing headers (pipeline falls back to default backoff).
    /// </summary>
    public static TimeSpan? ReadDelay(HttpResponseMessage response)
    {
        if (response.StatusCode != HttpStatusCode.TooManyRequests)
            return null;

        if (!response.Headers.TryGetValues("X-RateLimit-Reset", out var values))
            return null;

        var resetValue = values.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(resetValue) || !long.TryParse(resetValue, out var resetUnix))
            return null;

        var resetAt = DateTimeOffset.FromUnixTimeSeconds(resetUnix);
        var wait = resetAt - DateTimeOffset.UtcNow;

        if (wait < MinDelay)
            return MinDelay;

        return wait > MaxDelay ? MaxDelay : wait;
    }
}
