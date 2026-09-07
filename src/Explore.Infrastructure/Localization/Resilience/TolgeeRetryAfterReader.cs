using System.Net;
using System.Text.Json;

namespace Explore.Infrastructure.Localization.Resilience;

/// <summary>
/// Reads the <c>retryAfter</c> field from a Tolgee 429 response body.
/// <para>
/// Expected body: <c>{"message":"...","retryAfter":2000,"global":false}</c> where retryAfter is in milliseconds.
/// </para>
/// This is a stateless helper, NOT a DelegatingHandler. It is called from the resilience pipeline's
/// <c>DelayGenerator</c>; it never executes retries itself.
/// </summary>
public static class TolgeeRetryAfterReader
{
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Reads the retry-after delay from the response body, if the response is a 429.
    /// Returns <c>null</c> on non-429 responses or parse failures (pipeline falls back to default backoff).
    /// </summary>
    public static async ValueTask<TimeSpan?> ReadDelayAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.StatusCode != HttpStatusCode.TooManyRequests)
            return null;

        try
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);

            if (doc.RootElement.TryGetProperty("retryAfter", out var retryAfterEl)
                && retryAfterEl.ValueKind == JsonValueKind.Number)
            {
                var ms = retryAfterEl.GetInt64();
                var delay = TimeSpan.FromMilliseconds(ms);
                return delay > MaxDelay ? MaxDelay : delay;
            }
        }
        catch
        {
            // Malformed body — fall through to default backoff.
        }

        return null;
    }
}
