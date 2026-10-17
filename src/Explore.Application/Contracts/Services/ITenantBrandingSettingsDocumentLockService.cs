namespace Explore.Application.Contracts.Services;

using Explore.Domain.Settings.Documents.Payloads;

public interface ITenantBrandingSettingsDocumentLockService
{
    Task<TenantBrandingSettingsDocumentLockState> GetLockStateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Holds all branding governance setting leases around the caller's committing transaction.
    /// The caller must open its transaction and reread document and lock state inside the operation.
    /// </summary>
    Task<T> ExecuteWithGovernanceLocksAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default);

    IReadOnlyList<string> ValidateAllowedChanges(
        BrandingSettings currentPayload,
        BrandingSettings requestedPayload,
        TenantBrandingSettingsDocumentLockState lockState);
}

public sealed record TenantBrandingSettingsDocumentLockState(
    bool CanChangeDisplayName,
    bool CanChangeLogoUrl,
    bool CanChangeFaviconUrl,
    bool CanChangeCustomCssUrl)
{
    public static TenantBrandingSettingsDocumentLockState AllowAll { get; } = new(true, true, true, true);

    public bool IsLockedByInstance =>
        !CanChangeDisplayName ||
        !CanChangeLogoUrl ||
        !CanChangeFaviconUrl ||
        !CanChangeCustomCssUrl;
}
