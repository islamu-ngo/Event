using Explore.API.Hateoas;
using Explore.API.Hateoas.Policies;
using Explore.Application.Authorization;
using Explore.Application.DTOs.ControlPlane;
using Explore.Application.Features.ControlPlane.Requests.Queries;
using Explore.Application.Hateoas;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features.Hateoas;

public sealed class ControlPlaneDomainHateoasTests
{
    [Test]
    public async Task ControlPlaneDomainLinks_ExposeInstanceSettingPermissionMetadata()
    {
        var policy = new ControlPlaneDomainLinkPolicy();

        var links = policy.GetLinks(new ControlPlaneDomainOverviewDto(), user: null).ToArray();

        var self = links.Single(link => link.Rel == LinkRelations.Self);
        await Assert.That(self.RouteName).IsEqualTo(RouteNames.GetControlPlaneDomains);
        await Assert.That(self.Method).IsEqualTo("GET");
        await Assert.That(self.RequiresAuth).IsTrue();
        await Assert.That(self.PermissionResourceKind).IsEqualTo(ResourceKinds.InstanceSetting);
        await Assert.That(self.PermissionAction).IsEqualTo(AuthorizationActions.InstanceSettings.View);
        await Assert.That(self.PermissionResourceId).IsEqualTo(GetControlPlaneDomainsQuery.SettingKey);
        await Assert.That(self.PermissionFacts).IsEqualTo(InstanceScopedAuthorizationFacts.Instance);

        var overview = links.Single(link => link.Rel == "overview");
        await Assert.That(overview.RouteName).IsEqualTo(RouteNames.GetControlPlaneOverview);
        await Assert.That(overview.PermissionResourceId).IsEqualTo(GetControlPlaneOverviewQuery.SettingKey);

        var settings = links.Single(link => link.Rel == "settings");
        await Assert.That(settings.RouteName).IsEqualTo(RouteNames.GetInstanceDomainSettings);
        await Assert.That(settings.PermissionAction).IsEqualTo(AuthorizationActions.InstanceSettings.View);

        var edit = links.Single(link => link.Rel == LinkRelations.Edit);
        await Assert.That(edit.RouteName).IsEqualTo(RouteNames.UpdateInstanceDomainSettings);
        await Assert.That(edit.Method).IsEqualTo("PATCH");
        await Assert.That(edit.PermissionAction).IsEqualTo(AuthorizationActions.InstanceSettings.Update);
    }
}
