using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Geocoding;

public sealed class GeocodingReadinessProbe(
    IHttpClientFactory httpClientFactory,
    IOptions<PhotonGeocodingOptions> options,
    TimeProvider timeProvider)
    : IHealthCheck
{
    public const string HttpClientName = "photon-readiness";

    private readonly PhotonGeocodingOptions _options = options.Value;

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default) =>
        ProbeAsync(cancellationToken);

    public async Task<HealthCheckResult> ProbeAsync(
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(
            _options.Provider,
            PhotonGeocodingOptions.DisabledProvider,
            StringComparison.OrdinalIgnoreCase))
        {
            return Result(HealthStatus.Healthy, "disabled");
        }

        if (new PhotonOptionsValidator().Validate(null, _options).Failed
            || _options.Endpoint is null)
        {
            return Result(HealthStatus.Degraded, "invalid_configuration");
        }

        using var timeout = new CancellationTokenSource(
            TimeSpan.FromMilliseconds(_options.ReadinessTimeoutMilliseconds),
            timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri(_options.Endpoint, "/status"));

        try
        {
            HttpClient client = httpClientFactory.CreateClient(HttpClientName);
            using HttpResponseMessage response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                linked.Token);

            if (response.IsSuccessStatusCode)
            {
                return Result(HealthStatus.Healthy, "configured");
            }

            return Result(
                HealthStatus.Degraded,
                response.StatusCode == System.Net.HttpStatusCode.TooManyRequests
                    ? "limited"
                    : "unreachable");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return Result(HealthStatus.Degraded, "timeout");
        }
        catch (HttpRequestException)
        {
            return Result(HealthStatus.Degraded, "unreachable");
        }
    }

    private static HealthCheckResult Result(HealthStatus status, string state) => new(
        status,
        description: $"Photon geocoding is {state}.",
        data: new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["provider"] = "photon",
            ["state"] = state
        });
}
