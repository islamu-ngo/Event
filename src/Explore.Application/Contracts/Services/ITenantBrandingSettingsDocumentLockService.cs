namespace Explore.Application.Contracts.Services;

using Explore.Domain.Settings.Documents.Payloads;

public interface ITenantBrandingSettingsDocumentLockService
{
    Task<TenantBrandingSettingsDocumentLockState> GetLockStateAsync(CancellationToken cancellationToken = default);

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
