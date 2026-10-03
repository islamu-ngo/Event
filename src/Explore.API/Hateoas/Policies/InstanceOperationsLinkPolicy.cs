namespace Explore.API.Hateoas.Policies;

using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.InstanceAdmin;
using Explore.Application.Features.InstanceAdmin.Requests.Queries;
using Explore.Application.Hateoas;

public sealed class InstanceOperationsLinkPolicy : ILinkPolicy<InstanceOperationsDto>
{
    public IEnumerable<LinkDefinition> GetLinks(InstanceOperationsDto dto, ClaimsPrincipal? user)
    {
        _ = dto;
        _ = user;

        yield return InstanceSettingLink(
            LinkRelations.Self,
            RouteNames.GetInstanceAdminOperations,
            "GET",
            "Instance administration operations",
            GetInstanceOperationsQuery.SettingKey);

        yield return InstanceSettingLink(
            "overview",
            RouteNames.GetInstanceAdminOverview,
            "GET",
            "Instance administration overview",
            GetInstanceOverviewQuery.SettingKey);

        yield return InstanceSettingLink(
            LinkRelations.DeploymentModeRunbook,
            RouteNames.GetInstanceAdminDeploymentModeRunbook,
            "GET",
            "Deployment mode runbook",
            GetInstanceDeploymentModeRunbookQuery.SettingKey);

        yield return InstanceSettingLink(
            "storage",
            RouteNames.GetInstanceStorageSettings,
            "GET",
            "Storage settings",
            "storage");
    }

    private static LinkDefinition InstanceSettingLink(
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
}

public sealed class InstanceOperationsCollectionLinkPolicy : ICollectionLinkPolicy<InstanceOperationsDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(InstanceOperationsDto dto, ClaimsPrincipal? user) => [];

    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user) => [];
}
