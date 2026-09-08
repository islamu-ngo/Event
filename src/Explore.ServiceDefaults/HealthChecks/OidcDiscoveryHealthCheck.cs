using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Explore.ServiceDefaults.HealthChecks;

public sealed class OidcDiscoveryHealthCheck(
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory) : IHealthCheck
{
    public const string HttpClientName = "OidcDiscoveryHealth";

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var metadataAddress = ResolveMetadataAddress(configuration);
        if (metadataAddress is null)
        {
            return HealthCheckResult.Healthy("OIDC discovery is not configured; skipping optional dependency.");
        }

        var data = new Dictionary<string, object>
        {
            ["metadataAddress"] = metadataAddress
        };

        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Get, metadataAddress);
            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);

            data["statusCode"] = (int)response.StatusCode;

            if (!response.IsSuccessStatusCode)
            {
                return HealthCheckResult.Unhealthy(
                    "OIDC discovery endpoint returned a non-success status code.",
                    data: data);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!HasRequiredDiscoveryFields(document.RootElement))
            {
                return HealthCheckResult.Unhealthy(
                    "OIDC discovery document is missing required issuer or jwks_uri fields.",
                    data: data);
            }

            return HealthCheckResult.Healthy("OIDC discovery endpoint is reachable.", data);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or NotSupportedException)
        {
            return HealthCheckResult.Unhealthy("OIDC discovery endpoint is not reachable or returned invalid metadata.", ex, data);
        }
    }

    private static string? ResolveMetadataAddress(IConfiguration configuration)
    {
        var metadataAddress = configuration["Keycloak:MetadataAddress"];
        if (!string.IsNullOrWhiteSpace(metadataAddress))
        {
            return metadataAddress;
        }

        var authority = configuration["Keycloak:Authority"];
        if (string.IsNullOrWhiteSpace(authority))
        {
            return null;
        }

        return $"{authority.TrimEnd('/')}/.well-known/openid-configuration";
    }

    private static bool HasRequiredDiscoveryFields(JsonElement root)
    {
        return HasStringProperty(root, "issuer") && HasStringProperty(root, "jwks_uri");
    }

    private static bool HasStringProperty(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out var value)
               && value.ValueKind == JsonValueKind.String
               && !string.IsNullOrWhiteSpace(value.GetString());
    }
}
