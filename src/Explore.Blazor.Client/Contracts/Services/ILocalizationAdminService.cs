using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Contracts.Services;

/// <summary>
/// Operations exposed by the localization admin UI.
/// Implementations are resilient: HTTP failures are logged and returned as <c>null</c> / success=false
/// rather than thrown so the admin UI can surface them as snackbars without try/catch noise.
/// </summary>
public interface ILocalizationAdminService
{
    /// <summary>Reads the current effective localization configuration for the tenant.</summary>
    Task<LocalizationConfigDto?> GetConfigurationAsync(CancellationToken ct = default);

    /// <summary>Tests connectivity to the currently configured TMS provider.</summary>
    Task<LocalizationAdminCommandResult> TestConnectionAsync(CancellationToken ct = default);

    /// <summary>
    /// Pulls translations from the currently configured TMS for <paramref name="languageCode"/>,
    /// persists them as an on-disk bundle, and invalidates the resolver cache.
    /// </summary>
    Task<LocalizationAdminCommandResult> ExportFromTmsAsync(string languageCode, CancellationToken ct = default);

    Task<IReadOnlyDictionary<string, string>?> ExportBundleAsync(string languageCode, CancellationToken ct = default);

    Task<LocalizationAdminCommandResult> ImportBundleAsync(
        string languageCode,
        IReadOnlyDictionary<string, string> translations,
        CancellationToken ct = default);

    /// <summary>
    /// Writes the provided governance snapshot to SystemSettings and invalidates the config cache.
    /// </summary>
    Task<LocalizationAdminCommandResult> UpdateGovernanceAsync(UpdateLocalizationGovernanceDto payload, CancellationToken ct = default);

    /// <summary>
    /// Reports whether the offline bundle target directory is writable.
    /// The admin UI surfaces this as a health banner and gates the "Export" buttons.
    /// </summary>
    Task<WritablePathHealth?> GetBundlePathHealthAsync(CancellationToken ct = default);
}

/// <summary>
/// Result wrapper — success bool + human-readable message from the server.
/// </summary>
public sealed record LocalizationAdminCommandResult(bool Success, string Message);
