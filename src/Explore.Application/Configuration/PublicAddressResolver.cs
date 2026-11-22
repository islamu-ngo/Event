using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Domain.Constants;
using Microsoft.Extensions.Configuration;

namespace Explore.Application.Configuration;

/// <summary>Resolves an explicit deployment address or one established by authorized setup; never adopts a request host.</summary>
public static class PublicAddressResolver
{
    public static string? ReadOverride(IConfiguration configuration) =>
        new[] { "PUBLIC_BASE_URL", "PublicBaseUrl", "App:PublicBaseUrl", "Application:PublicBaseUrl" }
            .Select(key => configuration[key]?.Trim())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    public static bool IsValid(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query)
        && string.IsNullOrEmpty(uri.Fragment);

    public static Task<Uri?> ResolveAsync(IConfiguration configuration,
        ISystemSettingRepository settings, CancellationToken cancellationToken) =>
        ResolveAsync(configuration, async token =>
        {
            var stored = await settings.GetByKey(GovernanceSettingKeys.Domains.PublicBaseUrl, token);
            return !string.IsNullOrWhiteSpace(stored?.Value)
                ? JsonSerializer.Deserialize<string>(stored.Value) : null;
        }, cancellationToken);

    /// <summary>Reads a decoded stored address lazily, only when no deployment override is present.</summary>
    public static async Task<Uri?> ResolveAsync(IConfiguration configuration,
        Func<CancellationToken, Task<string?>> readStoredValue, CancellationToken cancellationToken)
    {
        var value = ReadOverride(configuration);
        if (value is null)
            value = await readStoredValue(cancellationToken);

        // An invalid explicit override must not silently fall back to a different host.
        return IsValid(value) ? new Uri(value!.TrimEnd('/') + "/", UriKind.Absolute) : null;
    }
}
