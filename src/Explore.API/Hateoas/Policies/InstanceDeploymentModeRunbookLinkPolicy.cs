using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.InstanceAdmin;
using Explore.Application.Features.InstanceAdmin.Requests.Commands;
using Explore.Application.Features.InstanceAdmin.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Domain.Enums;

namespace Explore.API.Hateoas.Policies;

public sealed class InstanceDeploymentModeRunbookLinkPolicy : ILinkPolicy<InstanceDeploymentModeRunbookDto>
{
    public IEnumerable<LinkDefinition> GetLinks(InstanceDeploymentModeRunbookDto resource, ClaimsPrincipal? user)
    {
        yield return ViewLink(
            LinkRelations.Self,
            RouteNames.GetInstanceAdminDeploymentModeRunbook,
            "Deployment mode runbook");

        yield return ViewLink(
            "operations",
            RouteNames.GetInstanceAdminOperations,
            "Instance administration operations");

        foreach (var option in resource.TargetOptions.Where(option => option.Allowed))
        {
            if (!Enum.TryParse<DeploymentMode>(option.TargetMode, ignoreCase: false, out var targetMode))
            {
                continue;
            }

            yield return TransitionLink(targetMode, option.Label);
        }
    }

    private static LinkDefinition ViewLink(string rel, string routeName, string title) =>
        new LinkDefinition(rel, routeName, null, "GET", title, RequiresAuth: true)
            .RequirePermission(AuthorizationActions.InstanceSettings.View,
                ResourceKinds.InstanceSetting,
                GetInstanceDeploymentModeRunbookQuery.SettingKey,
                facts: InstanceScopedAuthorizationFacts.Instance);

    private static LinkDefinition TransitionLink(DeploymentMode targetMode, string title)
    {
        var rel = targetMode == DeploymentMode.MultiTenant
            ? LinkRelations.TransitionToMultiTenant
            : LinkRelations.TransitionToSingleTenant;

        return new LinkDefinition(
                rel,
                RouteNames.TransitionInstanceAdminDeploymentMode,
                null,
                "POST",
                title,
                RequiresAuth: true)
            .RequirePermission(AuthorizationActions.InstanceSettings.Update,
                ResourceKinds.InstanceSetting,
                TransitionInstanceDeploymentModeCommand.SettingKey,
                facts: InstanceScopedAuthorizationFacts.Instance);
    }
}

public sealed class InstanceDeploymentModeRunbookCollectionLinkPolicy
    : ICollectionLinkPolicy<InstanceDeploymentModeRunbookDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(InstanceDeploymentModeRunbookDto item, ClaimsPrincipal? user) => [];

    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user) => [];
}
