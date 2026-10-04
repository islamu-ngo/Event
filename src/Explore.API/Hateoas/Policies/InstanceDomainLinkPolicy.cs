namespace Explore.API.Hateoas.Policies;

using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.InstanceAdmin;
using Explore.Application.Features.InstanceAdmin.Requests.Queries;
using Explore.Application.Hateoas;

public sealed class InstanceDomainLinkPolicy : ILinkPolicy<InstanceDomainOverviewDto>
{
    public IEnumerable<LinkDefinition> GetLinks(InstanceDomainOverviewDto dto, ClaimsPrincipal? user)
    {
        _ = dto;
        _ = user;

        yield return InstanceSettingViewLink(
            LinkRelations.Self,
            RouteNames.GetInstanceAdminDomains,
            "GET",
            "Instance administration domains",
            GetInstanceDomainsQuery.SettingKey);

        yield return InstanceSettingViewLink(
            "overview",
            RouteNames.GetInstanceAdminOverview,
            "GET",
            "Instance administration overview",
            GetInstanceOverviewQuery.SettingKey);

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

public sealed class InstanceDomainCollectionLinkPolicy : ICollectionLinkPolicy<InstanceDomainOverviewDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(InstanceDomainOverviewDto dto, ClaimsPrincipal? user) => [];

    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user) => [];
}
