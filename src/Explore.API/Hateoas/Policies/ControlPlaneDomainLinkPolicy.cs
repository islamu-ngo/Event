namespace Explore.API.Hateoas.Policies;

using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.ControlPlane;
using Explore.Application.Features.ControlPlane.Requests.Queries;
using Explore.Application.Hateoas;

public sealed class ControlPlaneDomainLinkPolicy : ILinkPolicy<ControlPlaneDomainOverviewDto>
{
    public IEnumerable<LinkDefinition> GetLinks(ControlPlaneDomainOverviewDto dto, ClaimsPrincipal? user)
    {
        _ = dto;
        _ = user;

        yield return InstanceSettingViewLink(
            LinkRelations.Self,
            RouteNames.GetControlPlaneDomains,
            "GET",
            "Control-plane domains",
            GetControlPlaneDomainsQuery.SettingKey);

        yield return InstanceSettingViewLink(
            "overview",
            RouteNames.GetControlPlaneOverview,
            "GET",
            "Control-plane overview",
            GetControlPlaneOverviewQuery.SettingKey);

        yield return InstanceSettingViewLink(
            "settings",
            RouteNames.GetInstanceDomainSettings,
            "GET",
            "Domain settings",
            "domains");

        yield return InstanceSettingUpdateLink(
            LinkRelations.Edit,
            RouteNames.UpdateInstanceDomainSettings,
            "PATCH",
            "Update domain settings",
            "domains");
    }

    private static LinkDefinition InstanceSettingViewLink(
        string rel,
        string routeName,
        string method,
        string title,
        string settingKey) =>
        new LinkDefinition(rel, routeName, null, method, title, RequiresAuth: true)
            .RequirePermission(AuthorizationActions.InstanceSettings.View,
                ResourceKinds.InstanceSetting,
                settingKey,
                facts: InstanceScopedAuthorizationFacts.Instance);

    private static LinkDefinition InstanceSettingUpdateLink(
        string rel,
        string routeName,
        string method,
        string title,
        string settingKey) =>
        new LinkDefinition(rel, routeName, null, method, title, RequiresAuth: true)
            .RequirePermission(AuthorizationActions.InstanceSettings.Update,
                ResourceKinds.InstanceSetting,
                settingKey,
                facts: InstanceScopedAuthorizationFacts.Instance);
}

public sealed class ControlPlaneDomainCollectionLinkPolicy : ICollectionLinkPolicy<ControlPlaneDomainOverviewDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(ControlPlaneDomainOverviewDto dto, ClaimsPrincipal? user) => [];

    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user) => [];
}
