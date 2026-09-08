namespace Explore.Application.Services;

using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Domain.Constants;
using Explore.Domain.Settings.Documents.Payloads;

public sealed class TenantBrandingSettingsDocumentLockService(ISystemSettingRepository systemSettingRepository)
    : ITenantBrandingSettingsDocumentLockService
{
    public async Task<TenantBrandingSettingsDocumentLockState> GetLockStateAsync(CancellationToken cancellationToken = default)
    {
        var deploymentMode = await systemSettingRepository.GetByKey(GovernanceSettingKeys.Deployment.Mode, cancellationToken);
        var isMultiTenant = DeserializeString(deploymentMode?.Value, "SingleTenant")
            .Equals("MultiTenant", StringComparison.OrdinalIgnoreCase);

        if (!isMultiTenant)
        {
            return TenantBrandingSettingsDocumentLockState.AllowAll;
        }

        var whiteLabeling = await systemSettingRepository.GetByKey(GovernanceSettingKeys.Tenants.WhiteLabelingEnabled, cancellationToken);
        var isWhiteLabelingEnabled = DeserializeBoolean(whiteLabeling?.Value, false);
        var displayName = await systemSettingRepository.GetByKey(GovernanceSettingKeys.Branding.DisplayName, cancellationToken);
        var logoUrl = await systemSettingRepository.GetByKey(GovernanceSettingKeys.Branding.LogoUrl, cancellationToken);
        var faviconUrl = await systemSettingRepository.GetByKey(GovernanceSettingKeys.Branding.FaviconUrl, cancellationToken);
        var customCssUrl = await systemSettingRepository.GetByKey(GovernanceSettingKeys.Branding.CustomCssUrl, cancellationToken);

        return new TenantBrandingSettingsDocumentLockState(
            CanChangeDisplayName: displayName?.IsLocked != true,
            CanChangeLogoUrl: isWhiteLabelingEnabled && logoUrl?.IsLocked != true,
            CanChangeFaviconUrl: isWhiteLabelingEnabled && faviconUrl?.IsLocked != true,
            CanChangeCustomCssUrl: isWhiteLabelingEnabled && customCssUrl?.IsLocked != true);
    }

    public IReadOnlyList<string> ValidateAllowedChanges(
        BrandingSettings currentPayload,
        BrandingSettings requestedPayload,
        TenantBrandingSettingsDocumentLockState lockState)
    {
        List<string> errors = [];
        AddIfDisallowedChange(errors, "Display name", currentPayload.DisplayName, requestedPayload.DisplayName, lockState.CanChangeDisplayName);
        AddIfDisallowedChange(errors, "Logo URL", currentPayload.LogoUrl, requestedPayload.LogoUrl, lockState.CanChangeLogoUrl);
        AddIfDisallowedChange(errors, "Favicon URL", currentPayload.FaviconUrl, requestedPayload.FaviconUrl, lockState.CanChangeFaviconUrl);
        AddIfDisallowedChange(errors, "Custom CSS URL", currentPayload.CustomCssUrl, requestedPayload.CustomCssUrl, lockState.CanChangeCustomCssUrl);
        return errors;
    }

    private static void AddIfDisallowedChange(
        List<string> errors,
        string fieldName,
        string? currentValue,
        string? requestedValue,
        bool canChange)
    {
        if (canChange || string.Equals(Normalize(currentValue), Normalize(requestedValue), StringComparison.Ordinal))
        {
            return;
        }

        errors.Add($"{fieldName} cannot be changed because instance branding governance currently locks this tenant override.");
    }

    private static bool DeserializeBoolean(string? rawValue, bool fallback)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return fallback;
        }

        try
        {
            return JsonSerializer.Deserialize<bool>(rawValue);
        }
        catch (JsonException)
        {
            return bool.TryParse(rawValue.Trim('"'), out var parsed) ? parsed : fallback;
        }
    }

    private static string DeserializeString(string? rawValue, string fallback)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return fallback;
        }

        try
        {
            return JsonSerializer.Deserialize<string>(rawValue) ?? fallback;
        }
        catch (JsonException)
        {
            return rawValue.Trim('"');
        }
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
