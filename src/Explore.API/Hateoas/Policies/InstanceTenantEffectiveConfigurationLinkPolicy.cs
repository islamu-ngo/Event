namespace Explore.API.Hateoas.Policies;

using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.InstanceAdmin;
using Explore.Application.Features.InstanceAdmin.Requests.Commands;
using Explore.Application.Features.InstanceAdmin.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Domain.Settings;

public sealed class InstanceTenantEffectiveConfigurationLinkPolicy
    : ILinkPolicy<InstanceTenantEffectiveConfigurationDto>
{
    public IEnumerable<LinkDefinition> GetLinks(InstanceTenantEffectiveConfigurationDto dto, ClaimsPrincipal? user)
    {
        _ = user;

        yield return ViewLink(
            LinkRelations.Self,
            RouteNames.GetInstanceAdminTenantEffectiveConfiguration,
            new { tenantId = dto.TenantId },
            "Tenant effective configuration",
            GetInstanceTenantEffectiveConfigurationQuery.SettingKey,
            dto.TenantId);

        yield return ViewLink(
            "plan-assignment",
            RouteNames.GetInstanceAdminTenantPlanAssignment,
            new { tenantId = dto.TenantId },
            "Tenant plan assignment",
            GetInstanceTenantPlanAssignmentQuery.SettingKey,
            dto.TenantId);

        yield return UpdateLink(
            "switch-plan",
            RouteNames.SwitchInstanceAdminTenantPlanAssignment,
            new { tenantId = dto.TenantId },
            "POST",
            "Switch tenant plan assignment",
            SwitchInstanceTenantPlanAssignmentCommand.SettingKey,
            dto.TenantId);

        if (dto.PlanAssignment is null)
        {
            yield break;
        }

        yield return UpdateLink(
            "apply",
            RouteNames.ApplyInstanceAdminTenantPlanAssignment,
            new { tenantId = dto.TenantId, assignmentId = dto.PlanAssignment.Id },
            "POST",
            "Apply tenant plan assignment",
            ApplyInstanceTenantPlanAssignmentCommand.SettingKey,
            dto.TenantId,
            dto.PlanAssignment.Id);

        if (dto.RollbackAssignment is not null)
        {
            yield return UpdateLink(
                "rollback",
                RouteNames.RollbackInstanceAdminTenantPlanAssignment,
                new { tenantId = dto.TenantId, assignmentId = dto.RollbackAssignment.Id },
                "POST",
                "Rollback tenant plan assignment",
                RollbackInstanceTenantPlanAssignmentCommand.SettingKey,
                dto.TenantId,
                dto.RollbackAssignment.Id);
        }

    }

    private static LinkDefinition ViewLink(
        string rel,
        string routeName,
        object routeValues,
        string title,
        string settingKey,
        Guid tenantId) =>
        new LinkDefinition(rel, routeName, routeValues, "GET", title, RequiresAuth: true)
            .RequirePermission(AuthorizationActions.InstanceSettings.View,
                ResourceKinds.InstanceSetting,
                settingKey,
                facts: InstanceScopedAuthorizationFacts.Instance);

    private static LinkDefinition UpdateLink(
        string rel,
        string routeName,
        object routeValues,
        string method,
        string title,
        string settingKey,
        Guid tenantId,
        Guid? assignmentId = null,
        string? settingTargetKey = null) =>
        new LinkDefinition(rel, routeName, routeValues, method, title, RequiresAuth: true)
            .RequirePermission(AuthorizationActions.InstanceSettings.Update,
                ResourceKinds.InstanceSetting,
                settingKey,
                facts: InstanceScopedAuthorizationFacts.Instance);
}

internal static class InstanceTenantEffectiveSettingLinks
{
    public static IEnumerable<LinkDefinition> GetLinks(
        Guid tenantId,
        InstanceTenantEffectiveSettingDto setting)
    {
        SettingDefinition? definition = SettingRegistry.Get(setting.Key);
        if (definition is null
            || SettingScope.Tenant < definition.MinScope
            || SettingScope.Tenant > definition.MaxScope
            || definition.IsSensitive
            || setting.IsSensitive
            || string.Equals(setting.ValueSource, "SystemLocked", StringComparison.Ordinal)
            || string.Equals(setting.LockSource, "SystemLocked", StringComparison.Ordinal))
        {
            yield break;
        }

        yield return UpdateLink(
            "override",
            RouteNames.SetInstanceAdminTenantSetting,
            "PUT",
            $"Override setting '{setting.Key}'",
            SetInstanceTenantSettingCommand.SettingKey,
            tenantId,
            setting.Key);

        if (definition.IsLockable
            && string.Equals(setting.ValueSource, "TenantLocked", StringComparison.Ordinal))
        {
            yield return UpdateLink(
                "unlock",
                RouteNames.UnlockInstanceAdminTenantSetting,
                "DELETE",
                $"Unlock setting '{setting.Key}'",
                UnlockInstanceTenantSettingCommand.SettingKey,
                tenantId,
                setting.Key);
        }
        else if (definition.IsLockable
            && string.Equals(setting.ValueSource, "TenantOverride", StringComparison.Ordinal))
        {
            yield return UpdateLink(
                "lock",
                RouteNames.LockInstanceAdminTenantSetting,
                "POST",
                $"Lock setting '{setting.Key}'",
                LockInstanceTenantSettingCommand.SettingKey,
                tenantId,
                setting.Key);
        }
    }

    private static LinkDefinition UpdateLink(
        string relation,
        string routeName,
        string method,
        string title,
        string resourceId,
        Guid tenantId,
        string settingKey) =>
        new LinkDefinition(
            relation,
            routeName,
            new { tenantId, key = settingKey },
            method,
            title,
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.InstanceSettings.Update,
                ResourceKinds.InstanceSetting,
                resourceId,
                facts: InstanceScopedAuthorizationFacts.Instance);
}

public sealed class InstanceTenantEffectiveConfigurationCollectionLinkPolicy
    : ICollectionLinkPolicy<InstanceTenantEffectiveConfigurationDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(InstanceTenantEffectiveConfigurationDto dto, ClaimsPrincipal? user) => [];

    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user) => [];
}
