namespace Explore.Application.Settings;

using Explore.Domain.Constants;

public static class TenantBrandingGovernanceMutationLockKeys
{
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(
    [
        GovernanceSettingKeys.Deployment.Mode,
        GovernanceSettingKeys.Tenants.WhiteLabelingEnabled,
        GovernanceSettingKeys.Branding.DisplayName,
        GovernanceSettingKeys.Branding.LogoUrl,
        GovernanceSettingKeys.Branding.FaviconUrl,
        GovernanceSettingKeys.Branding.CustomCssUrl
    ]);
}
