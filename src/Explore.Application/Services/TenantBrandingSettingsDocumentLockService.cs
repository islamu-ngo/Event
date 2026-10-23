namespace Explore.Application.Services;

using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Domain.Constants;
using Explore.Domain.Settings.Documents.Payloads;

public sealed class TenantBrandingSettingsDocumentLockService(
    ISystemSettingRepository systemSettingRepository,
    ISettingMutationLock mutationLock)
    : ITenantBrandingSettingsDocumentLockService
{
    public Task<T> ExecuteWithGovernanceLocksAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default) =>
        mutationLock.ExecuteOrderedGroupsAsync(
            [[
                GovernanceSettingKeys.Deployment.Mode,
                GovernanceSettingKeys.Tenants.WhiteLabelingEnabled,
                GovernanceSettingKeys.Branding.DisplayName,
                GovernanceSettingKeys.Branding.LogoUrl,
                GovernanceSettingKeys.Branding.FaviconUrl,
                GovernanceSettingKeys.Branding.CustomCssUrl
            ]],
            operation,
            cancellationToken);

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
        var displayName = await systemSettingRepository.GetByKey(GovernanceSettingKeys.Branding.DisplayName, cancellationToken);
        var logoUrl = await systemSettingRepository.GetByKey(GovernanceSettingKeys.Branding.LogoUrl, cancellationToken);
        var faviconUrl = await systemSettingRepository.GetByKey(GovernanceSettingKeys.Branding.FaviconUrl, cancellationToken);
        var customCssUrl = await systemSettingRepository.GetByKey(GovernanceSettingKeys.Branding.CustomCssUrl, cancellationToken);

        return AssessLockState(deploymentMode?.Value, whiteLabeling?.Value, displayName?.IsLocked == true,
            logoUrl?.IsLocked == true, faviconUrl?.IsLocked == true, customCssUrl?.IsLocked == true);
    }

    public IReadOnlyList<string> ValidateAllowedChanges(
        BrandingSettings currentPayload,
        BrandingSettings requestedPayload,
        TenantBrandingSettingsDocumentLockState lockState) =>
        ValidateChanges(currentPayload, requestedPayload, lockState);

    /// <summary>Assesses already retained nonsecret governance facts without another repository read.</summary>
    public static TenantBrandingSettingsDocumentLockState AssessLockState(string? deploymentMode,
        string? whiteLabeling, bool displayNameLocked, bool logoLocked, bool faviconLocked, bool customCssLocked)
    {
        if (!DeserializeString(deploymentMode, "SingleTenant").Equals("MultiTenant", StringComparison.OrdinalIgnoreCase))
            return TenantBrandingSettingsDocumentLockState.AllowAll;
        bool enabled = DeserializeBoolean(whiteLabeling, false);
        return new(!displayNameLocked, enabled && !logoLocked, enabled && !faviconLocked, enabled && !customCssLocked);
    }

    public static IReadOnlyList<string> ValidateChanges(BrandingSettings currentPayload,
        BrandingSettings requestedPayload, TenantBrandingSettingsDocumentLockState lockState)
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
