namespace Explore.API.Hateoas.Policies;

using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.Onboarding;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Explore.Application.Features.ConfigurationManifest.Requests.Commands;
using Explore.Application.Features.ConfigurationManifest.Requests.Queries;
using Explore.Application.Features.ControlPlane.Requests.Queries;
using Explore.Application.Hateoas;

public sealed class TenantOnboardingStatusLinkPolicy : ILinkPolicy<TenantOnboardingStatusDto>
{
    private const string TenantOnboardingSettingKey = "onboarding";

    public IEnumerable<LinkDefinition> GetLinks(TenantOnboardingStatusDto dto, ClaimsPrincipal? user)
    {
        _ = user;

        yield return new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetTenantOnboardingStatus,
            Method: HttpMethods.Get,
            Title: "Tenant onboarding status",
            RequiresAuth: true);

        if (dto.TenantId == Guid.Empty || !dto.IsAuthenticated)
        {
            yield break;
        }

        if (dto.IsCurrentUserTenantAdministrator)
        {
            yield return new LinkDefinition(
                "manage-tenant-settings",
                RouteNames.GetTenantOnboardingPolicySettings,
                Method: HttpMethods.Get,
                Title: "Manage tenant settings",
                RequiresAuth: true)
                 .RequirePermission(AuthorizationActions.TenantSettings.Update,
                    ResourceKinds.TenantSetting,
                    $"{dto.TenantId}:{TenantOnboardingSettingKey}",
                    new AuthorizationScope(TenantId: dto.TenantId.ToString()),
                    new TenantSettingAuthorizationFacts(dto.TenantId, TenantOnboardingSettingKey, IsLockedByInstance: false));

            yield return new LinkDefinition(
                    LinkRelations.CreateConfigurationImportSession,
                    RouteNames.CreateTenantConfigurationImportSession,
                    new { tenantId = dto.TenantId },
                    HttpMethods.Post,
                    "Import tenant configuration package",
                    RequiresAuth: true)
                .RequirePermission(
                    AuthorizationActions.TenantSettings.Update,
                    ResourceKinds.TenantSetting,
                    CreateTenantConfigurationImportSessionCommand.ResourceKey,
                    facts: new TenantSettingAuthorizationFacts(
                        dto.TenantId,
                        CreateTenantConfigurationImportSessionCommand.ResourceKey));

            yield return new LinkDefinition(
                    LinkRelations.ExportTenantConfigurationPackage,
                    RouteNames.ExportTenantConfigurationPackage,
                    new
                    {
                        tenantId = dto.TenantId,
                        view = ConfigurationManifestExportView.Overrides
                    },
                    HttpMethods.Get,
                    "Export tenant configuration package",
                    RequiresAuth: true)
                .RequirePermission(
                    AuthorizationActions.TenantSettings.View,
                    ResourceKinds.TenantSetting,
                    ExportTenantConfigurationPackageQuery.ResourceKey,
                    facts: new TenantSettingAuthorizationFacts(
                        dto.TenantId,
                        ExportTenantConfigurationPackageQuery.ResourceKey));

            yield return new LinkDefinition(
                    LinkRelations.ConfigurationImportHistory,
                    RouteNames.ListTenantConfigurationImportHistory,
                    new { tenantId = dto.TenantId },
                    HttpMethods.Get,
                    "Tenant configuration import history",
                    RequiresAuth: true)
                .RequirePermission(
                    AuthorizationActions.TenantSettings.View,
                    ResourceKinds.TenantSetting,
                    CreateTenantConfigurationImportSessionCommand.ResourceKey,
                    facts: new TenantSettingAuthorizationFacts(
                        dto.TenantId,
                        CreateTenantConfigurationImportSessionCommand.ResourceKey));

            yield return new LinkDefinition(
                    LinkRelations.CreateConfigurationDirectTransfer,
                    RouteNames.CreateTenantConfigurationTransfer,
                    new { tenantId = dto.TenantId },
                    HttpMethods.Post,
                    "Create direct tenant configuration transfer",
                    RequiresAuth: true)
                .RequirePermission(
                    AuthorizationActions.TenantSettings.Update,
                    ResourceKinds.TenantSetting,
                    CreateTenantConfigurationImportSessionCommand.ResourceKey,
                    facts: new TenantSettingAuthorizationFacts(
                        dto.TenantId,
                        CreateTenantConfigurationImportSessionCommand.ResourceKey));

            if (!dto.IsCompleted)
            {
                yield return new LinkDefinition(
                    "complete",
                    RouteNames.CompleteTenantOnboarding,
                    Method: HttpMethods.Post,
                    Title: "Complete tenant onboarding",
                    RequiresAuth: true)
                    .RequirePermission(AuthorizationActions.TenantSettings.Update,
                        ResourceKinds.TenantSetting,
                        $"{dto.TenantId}:{TenantOnboardingSettingKey}",
                        new AuthorizationScope(TenantId: dto.TenantId.ToString()),
                        new TenantSettingAuthorizationFacts(dto.TenantId, TenantOnboardingSettingKey, IsLockedByInstance: false));
            }
        }

        if (!dto.IsCurrentUserPlatformAdministrator)
        {
            yield break;
        }

        yield return new LinkDefinition(
            "manage-control-plane",
            RouteNames.GetControlPlaneTenantById,
            new { tenantId = dto.TenantId },
            HttpMethods.Get,
            "Manage tenant from the control plane",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.InstanceSettings.View,
                ResourceKinds.InstanceSetting,
                GetControlPlaneTenantListQuery.SettingKey,
                facts: InstanceScopedAuthorizationFacts.Instance);

        if (!dto.IsCompleted && !dto.IsCurrentUserTenantAdministrator)
        {
            yield return new LinkDefinition(
                "complete",
                RouteNames.CompleteTenantOnboarding,
                Method: HttpMethods.Post,
                Title: "Complete tenant onboarding",
                RequiresAuth: true)
                .RequirePermission(AuthorizationActions.InstanceSettings.Update,
                    ResourceKinds.InstanceSetting,
                    GetControlPlaneTenantListQuery.SettingKey,
                    facts: InstanceScopedAuthorizationFacts.Instance);
        }
    }
}

public sealed class TenantOnboardingStatusCollectionLinkPolicy
    : ICollectionLinkPolicy<TenantOnboardingStatusDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(TenantOnboardingStatusDto dto, ClaimsPrincipal? user) => [];

    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user) => [];
}
