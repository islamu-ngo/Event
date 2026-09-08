namespace Explore.API.Hateoas.Policies;

using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.ControlPlane;
using Explore.Application.Features.ControlPlane.Requests.Commands;
using Explore.Application.Features.ControlPlane.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Domain.Enums;

public sealed class ControlPlaneTenantDetailLinkPolicy : ILinkPolicy<ControlPlaneTenantDetailDto>
{
    public IEnumerable<LinkDefinition> GetLinks(ControlPlaneTenantDetailDto dto, ClaimsPrincipal? user)
    {
        _ = user;

        yield return ViewLink(
            LinkRelations.Self,
            RouteNames.GetControlPlaneTenantById,
            new { tenantId = dto.Id },
            "Control-plane tenant detail");

        yield return ViewLink(
            LinkRelations.Collection,
            RouteNames.GetControlPlaneTenants,
            null,
            "Control-plane tenants");

        yield return ViewLink(
            "overview",
            RouteNames.GetControlPlaneOverview,
            null,
            "Control-plane overview");

        yield return new LinkDefinition(
            "configuration",
            RouteNames.GetControlPlaneTenantEffectiveConfiguration,
            new { tenantId = dto.Id },
            "GET",
            "Tenant effective configuration",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.InstanceSettings.View,
                ResourceKinds.InstanceSetting,
                GetControlPlaneTenantEffectiveConfigurationQuery.SettingKey,
                facts: InstanceScopedAuthorizationFacts.Instance);

        foreach (var link in ControlPlaneTenantLifecycleLinks.GetLinks(dto.Id, dto.StatusId))
        {
            yield return link;
        }
    }

    private static LinkDefinition ViewLink(string rel, string routeName, object? routeValues, string title) =>
        new LinkDefinition(rel, routeName, routeValues, "GET", title, RequiresAuth: true)
            .RequirePermission(AuthorizationActions.InstanceSettings.View,
                ResourceKinds.InstanceSetting,
                GetControlPlaneTenantListQuery.SettingKey,
                facts: InstanceScopedAuthorizationFacts.Instance);
}

public sealed class ControlPlaneTenantCollectionLinkPolicy : ICollectionLinkPolicy<ControlPlaneTenantListItemDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(ControlPlaneTenantListItemDto dto, ClaimsPrincipal? user)
    {
        _ = user;

        yield return new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetControlPlaneTenantById,
            new { tenantId = dto.Id },
            "GET",
            dto.FullName,
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.InstanceSettings.View,
                ResourceKinds.InstanceSetting,
                GetControlPlaneTenantListQuery.SettingKey,
                facts: InstanceScopedAuthorizationFacts.Instance);

        yield return new LinkDefinition(
            "configuration",
            RouteNames.GetControlPlaneTenantEffectiveConfiguration,
            new { tenantId = dto.Id },
            "GET",
            "Tenant effective configuration",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.InstanceSettings.View,
                ResourceKinds.InstanceSetting,
                GetControlPlaneTenantEffectiveConfigurationQuery.SettingKey,
                facts: InstanceScopedAuthorizationFacts.Instance);

        foreach (var link in ControlPlaneTenantLifecycleLinks.GetLinks(dto.Id, dto.StatusId))
        {
            yield return link;
        }
    }

    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user)
    {
        _ = user;

        yield return new LinkDefinition(
            LinkRelations.Create,
            RouteNames.CreateControlPlaneTenant,
            null,
            "POST",
            "Create tenant",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Create, ResourceKinds.Tenant);
    }
}

internal static class ControlPlaneTenantLifecycleLinks
{
    public static IEnumerable<LinkDefinition> GetLinks(Guid tenantId, int statusId)
    {
        var status = (TenantStatusEnum)statusId;

        if (status is TenantStatusEnum.Provisioning)
        {
            yield return UpdateLink("activate", RouteNames.ActivateControlPlaneTenant, tenantId, TenantStatusEnum.Active, "Activate tenant");
        }

        if (status is TenantStatusEnum.Active or TenantStatusEnum.Provisioning)
        {
            yield return UpdateLink("suspend", RouteNames.SuspendControlPlaneTenant, tenantId, TenantStatusEnum.Suspended, "Suspend tenant");
        }

        if (status is TenantStatusEnum.Active or TenantStatusEnum.Provisioning or TenantStatusEnum.Suspended)
        {
            yield return UpdateLink(LinkRelations.Archive, RouteNames.ArchiveControlPlaneTenant, tenantId, TenantStatusEnum.Archived, "Archive tenant");
        }

        if (status is TenantStatusEnum.Suspended or TenantStatusEnum.Archived)
        {
            yield return UpdateLink("reactivate", RouteNames.ReactivateControlPlaneTenant, tenantId, TenantStatusEnum.Active, "Reactivate tenant");
        }

        if (status is TenantStatusEnum.Archived)
        {
            yield return UpdateLink("schedule-purge", RouteNames.ScheduleControlPlaneTenantPurge, tenantId, TenantStatusEnum.Purged, "Schedule tenant purge");
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
                TransitionControlPlaneTenantLifecycleCommand.SettingKey,
                facts: InstanceScopedAuthorizationFacts.Instance);
}
