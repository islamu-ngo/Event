using System.Text.Json;

using Microsoft.Extensions.Options;

namespace Explore.Blazor.Services.Auth;

public interface IBffAuthDiagnosticsService
{
    Task<IReadOnlyDictionary<string, object?>> BuildDebugSnapshotAsync(CancellationToken cancellationToken);
}

public sealed class BffAuthDiagnosticsOptions
{
    public string? Authority { get; init; }

    public string? MetadataAddress { get; init; }

    public string? ClientId { get; init; }

    public string? ClientSecret { get; init; }
}

public sealed class BffAuthDiagnosticsService(
    IOptions<BffAuthDiagnosticsOptions> options,
    IHttpClientFactory httpClientFactory)
    : IBffAuthDiagnosticsService
{
    public async Task<IReadOnlyDictionary<string, object?>> BuildDebugSnapshotAsync(CancellationToken cancellationToken)
    {
        var diagnosticsOptions = options.Value;
        var authority = diagnosticsOptions.Authority;
        var metadataAddress = diagnosticsOptions.MetadataAddress
            ?? $"{authority}/.well-known/openid-configuration";

        var result = new Dictionary<string, object?>
        {
            ["authority"] = authority,
            ["metadataAddress"] = metadataAddress,
            ["hasClientId"] = !string.IsNullOrEmpty(diagnosticsOptions.ClientId),
            ["hasClientSecret"] = !string.IsNullOrEmpty(diagnosticsOptions.ClientSecret)
        };

        try
        {
            using var httpClient = httpClientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromSeconds(10);
            using var response = await httpClient.GetAsync(metadataAddress, cancellationToken);
            result["discoveryStatus"] = (int)response.StatusCode;
            result["discoverySuccess"] = response.IsSuccessStatusCode;

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                result["discoveryDocument"] = JsonSerializer.Deserialize<object>(content);
            }
            else
            {
                result["discoveryError"] = await response.Content.ReadAsStringAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            result["discoveryError"] = ex.Message;
        }

        return result;
    }
}
