using Explore.API.Hateoas;
using Explore.API.Hateoas.Policies;
using Explore.Application.Authorization;
using System.Text.Json;
using Explore.Application.DTOs.ControlPlane;
using Explore.Application.Features.ConfigurationManifest.Requests.Queries;
using Explore.Application.Features.ControlPlane.Requests.Queries;
using Explore.Application.Hateoas;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features.Hateoas;

public sealed class ControlPlaneOverviewHateoasTests
{
    [Test]
    public async Task ControlPlaneOverviewLinks_ExposeInstanceSettingPermissionMetadata()
    {
        var policy = new ControlPlaneOverviewLinkPolicy();

        var links = policy.GetLinks(new ControlPlaneOverviewDto(), user: null).ToArray();

        var self = links.Single(link => link.Rel == LinkRelations.Self);
        await Assert.That(self.RouteName).IsEqualTo(RouteNames.GetControlPlaneOverview);
        await Assert.That(self.Method).IsEqualTo("GET");
        await Assert.That(self.RequiresAuth).IsTrue();
        await Assert.That(self.PermissionResourceKind).IsEqualTo(ResourceKinds.InstanceSetting);
        await Assert.That(self.PermissionAction).IsEqualTo(AuthorizationActions.InstanceSettings.View);
        await Assert.That(self.PermissionResourceId).IsEqualTo(GetControlPlaneOverviewQuery.SettingKey);
        await Assert.That(self.PermissionFacts).IsEqualTo(InstanceScopedAuthorizationFacts.Instance);

        var domains = links.Single(link => link.Rel == "domains");
        await Assert.That(domains.RouteName).IsEqualTo(RouteNames.GetControlPlaneDomains);
        await Assert.That(domains.PermissionResourceKind).IsEqualTo(ResourceKinds.InstanceSetting);
        await Assert.That(domains.PermissionAction).IsEqualTo(AuthorizationActions.InstanceSettings.View);
        await Assert.That(domains.PermissionResourceId).IsEqualTo(GetControlPlaneDomainsQuery.SettingKey);

        var operations = links.Single(link => link.Rel == "operations");
        await Assert.That(operations.RouteName).IsEqualTo(RouteNames.GetControlPlaneOperations);
        await Assert.That(operations.PermissionResourceKind).IsEqualTo(ResourceKinds.InstanceSetting);
        await Assert.That(operations.PermissionAction).IsEqualTo(AuthorizationActions.InstanceSettings.View);
        await Assert.That(operations.PermissionResourceId).IsEqualTo(GetControlPlaneOperationsQuery.SettingKey);

        var plans = links.Single(link => link.Rel == "plans");
        await Assert.That(plans.RouteName).IsEqualTo(RouteNames.GetControlPlaneTenantPlans);
        await Assert.That(plans.Method).IsEqualTo("GET");
        await Assert.That(plans.RequiresAuth).IsTrue();
        await Assert.That(plans.PermissionResourceKind).IsEqualTo(ResourceKinds.InstanceSetting);
        await Assert.That(plans.PermissionAction).IsEqualTo(AuthorizationActions.InstanceSettings.View);
        await Assert.That(plans.PermissionResourceId).IsEqualTo(GetControlPlaneTenantPlanListQuery.SettingKey);
        await Assert.That(plans.PermissionFacts).IsEqualTo(InstanceScopedAuthorizationFacts.Instance);

        var storage = links.Single(link => link.Rel == "storage");
        await Assert.That(storage.RouteName).IsEqualTo(RouteNames.GetInstanceStorageSettings);
        await Assert.That(storage.PermissionResourceKind).IsEqualTo(ResourceKinds.InstanceSetting);
        await Assert.That(storage.PermissionAction).IsEqualTo(AuthorizationActions.InstanceSettings.View);

        var authentication = links.Single(link => link.Rel == "authentication");
        await Assert.That(authentication.RouteName).IsEqualTo(RouteNames.GetInstanceAuthProviderConfigurationStatus);
        await Assert.That(authentication.PermissionResourceKind).IsEqualTo(ResourceKinds.InstanceSetting);
        await Assert.That(authentication.PermissionAction).IsEqualTo(AuthorizationActions.InstanceSettings.View);

        var authorization = links.Single(link => link.Rel == "authorization");
        await Assert.That(authorization.RouteName).IsEqualTo(RouteNames.GetInstanceAuthorizationProviderConfigurationStatus);
        await Assert.That(authorization.PermissionResourceKind).IsEqualTo(ResourceKinds.InstanceSetting);
        await Assert.That(authorization.PermissionAction).IsEqualTo(AuthorizationActions.InstanceSettings.View);

        await AssertExportLink(
            links.Single(link => link.Rel == LinkRelations.ExportConfigurationOverrides),
            ConfigurationManifestExportView.Overrides);
        await AssertExportLink(
            links.Single(link => link.Rel == LinkRelations.ExportConfigurationPortable),
            ConfigurationManifestExportView.Portable);
    }

    private static async Task AssertExportLink(
        Explore.Application.Hateoas.LinkDefinition link,
        ConfigurationManifestExportView view)
    {
        JsonElement routeValues = JsonSerializer.SerializeToElement(link.RouteValues);

        await Assert.That(link.RouteName).IsEqualTo(RouteNames.ExportConfigurationManifest);
        await Assert.That(link.Method).IsEqualTo("GET");
        await Assert.That(link.RequiresAuth).IsTrue();
        await Assert.That(link.PermissionResourceKind).IsEqualTo(ResourceKinds.InstanceSetting);
        await Assert.That(link.PermissionAction).IsEqualTo(AuthorizationActions.InstanceSettings.View);
        await Assert.That(link.PermissionResourceId).IsEqualTo(ExportConfigurationManifestQuery.ResourceKey);
        await Assert.That(link.PermissionFacts)
            .IsEqualTo(new ConfigurationManifestExportAuthorizationFacts());
        await Assert.That(routeValues.GetProperty("view").GetString()).IsEqualTo(view.ToString());
        await Assert.That(routeValues.TryGetProperty("tenantId", out _)).IsFalse();
    }
}
