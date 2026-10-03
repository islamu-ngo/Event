using System.Collections.Frozen;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;

namespace ISLAMU.DependencySubmission;

public enum SubmissionClassification { Accepted, ServerTransient, RateLimited, NetworkTransient, Timeout, Permanent, InvalidRetryHint, Cancelled }
public enum SubmissionOutcome { Success, RetryScheduled, Terminal, AttemptsExhausted, DeadlineExceeded, Cancelled }
public sealed record SubmissionResult(SubmissionOutcome Outcome, int Attempts, HttpStatusCode? Status);
public sealed record SubmissionLog(int Attempt, HttpStatusCode? Status, SubmissionClassification Classification,
    TimeSpan Delay, string? RequestId, SubmissionOutcome Result);

public static class SubmissionLogging
{
    public static FrozenDictionary<SubmissionClassification, string> Classifications { get; } =
        new Dictionary<SubmissionClassification, string>
        {
            [SubmissionClassification.Accepted] = "accepted",
            [SubmissionClassification.ServerTransient] = "server_transient",
            [SubmissionClassification.RateLimited] = "rate_limited",
            [SubmissionClassification.NetworkTransient] = "network_transient",
            [SubmissionClassification.Timeout] = "timeout",
            [SubmissionClassification.Permanent] = "permanent",
            [SubmissionClassification.InvalidRetryHint] = "invalid_retry_hint",
            [SubmissionClassification.Cancelled] = "cancelled"
        }.ToFrozenDictionary();
    public static FrozenDictionary<SubmissionOutcome, string> Outcomes { get; } =
        new Dictionary<SubmissionOutcome, string>
        {
            [SubmissionOutcome.Success] = "success",
            [SubmissionOutcome.RetryScheduled] = "retry_scheduled",
            [SubmissionOutcome.Terminal] = "terminal",
            [SubmissionOutcome.AttemptsExhausted] = "attempts_exhausted",
            [SubmissionOutcome.DeadlineExceeded] = "deadline_exceeded",
            [SubmissionOutcome.Cancelled] = "cancelled"
        }.ToFrozenDictionary();

    public static void Write(TextWriter writer, SubmissionLog entry) => writer.WriteLine(FormattableString.Invariant(
        $"status={(entry.Status.HasValue ? ((int)entry.Status.Value).ToString(CultureInfo.InvariantCulture) : "none")} attempt={entry.Attempt} classification={Classifications[entry.Classification]} delay_ms={entry.Delay.TotalMilliseconds:0} request_id={SafeRequestId(entry.RequestId) ?? "none"} result={Outcomes[entry.Result]}"));

    public static string? SafeRequestId(string? value) =>
        value is { Length: > 0 and <= 128 } && value.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is ':' or '-')
            ? value : null;
}

public sealed class SubmissionClient(HttpClient http, TimeProvider time, Action<SubmissionLog> log)
{
    public const int MaximumAttempts = 3;
    public static TimeSpan OverallTimeout { get; } = TimeSpan.FromMinutes(4);
    public static TimeSpan AttemptTimeout { get; } = TimeSpan.FromSeconds(45);
    public static DateOnly ApiVersion { get; } = new(2026, 3, 10);

