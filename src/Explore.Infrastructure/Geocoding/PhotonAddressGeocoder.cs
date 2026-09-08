using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Headers;
using Explore.Application.Contracts.Infrastructure.Geocoding;
using Explore.Application.DTOs.Geocoding;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Geocoding;

public sealed class PhotonGeocodingAdapter : IAddressSuggestionProviderGateway
{
    private static readonly ActivitySource ActivitySource = new("Explore.Geocoding.Photon");

    private readonly HttpClient _httpClient;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PhotonGeocodingAdapter> _logger;
    private readonly PhotonGeocodingOptions _options;
    private readonly Counter<long> _operations;

    public PhotonGeocodingAdapter(
        HttpClient httpClient,
        TimeProvider timeProvider,
        ILogger<PhotonGeocodingAdapter> logger,
        IMeterFactory meterFactory,
        IOptions<PhotonGeocodingOptions> options)
    {
        _httpClient = httpClient;
        _timeProvider = timeProvider;
        _logger = logger;
        _options = options.Value;
        Meter meter = meterFactory.Create(new MeterOptions("Explore.Geocoding.Photon"));
        _operations = meter.CreateCounter<long>("geocoding.operations");
    }

    public async Task<AddressGeocoderResult> SearchAsync(
        AddressGeocoderRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        long started = _timeProvider.GetTimestamp();
        using Activity? activity = ActivitySource.StartActivity("Photon forward geocoding");

        if (string.Equals(
            _options.Provider,
            PhotonGeocodingOptions.DisabledProvider,
            StringComparison.OrdinalIgnoreCase))
        {
            return Finish("disabled", 0, started, activity, AddressGeocoderResult.None);
        }

        if (new PhotonOptionsValidator().Validate(null, _options).Failed
            || string.IsNullOrWhiteSpace(request.SearchText)
            || request.SearchText.Trim().Length > 200)
        {
            return Finish(
                "invalid_request",
                0,
                started,
                activity,
                Unavailable());
        }

        using var budgetCancellation = new CancellationTokenSource(
            TotalBudget,
            _timeProvider);
        using var requestCancellation = new CancellationTokenSource();
        using CancellationTokenRegistration callerRegistration = cancellationToken.Register(
            static state => ((CancellationTokenSource)state!).Cancel(),
            requestCancellation);
        using CancellationTokenRegistration budgetRegistration = budgetCancellation.Token.Register(
            static state => ((CancellationTokenSource)state!).Cancel(),
            requestCancellation);
        int retryCount = 0;

        try
        {
            while (true)
            {
                try
                {
                    using HttpRequestMessage outbound = CreateRequest(request);
                    using HttpResponseMessage response = await _httpClient.SendAsync(
                        outbound,
                        HttpCompletionOption.ResponseHeadersRead,
                        requestCancellation.Token);

                    if (response.IsSuccessStatusCode)
                    {
                        byte[]? payload = await ReadBoundedAsync(response.Content, requestCancellation.Token);
                        IReadOnlyList<ProtectedAddressSelection>? suggestions = payload is null
                            ? null
                            : PhotonGeoJsonParser.Parse(
                                payload,
                                EffectiveLimit(request.Limit),
                                _options.DatasetVersion);
                        string outcome = suggestions is null ? "invalid_response" : "success";
                        return Finish(
                            outcome,
                            retryCount,
                            started,
                            activity,
                            suggestions is null
                                ? Unavailable()
                                : new AddressGeocoderResult(
                                    suggestions,
                                    AddressProviderOutcome.Ready));
                    }

                    if (!IsTransient(response.StatusCode)
                        || !TryGetRetryDelay(response.Headers.RetryAfter, retryCount, started, out TimeSpan delay))
                    {
                        bool limited = response.StatusCode == HttpStatusCode.TooManyRequests;
                        return Finish(
                            limited ? "limited" : "unavailable",
                            retryCount,
                            started,
                            activity,
                            limited
                                ? new AddressGeocoderResult([], AddressProviderOutcome.Limited)
                                : Unavailable());
                    }

                    await Task.Delay(delay, _timeProvider, requestCancellation.Token);
                    retryCount++;
                }
                catch (Exception exception) when (exception is HttpRequestException or IOException)
                {
                    if (!TryGetRetryDelay(null, retryCount, started, out TimeSpan delay))
                    {
                        return Finish(
                            "unavailable",
                            retryCount,
                            started,
                            activity,
                            Unavailable());
                    }

                    await Task.Delay(delay, _timeProvider, requestCancellation.Token);
                    retryCount++;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _ = Finish("cancelled", retryCount, started, activity, Unavailable());
            throw;
        }
        catch (OperationCanceledException) when (budgetCancellation.IsCancellationRequested)
        {
            return Finish(
                "timeout",
                retryCount,
                started,
                activity,
                new AddressGeocoderResult([], AddressProviderOutcome.Timeout));
        }
    }

    private HttpRequestMessage CreateRequest(AddressGeocoderRequest request)
    {
        int limit = EffectiveLimit(request.Limit);
        List<string> parameters =
        [
            $"q={Uri.EscapeDataString(request.SearchText.Trim())}",
            $"limit={limit}",
            $"lang={Uri.EscapeDataString(_options.Language.Trim())}"
        ];
        parameters.AddRange(_options.CountryCodes.Select(code =>
            $"countrycode={Uri.EscapeDataString(code.Trim().ToUpperInvariant())}"));
        var operation = new Uri(_options.Endpoint!, "api");
        var requestUri = new UriBuilder(operation) { Query = string.Join('&', parameters) }.Uri;
        var message = new HttpRequestMessage(HttpMethod.Get, requestUri);
        message.Headers.UserAgent.Add(new ProductInfoHeaderValue("ISLAMU-Event-Geocoding", "1.0"));
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/geo+json"));
        return message;
    }

    private async Task<byte[]?> ReadBoundedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        int maximumBytes = _options.MaximumResponseBytes;
        if (content.Headers.ContentLength > maximumBytes)
        {
            return null;
        }

        await using Stream stream = await content.ReadAsStreamAsync(cancellationToken);
        using var destination = new MemoryStream(Math.Min(maximumBytes, 65_536));
        byte[] buffer = new byte[Math.Min(8_192, maximumBytes + 1)];
        while (destination.Length <= maximumBytes)
        {
            int remaining = maximumBytes + 1 - checked((int)destination.Length);
            int read = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining)), cancellationToken);
            if (read == 0)
            {
                return destination.ToArray();
            }

