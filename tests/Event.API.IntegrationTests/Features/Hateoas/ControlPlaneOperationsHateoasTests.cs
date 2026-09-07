using Explore.API.Hateoas;
using Explore.API.Hateoas.Policies;
using Explore.Application.Authorization;
using Explore.Application.DTOs.ControlPlane;
using Explore.Application.Features.ControlPlane.Requests.Queries;
using Explore.Application.Hateoas;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features.Hateoas;

public sealed class ControlPlaneOperationsHateoasTests
{
    [Test]
    public async Task ControlPlaneOperationsLinks_ExposeInstanceSettingPermissionMetadata()
    {
        var policy = new ControlPlaneOperationsLinkPolicy();

        var links = policy.GetLinks(new ControlPlaneOperationsDto(), user: null).ToArray();

        var self = links.Single(link => link.Rel == LinkRelations.Self);
        await Assert.That(self.RouteName).IsEqualTo(RouteNames.GetControlPlaneOperations);
        await Assert.That(self.Method).IsEqualTo("GET");
        await Assert.That(self.RequiresAuth).IsTrue();
        await Assert.That(self.PermissionResourceKind).IsEqualTo(ResourceKinds.InstanceSetting);
        await Assert.That(self.PermissionAction).IsEqualTo(AuthorizationActions.InstanceSettings.View);
        await Assert.That(self.PermissionResourceId).IsEqualTo(GetControlPlaneOperationsQuery.SettingKey);
        await Assert.That(self.PermissionFacts).IsEqualTo(InstanceScopedAuthorizationFacts.Instance);

        var overview = links.Single(link => link.Rel == "overview");
        await Assert.That(overview.RouteName).IsEqualTo(RouteNames.GetControlPlaneOverview);
        await Assert.That(overview.PermissionResourceId).IsEqualTo(GetControlPlaneOverviewQuery.SettingKey);

        var storage = links.Single(link => link.Rel == "storage");
        await Assert.That(storage.RouteName).IsEqualTo(RouteNames.GetInstanceStorageSettings);
        await Assert.That(storage.PermissionAction).IsEqualTo(AuthorizationActions.InstanceSettings.View);
    }
}
