namespace Explore.API.Hateoas.Policies;

using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.InstanceAdmin;
using Explore.Application.Features.InstanceAdmin.Requests.Commands;
using Explore.Application.Features.InstanceAdmin.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Domain.Enums;

public sealed class InstanceTenantDetailLinkPolicy : ILinkPolicy<InstanceTenantDetailDto>
{
    public IEnumerable<LinkDefinition> GetLinks(InstanceTenantDetailDto dto, ClaimsPrincipal? user)
    {
        _ = user;

        yield return ViewLink(
            LinkRelations.Self,
            RouteNames.GetInstanceAdminTenantById,
            new { tenantId = dto.Id },
            "Instance administration tenant detail");

        yield return ViewLink(
            LinkRelations.Collection,
            RouteNames.GetInstanceAdminTenants,
            null,
            "Instance administration tenants");

        yield return ViewLink(
            "overview",
            RouteNames.GetInstanceAdminOverview,
            null,
            "Instance administration overview");

        yield return new LinkDefinition(
            "configuration",
            RouteNames.GetInstanceAdminTenantEffectiveConfiguration,
            new { tenantId = dto.Id },
            "GET",
            "Tenant effective configuration",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.InstanceSettings.View,
                ResourceKinds.InstanceSetting,
                GetInstanceTenantEffectiveConfigurationQuery.SettingKey,
                facts: InstanceScopedAuthorizationFacts.Instance);

        foreach (var link in InstanceTenantLifecycleLinks.GetLinks(dto.Id, dto.StatusId))
        {
            yield return link;
        }
    }

    private static LinkDefinition ViewLink(string rel, string routeName, object? routeValues, string title) =>
        new LinkDefinition(rel, routeName, routeValues, "GET", title, RequiresAuth: true)
            .RequirePermission(AuthorizationActions.InstanceSettings.View,
                ResourceKinds.InstanceSetting,
                GetInstanceTenantListQuery.SettingKey,
                facts: InstanceScopedAuthorizationFacts.Instance);
}

public sealed class InstanceTenantCollectionLinkPolicy : ICollectionLinkPolicy<InstanceTenantListItemDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(InstanceTenantListItemDto dto, ClaimsPrincipal? user)
    {
        _ = user;

        yield return new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetInstanceAdminTenantById,
            new { tenantId = dto.Id },
            "GET",
            dto.FullName,
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.InstanceSettings.View,
                ResourceKinds.InstanceSetting,
                GetInstanceTenantListQuery.SettingKey,
                facts: InstanceScopedAuthorizationFacts.Instance);

        yield return new LinkDefinition(
            "configuration",
            RouteNames.GetInstanceAdminTenantEffectiveConfiguration,
            new { tenantId = dto.Id },
            "GET",
            "Tenant effective configuration",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.InstanceSettings.View,
                ResourceKinds.InstanceSetting,
                GetInstanceTenantEffectiveConfigurationQuery.SettingKey,
                facts: InstanceScopedAuthorizationFacts.Instance);

        foreach (var link in InstanceTenantLifecycleLinks.GetLinks(dto.Id, dto.StatusId))
        {
            yield return link;
        }
    }

    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user)
    {
        _ = user;

        yield return new LinkDefinition(
            LinkRelations.Create,
            RouteNames.CreateInstanceAdminTenant,
            null,
            "POST",
            "Create tenant",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Create, ResourceKinds.Tenant);
    }
}

internal static class InstanceTenantLifecycleLinks
{
    public static IEnumerable<LinkDefinition> GetLinks(Guid tenantId, int statusId)
    {
        var status = (TenantStatusEnum)statusId;

        if (status is TenantStatusEnum.Provisioning)
        {
            yield return UpdateLink("activate", RouteNames.ActivateInstanceAdminTenant, tenantId, TenantStatusEnum.Active, "Activate tenant");
        }

        if (status is TenantStatusEnum.Active or TenantStatusEnum.Provisioning)
        {
            yield return UpdateLink("suspend", RouteNames.SuspendInstanceAdminTenant, tenantId, TenantStatusEnum.Suspended, "Suspend tenant");
        }

        if (status is TenantStatusEnum.Active or TenantStatusEnum.Provisioning or TenantStatusEnum.Suspended)
        {
            yield return UpdateLink(LinkRelations.Archive, RouteNames.ArchiveInstanceAdminTenant, tenantId, TenantStatusEnum.Archived, "Archive tenant");
        }

        if (status is TenantStatusEnum.Suspended or TenantStatusEnum.Archived)
        {
            yield return UpdateLink("reactivate", RouteNames.ReactivateInstanceAdminTenant, tenantId, TenantStatusEnum.Active, "Reactivate tenant");
        }

        if (status is TenantStatusEnum.Archived)
        {
            yield return UpdateLink("schedule-purge", RouteNames.ScheduleInstanceAdminTenantPurge, tenantId, TenantStatusEnum.Purged, "Schedule tenant purge");
        }
    }

    private static LinkDefinition UpdateLink(
        string rel,
        string routeName,
        Guid tenantId,
        TenantStatusEnum targetStatus,
        string title) =>
        new LinkDefinition(rel, routeName, new { tenantId }, "POST", title, RequiresAuth: true)
            .RequirePermission(AuthorizationActions.InstanceSettings.Update,
                ResourceKinds.InstanceSetting,
                TransitionInstanceTenantLifecycleCommand.SettingKey,
                facts: InstanceScopedAuthorizationFacts.Instance);
}