            destination.Write(buffer, 0, read);
        }

        return null;
    }

    private bool TryGetRetryDelay(
        RetryConditionHeaderValue? retryAfter,
        int retryCount,
        long started,
        out TimeSpan delay)
    {
        delay = default;
        if (retryCount >= _options.MaximumRetryCount)
        {
            return false;
        }

        TimeSpan remaining = TotalBudget - _timeProvider.GetElapsedTime(started);
        TimeSpan? providerDelay = retryAfter?.Delta;
        if (providerDelay is null && retryAfter?.Date is { } retryAt)
        {
            providerDelay = retryAt - _timeProvider.GetUtcNow();
        }

        delay = providerDelay.HasValue && providerDelay.Value > TimeSpan.Zero
            ? providerDelay.Value
            : TimeSpan.FromMilliseconds(_options.RetryDelaysMilliseconds[retryCount]);
        return delay > TimeSpan.Zero && delay < remaining;
    }

    private AddressGeocoderResult Finish(
        string outcome,
        int retryCount,
        long started,
        Activity? activity,
        AddressGeocoderResult result)
    {
        string latencyBucket = Bucket(_timeProvider.GetElapsedTime(started));
        TagList tags = new()
        {
            { "provider", PhotonProvenance.Provider },
            { "outcome", outcome },
            { "retry_count", retryCount },
            { "latency_bucket", latencyBucket }
        };
        _operations.Add(1, tags);
        activity?.SetTag("provider", PhotonProvenance.Provider);
        activity?.SetTag("outcome", outcome);
        activity?.SetTag("retry_count", retryCount);
        activity?.SetTag("latency_bucket", latencyBucket);
        _logger.LogInformation(
            "Geocoding operation {provider} {outcome} {retry_count} {latency_bucket}",
            PhotonProvenance.Provider,
            outcome,
            retryCount,
            latencyBucket);
        return result;
    }

    private int EffectiveLimit(int requested) => Math.Clamp(requested, 1, _options.MaximumResults);

    private TimeSpan TotalBudget =>
        TimeSpan.FromMilliseconds(_options.TotalTimeoutMilliseconds);

    private static AddressGeocoderResult Unavailable() =>
        new([], AddressProviderOutcome.Unavailable);

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
        || (int)statusCode >= 500;

    private static string Bucket(TimeSpan elapsed) => elapsed.TotalMilliseconds switch
    {
        < 100 => "under_100ms",
        < 500 => "under_500ms",
        < 1_000 => "under_1s",
        < 3_000 => "under_3s",
        _ => "up_to_5s"
    };
}
