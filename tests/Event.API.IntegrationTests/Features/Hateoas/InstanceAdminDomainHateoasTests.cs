using Explore.API.Hateoas;
using Explore.API.Hateoas.Policies;
using Explore.Application.Authorization;
using Explore.Application.DTOs.InstanceAdmin;
using Explore.Application.Features.InstanceAdmin.Requests.Queries;
using Explore.Application.Hateoas;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features.Hateoas;

public sealed class InstanceAdminDomainHateoasTests
{
    [Test]
    public async Task ControlPlaneDomainLinks_ExposeInstanceSettingPermissionMetadata()
    {
        var policy = new InstanceDomainLinkPolicy();

        var links = policy.GetLinks(new InstanceDomainOverviewDto(), user: null).ToArray();

        var self = links.Single(link => link.Rel == LinkRelations.Self);
        await Assert.That(self.RouteName).IsEqualTo(RouteNames.GetInstanceAdminDomains);
        await Assert.That(self.Method).IsEqualTo("GET");
        await Assert.That(self.RequiresAuth).IsTrue();
        await Assert.That(self.PermissionResourceKind).IsEqualTo(ResourceKinds.InstanceSetting);
        await Assert.That(self.PermissionAction).IsEqualTo(AuthorizationActions.InstanceSettings.View);
        await Assert.That(self.PermissionResourceId).IsEqualTo(GetInstanceDomainsQuery.SettingKey);
        await Assert.That(self.PermissionFacts).IsEqualTo(InstanceScopedAuthorizationFacts.Instance);

        var overview = links.Single(link => link.Rel == "overview");
        await Assert.That(overview.RouteName).IsEqualTo(RouteNames.GetInstanceAdminOverview);
        await Assert.That(overview.PermissionResourceId).IsEqualTo(GetInstanceOverviewQuery.SettingKey);

        var settings = links.Single(link => link.Rel == "settings");
        await Assert.That(settings.RouteName).IsEqualTo(RouteNames.GetInstanceDomainSettings);
        await Assert.That(settings.PermissionAction).IsEqualTo(AuthorizationActions.InstanceSettings.View);

        var edit = links.Single(link => link.Rel == LinkRelations.Edit);
        await Assert.That(edit.RouteName).IsEqualTo(RouteNames.UpdateInstanceDomainSettings);
        await Assert.That(edit.Method).IsEqualTo("PATCH");
        await Assert.That(edit.PermissionAction).IsEqualTo(AuthorizationActions.InstanceSettings.Update);
    }
}
