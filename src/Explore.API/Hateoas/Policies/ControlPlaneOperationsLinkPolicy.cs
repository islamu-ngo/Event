namespace Explore.API.Hateoas.Policies;

using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.ControlPlane;
using Explore.Application.Features.ControlPlane.Requests.Queries;
using Explore.Application.Hateoas;

public sealed class ControlPlaneOperationsLinkPolicy : ILinkPolicy<ControlPlaneOperationsDto>
{
    public IEnumerable<LinkDefinition> GetLinks(ControlPlaneOperationsDto dto, ClaimsPrincipal? user)
    {
        _ = dto;
        _ = user;

        yield return InstanceSettingLink(
            LinkRelations.Self,
            RouteNames.GetControlPlaneOperations,
            "GET",
            "Control-plane operations",
            GetControlPlaneOperationsQuery.SettingKey);

        yield return InstanceSettingLink(
            "overview",
            RouteNames.GetControlPlaneOverview,
            "GET",
            "Control-plane overview",
            GetControlPlaneOverviewQuery.SettingKey);

        yield return InstanceSettingLink(
            LinkRelations.DeploymentModeRunbook,
            RouteNames.GetControlPlaneDeploymentModeRunbook,
            "GET",
            "Deployment mode runbook",
            GetControlPlaneDeploymentModeRunbookQuery.SettingKey);

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

public sealed class ControlPlaneOperationsCollectionLinkPolicy : ICollectionLinkPolicy<ControlPlaneOperationsDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(ControlPlaneOperationsDto dto, ClaimsPrincipal? user) => [];

    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user) => [];
}
