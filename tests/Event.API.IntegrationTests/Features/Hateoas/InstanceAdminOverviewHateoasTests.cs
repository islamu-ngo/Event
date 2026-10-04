using Explore.API.Hateoas;
using Explore.API.Hateoas.Policies;
using Explore.Application.Authorization;
using System.Text.Json;
using Explore.Application.DTOs.InstanceAdmin;
using Explore.Application.Features.ConfigurationManifest.Requests.Queries;
using Explore.Application.Features.InstanceAdmin.Requests.Queries;
using Explore.Application.Hateoas;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features.Hateoas;

public sealed class InstanceAdminOverviewHateoasTests
{
    [Test]
    public async Task OverviewLinks_ExposeInstanceSettingPermissionMetadata()
    {
        var policy = new InstanceOverviewLinkPolicy();

        var links = policy.GetLinks(new InstanceOverviewDto { DeploymentMode = "MultiTenant" }, user: null).ToArray();

        var self = links.Single(link => link.Rel == LinkRelations.Self);
        await Assert.That(self.RouteName).IsEqualTo(RouteNames.GetInstanceAdminOverview);
        await Assert.That(self.Method).IsEqualTo("GET");
        await Assert.That(self.RequiresAuth).IsTrue();
        await Assert.That(self.PermissionResourceKind).IsEqualTo(ResourceKinds.InstanceSetting);
        await Assert.That(self.PermissionAction).IsEqualTo(AuthorizationActions.InstanceSettings.View);
        await Assert.That(self.PermissionResourceId).IsEqualTo(GetInstanceOverviewQuery.SettingKey);
        await Assert.That(self.PermissionFacts).IsEqualTo(InstanceScopedAuthorizationFacts.Instance);

        var domains = links.Single(link => link.Rel == "domains");
        await Assert.That(domains.RouteName).IsEqualTo(RouteNames.GetInstanceAdminDomains);
        await Assert.That(domains.PermissionResourceKind).IsEqualTo(ResourceKinds.InstanceSetting);
        await Assert.That(domains.PermissionAction).IsEqualTo(AuthorizationActions.InstanceSettings.View);
        await Assert.That(domains.PermissionResourceId).IsEqualTo(GetInstanceDomainsQuery.SettingKey);

        var operations = links.Single(link => link.Rel == "operations");
        await Assert.That(operations.RouteName).IsEqualTo(RouteNames.GetInstanceAdminOperations);
        await Assert.That(operations.PermissionResourceKind).IsEqualTo(ResourceKinds.InstanceSetting);
        await Assert.That(operations.PermissionAction).IsEqualTo(AuthorizationActions.InstanceSettings.View);
        await Assert.That(operations.PermissionResourceId).IsEqualTo(GetInstanceOperationsQuery.SettingKey);

        var plans = links.Single(link => link.Rel == "plans");
        await Assert.That(plans.RouteName).IsEqualTo(RouteNames.GetInstanceAdminTenantPlans);
        await Assert.That(plans.Method).IsEqualTo("GET");
        await Assert.That(plans.RequiresAuth).IsTrue();
        await Assert.That(plans.PermissionResourceKind).IsEqualTo(ResourceKinds.InstanceSetting);
        await Assert.That(plans.PermissionAction).IsEqualTo(AuthorizationActions.InstanceSettings.View);
        await Assert.That(plans.PermissionResourceId).IsEqualTo(GetInstanceTenantPlanListQuery.SettingKey);
        await Assert.That(plans.PermissionFacts).IsEqualTo(InstanceScopedAuthorizationFacts.Instance);

        var tenants = links.Single(link => link.Rel == "tenants");
        await Assert.That(tenants.RouteName).IsEqualTo(RouteNames.GetInstanceAdminTenants);
        await Assert.That(tenants.Method).IsEqualTo("GET");
        await Assert.That(tenants.RequiresAuth).IsTrue();
        await Assert.That(tenants.PermissionResourceKind).IsEqualTo(ResourceKinds.InstanceSetting);
        await Assert.That(tenants.PermissionAction).IsEqualTo(AuthorizationActions.InstanceSettings.View);
        await Assert.That(tenants.PermissionResourceId).IsEqualTo(GetInstanceTenantListQuery.SettingKey);
        await Assert.That(tenants.PermissionFacts).IsEqualTo(InstanceScopedAuthorizationFacts.Instance);

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