    public async Task<SubmissionResult> SubmitAsync(Uri endpoint, DependencySnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        if (endpoint.Scheme != Uri.UriSchemeHttps || endpoint.Host != "api.github.com" ||
            endpoint.Port != 443 || endpoint.UserInfo.Length != 0 ||
            !endpoint.AbsolutePath.StartsWith("/repos/", StringComparison.Ordinal) ||
            !endpoint.AbsolutePath.EndsWith("/dependency-graph/snapshots", StringComparison.Ordinal) ||
            endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0)
            throw new ArgumentException("Only the GitHub snapshot endpoint is supported.", nameof(endpoint));

        var body = SnapshotJson.Serialize(snapshot);
        var start = time.GetTimestamp();
        using var budget = new CancellationTokenSource(OverallTimeout, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, budget.Token);
        HttpStatusCode? status = null;
        var attempt = 0;
        try
        {
            while (attempt < MaximumAttempts)
            {
                if (Remaining() <= TimeSpan.Zero || linked.IsCancellationRequested)
                    return Finish(cancellationToken.IsCancellationRequested ? SubmissionOutcome.Cancelled : SubmissionOutcome.DeadlineExceeded,
                        SubmissionClassification.Cancelled, null);
                attempt++;
                status = null;
                string? requestId = null;
                RetryDecision decision;
                using var attemptClock = new CancellationTokenSource(
                    Remaining() < AttemptTimeout ? Remaining() : AttemptTimeout, time);
                using var attemptCancellation = CancellationTokenSource.CreateLinkedTokenSource(linked.Token, attemptClock.Token);
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
                    request.Content = new ByteArrayContent(body);
                    request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
                    request.Headers.Add("X-GitHub-Api-Version", ApiVersion.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                    request.Headers.UserAgent.ParseAdd("ISLAMU-DependencySubmission/1.0");
                    using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, attemptCancellation.Token)
                        .ConfigureAwait(false);
                    status = response.StatusCode;
                    requestId = SubmissionLogging.SafeRequestId(Header(response, "X-GitHub-Request-Id"));
                    if (status == HttpStatusCode.Created)
                        return Finish(SubmissionOutcome.Success, SubmissionClassification.Accepted, requestId);
                    decision = Classify(response, attempt);
                }
                catch (HttpRequestException exception)
                {
                    status = exception.StatusCode;
                    decision = exception.StatusCode is null && exception.HttpRequestError is
                        HttpRequestError.ConnectionError or HttpRequestError.NameResolutionError or
                        HttpRequestError.ResponseEnded or HttpRequestError.HttpProtocolError
                        ? new RetryDecision(SubmissionClassification.NetworkTransient, Backoff(attempt))
                        : new RetryDecision(SubmissionClassification.Permanent, null);
                }
                catch (OperationCanceledException) when (!linked.IsCancellationRequested)
                {
                    decision = new RetryDecision(SubmissionClassification.Timeout, Backoff(attempt));
                }

                if (decision.Delay is null)
                    return Finish(SubmissionOutcome.Terminal, decision.Classification, requestId);
                if (attempt == MaximumAttempts)
                    return Finish(SubmissionOutcome.AttemptsExhausted, decision.Classification, requestId);
                if (decision.Delay >= Remaining())
                    return Finish(SubmissionOutcome.DeadlineExceeded, decision.Classification, requestId);
                log(new SubmissionLog(attempt, status, decision.Classification, decision.Delay.Value,
                    requestId, SubmissionOutcome.RetryScheduled));
                await Task.Delay(decision.Delay.Value, time, linked.Token).ConfigureAwait(false);
            }
            throw new InvalidOperationException("Attempt bound violated.");
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            return Finish(cancellationToken.IsCancellationRequested ? SubmissionOutcome.Cancelled : SubmissionOutcome.DeadlineExceeded,
                SubmissionClassification.Cancelled, null);
        }

        TimeSpan Remaining() => OverallTimeout - time.GetElapsedTime(start);
        SubmissionResult Finish(SubmissionOutcome outcome, SubmissionClassification classification, string? requestId)
        {
            log(new SubmissionLog(attempt, status, classification, TimeSpan.Zero, requestId, outcome));
            return new SubmissionResult(outcome, attempt, status);
        }
    }

    private RetryDecision Classify(HttpResponseMessage response, int attempt)
    {
        var retryAfter = response.Headers.RetryAfter;
        var exhausted = Header(response, "X-RateLimit-Remaining") == "0";
        var status = response.StatusCode;
        var rateLimited = status == HttpStatusCode.TooManyRequests ||
            status == HttpStatusCode.Forbidden && (retryAfter is not null || exhausted);
        var serverTransient = status is HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway or
            HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;
        if (!rateLimited && !serverTransient)
            return new RetryDecision(SubmissionClassification.Permanent, null);

        TimeSpan? serverDelay = null;
        if (response.Headers.Contains("Retry-After"))
        {
            if (retryAfter is null || retryAfter.Delta < TimeSpan.Zero)
                return new RetryDecision(SubmissionClassification.InvalidRetryHint, null);
            serverDelay = retryAfter.Delta ?? retryAfter.Date - time.GetUtcNow();
        }
        if (exhausted)
        {
            if (!long.TryParse(Header(response, "X-RateLimit-Reset"), NumberStyles.None,
                CultureInfo.InvariantCulture, out var reset) || reset < 0 || reset > 253402300799)
                return new RetryDecision(SubmissionClassification.InvalidRetryHint, null);
            // A second beyond reset avoids retrying within the reset instant.
            var resetDelay = DateTimeOffset.FromUnixTimeSeconds(reset) - time.GetUtcNow() + TimeSpan.FromSeconds(1);
            serverDelay = serverDelay.HasValue && serverDelay.Value > resetDelay ? serverDelay : resetDelay;
        }
        var delay = rateLimited && serverDelay is null ? TimeSpan.FromSeconds(60 * (1 << (attempt - 1))) : Backoff(attempt);
        if (serverDelay > delay)
            delay = serverDelay.Value;
        return new RetryDecision(rateLimited ? SubmissionClassification.RateLimited : SubmissionClassification.ServerTransient, delay);
    }

    private static TimeSpan Backoff(int attempt) => TimeSpan.FromSeconds(1 << (attempt - 1));
    private static string? Header(HttpResponseMessage response, string name)
    {
        if (!response.Headers.TryGetValues(name, out var values))
            return null;
        var tokens = values.Take(2).ToArray();
        return tokens.Length == 1 ? tokens[0] : null;
    }
    private sealed record RetryDecision(SubmissionClassification Classification, TimeSpan? Delay);
}
