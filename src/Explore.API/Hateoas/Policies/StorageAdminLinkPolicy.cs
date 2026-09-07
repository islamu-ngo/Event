namespace Explore.API.Hateoas.Policies;

using System.Collections.Generic;
using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.DTOs.Tenant;
using Explore.Application.Hateoas;

public sealed class InstanceStorageSettingsLinkPolicy : ILinkPolicy<InstanceStorageSettingsDto>
{
    private const string ResourceId = "storage";
    private const string SettingKey = "storage";

    public IEnumerable<LinkDefinition> GetLinks(InstanceStorageSettingsDto dto, ClaimsPrincipal? user)
    {
        _ = dto;

        yield return new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetInstanceStorageSettings,
            null,
            "GET",
            "Instance storage settings",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.InstanceSettings.View,
                ResourceKinds.InstanceSetting,
                ResourceId,
                facts: InstanceScopedAuthorizationFacts.Instance);

        yield return new LinkDefinition(
            LinkRelations.Edit,
            RouteNames.UpdateInstanceStorageSettings,
            null,
            "PATCH",
            "Patch instance storage settings",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.InstanceSettings.Update,
                ResourceKinds.InstanceSetting,
                ResourceId,
                facts: InstanceScopedAuthorizationFacts.Instance);

        yield return new LinkDefinition(
            "provider-test",
            RouteNames.TestInstanceStorageConnection,
            null,
            "POST",
            "Test storage provider",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.InstanceSettings.Update,
                ResourceKinds.InstanceSetting,
                ResourceId,
                facts: InstanceScopedAuthorizationFacts.Instance);

        yield return new LinkDefinition(
            "recalculate-usage",
            RouteNames.RecalculateInstanceStorageUsage,
            null,
            "POST",
            "Recalculate storage usage",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.InstanceSettings.Update,
                ResourceKinds.InstanceSetting,
                ResourceId,
                facts: InstanceScopedAuthorizationFacts.Instance);
    }
}

public sealed class InstanceStorageSettingsCollectionLinkPolicy : ICollectionLinkPolicy<InstanceStorageSettingsDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(InstanceStorageSettingsDto dto, ClaimsPrincipal? user) => [];

    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user) => [];
}

public sealed class TenantStorageSettingsLinkPolicy : ILinkPolicy<TenantStorageSettingsDto>
{
    private const string SettingKey = "storage";

    public IEnumerable<LinkDefinition> GetLinks(TenantStorageSettingsDto dto, ClaimsPrincipal? user)
    {
        yield return new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetTenantStorageSettings,
            null,
            "GET",
            "Tenant storage settings",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.TenantSettings.View,
                ResourceKinds.TenantSetting,
                TenantStorageResourceId(dto),
                TenantStorageScope(dto),
                TenantStorageFacts(dto));

        if (!CanUpdate(dto))
        {
            yield break;
        }

        yield return new LinkDefinition(
            LinkRelations.Edit,
            RouteNames.PatchTenantStorageSettings,
            null,
            "PATCH",
            "Patch tenant storage settings",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.TenantSettings.Update,
                ResourceKinds.TenantSetting,
                TenantStorageResourceId(dto),
                TenantStorageScope(dto),
                TenantStorageFacts(dto));

        yield return new LinkDefinition(
            "provider-test",
            RouteNames.TestTenantStorageConnection,
            null,
            "POST",
            "Test tenant storage provider",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.TenantSettings.Update,
                ResourceKinds.TenantSetting,
                TenantStorageResourceId(dto),
                TenantStorageScope(dto),
                TenantStorageFacts(dto));
    }

    private static bool CanUpdate(TenantStorageSettingsDto dto) =>
        dto.TenantOverridesAllowed
        && !dto.TenantStorageLocked
        && !dto.IsReadOnly;

    private static string TenantStorageResourceId(TenantStorageSettingsDto dto) =>
        $"{dto.TenantId}:storage";

    private static IAuthorizationFacts TenantStorageFacts(TenantStorageSettingsDto dto) =>
        new TenantSettingAuthorizationFacts(dto.TenantId, SettingKey, dto.TenantStorageLocked || dto.IsReadOnly);

    private static AuthorizationScope TenantStorageScope(TenantStorageSettingsDto dto) =>
        new(TenantId: dto.TenantId.ToString());
}

public sealed class TenantStorageSettingsCollectionLinkPolicy : ICollectionLinkPolicy<TenantStorageSettingsDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(TenantStorageSettingsDto dto, ClaimsPrincipal? user) => [];

    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user) => [];
}
