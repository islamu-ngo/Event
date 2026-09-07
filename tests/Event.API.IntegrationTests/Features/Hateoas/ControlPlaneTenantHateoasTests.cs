using Explore.API.Hateoas;
using Explore.API.Hateoas.Policies;
using Explore.Application.Authorization;
using Explore.Application.DTOs.ControlPlane;
using Explore.Application.Features.ControlPlane.Requests.Commands;
using Explore.Application.Features.ControlPlane.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Domain.Enums;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features.Hateoas;

public sealed class ControlPlaneTenantHateoasTests
{
    [Test]
    public async Task DetailLinks_ForActiveTenant_ExposeSuspendAndArchiveActions()
    {
        var tenantId = Guid.NewGuid();
        var policy = new ControlPlaneTenantDetailLinkPolicy();

        var links = policy.GetLinks(CreateDetail(tenantId, TenantStatusEnum.Active), user: null).ToArray();

        var self = links.Single(link => link.Rel == LinkRelations.Self);
        await Assert.That(self.RouteName).IsEqualTo(RouteNames.GetControlPlaneTenantById);
        await Assert.That(self.PermissionResourceKind).IsEqualTo(ResourceKinds.InstanceSetting);
        await Assert.That(self.PermissionAction).IsEqualTo(AuthorizationActions.InstanceSettings.View);
        await Assert.That(self.PermissionResourceId).IsEqualTo(GetControlPlaneTenantListQuery.SettingKey);

        var configuration = links.Single(link => link.Rel == "configuration");
        await Assert.That(configuration.RouteName).IsEqualTo(RouteNames.GetControlPlaneTenantEffectiveConfiguration);
        await Assert.That(configuration.Method).IsEqualTo("GET");
        await Assert.That(configuration.PermissionAction).IsEqualTo(AuthorizationActions.InstanceSettings.View);
        await Assert.That(configuration.PermissionResourceId).IsEqualTo(GetControlPlaneTenantEffectiveConfigurationQuery.SettingKey);
        await Assert.That(configuration.PermissionFacts).IsEqualTo(InstanceScopedAuthorizationFacts.Instance);

        var suspend = links.Single(link => link.Rel == "suspend");
        await Assert.That(suspend.RouteName).IsEqualTo(RouteNames.SuspendControlPlaneTenant);
        await Assert.That(suspend.Method).IsEqualTo("POST");
        await Assert.That(suspend.PermissionResourceKind).IsEqualTo(ResourceKinds.InstanceSetting);
        await Assert.That(suspend.PermissionAction).IsEqualTo(AuthorizationActions.InstanceSettings.Update);
        await Assert.That(suspend.PermissionResourceId).IsEqualTo(TransitionControlPlaneTenantLifecycleCommand.SettingKey);
        await Assert.That(suspend.PermissionFacts).IsEqualTo(InstanceScopedAuthorizationFacts.Instance);

        var archive = links.Single(link => link.Rel == LinkRelations.Archive);
        await Assert.That(archive.RouteName).IsEqualTo(RouteNames.ArchiveControlPlaneTenant);
        await Assert.That(archive.PermissionResourceId).IsEqualTo(TransitionControlPlaneTenantLifecycleCommand.SettingKey);

        await Assert.That(links.Any(link => link.Rel == "activate")).IsFalse();
        await Assert.That(links.Any(link => link.Rel == "reactivate")).IsFalse();
    }

    [Test]
    public async Task DetailLinks_ForSuspendedTenant_ExposeReactivateAndArchiveActions()
    {
        var tenantId = Guid.NewGuid();
        var policy = new ControlPlaneTenantDetailLinkPolicy();

        var links = policy.GetLinks(CreateDetail(tenantId, TenantStatusEnum.Suspended), user: null).ToArray();

        var reactivate = links.Single(link => link.Rel == "reactivate");
        await Assert.That(reactivate.RouteName).IsEqualTo(RouteNames.ReactivateControlPlaneTenant);
        await Assert.That(reactivate.Method).IsEqualTo("POST");
        await Assert.That(reactivate.PermissionResourceKind).IsEqualTo(ResourceKinds.InstanceSetting);
        await Assert.That(reactivate.PermissionAction).IsEqualTo(AuthorizationActions.InstanceSettings.Update);

        var archive = links.Single(link => link.Rel == LinkRelations.Archive);
        await Assert.That(archive.RouteName).IsEqualTo(RouteNames.ArchiveControlPlaneTenant);

        await Assert.That(links.Any(link => link.Rel == "activate")).IsFalse();
        await Assert.That(links.Any(link => link.Rel == "suspend")).IsFalse();
    }

    [Test]
    public async Task DetailLinks_ForArchivedTenant_ExposeReactivateAndSchedulePurgeActions()
    {
        var tenantId = Guid.NewGuid();
        var policy = new ControlPlaneTenantDetailLinkPolicy();

        var links = policy.GetLinks(CreateDetail(tenantId, TenantStatusEnum.Archived), user: null).ToArray();

        var schedulePurge = links.Single(link => link.Rel == "schedule-purge");
        await Assert.That(schedulePurge.RouteName).IsEqualTo(RouteNames.ScheduleControlPlaneTenantPurge);
        await Assert.That(schedulePurge.Method).IsEqualTo("POST");
        await Assert.That(schedulePurge.PermissionResourceKind).IsEqualTo(ResourceKinds.InstanceSetting);
        await Assert.That(schedulePurge.PermissionAction).IsEqualTo(AuthorizationActions.InstanceSettings.Update);
        await Assert.That(schedulePurge.PermissionResourceId).IsEqualTo(TransitionControlPlaneTenantLifecycleCommand.SettingKey);
        await Assert.That(schedulePurge.PermissionFacts).IsEqualTo(InstanceScopedAuthorizationFacts.Instance);

        var reactivate = links.Single(link => link.Rel == "reactivate");
        await Assert.That(reactivate.RouteName).IsEqualTo(RouteNames.ReactivateControlPlaneTenant);

        await Assert.That(links.Any(link => link.Rel == LinkRelations.Archive)).IsFalse();
        await Assert.That(links.Any(link => link.Rel == "suspend")).IsFalse();
    }

    [Test]
    public async Task CollectionLinks_ExposeTenantCreateAuthorizationMetadata()
    {
        var tenantId = Guid.NewGuid();
        var policy = new ControlPlaneTenantCollectionLinkPolicy();

        var itemLinks = policy.GetItemLinks(CreateListItem(tenantId), user: null).ToArray();
        var collectionLinks = policy.GetCollectionLinks(user: null).ToArray();

        var self = itemLinks.Single(link => link.Rel == LinkRelations.Self);
        await Assert.That(self.RouteName).IsEqualTo(RouteNames.GetControlPlaneTenantById);
        await Assert.That(self.PermissionResourceKind).IsEqualTo(ResourceKinds.InstanceSetting);
        await Assert.That(self.PermissionAction).IsEqualTo(AuthorizationActions.InstanceSettings.View);
        await Assert.That(self.PermissionFacts).IsEqualTo(InstanceScopedAuthorizationFacts.Instance);

        var configuration = itemLinks.Single(link => link.Rel == "configuration");
        await Assert.That(configuration.RouteName).IsEqualTo(RouteNames.GetControlPlaneTenantEffectiveConfiguration);
        await Assert.That(configuration.PermissionAction).IsEqualTo(AuthorizationActions.InstanceSettings.View);
        await Assert.That(configuration.PermissionResourceId).IsEqualTo(GetControlPlaneTenantEffectiveConfigurationQuery.SettingKey);
        await Assert.That(configuration.PermissionFacts).IsEqualTo(InstanceScopedAuthorizationFacts.Instance);

        var create = collectionLinks.Single(link => link.Rel == LinkRelations.Create);
        await Assert.That(create.RouteName).IsEqualTo(RouteNames.CreateControlPlaneTenant);
        await Assert.That(create.Method).IsEqualTo("POST");
        await Assert.That(create.RequiresAuth).IsTrue();
        await Assert.That(create.PermissionResourceKind).IsEqualTo(ResourceKinds.Tenant);
        await Assert.That(create.PermissionAction).IsEqualTo(AuthorizationActions.Create);
    }

    [Test]
    [Arguments(TenantStatusEnum.Provisioning, "activate", RouteNames.ActivateControlPlaneTenant)]
    [Arguments(TenantStatusEnum.Active, "suspend", RouteNames.SuspendControlPlaneTenant)]
    [Arguments(TenantStatusEnum.Active, "archive", RouteNames.ArchiveControlPlaneTenant)]
    [Arguments(TenantStatusEnum.Suspended, "reactivate", RouteNames.ReactivateControlPlaneTenant)]
    [Arguments(TenantStatusEnum.Archived, "schedule-purge", RouteNames.ScheduleControlPlaneTenantPurge)]
    public async Task CollectionItemLinks_ExposeStateValidLifecycleAuthorizationMetadata(
        TenantStatusEnum status,
        string relation,
        string routeName)
    {
        var tenantId = Guid.NewGuid();
        var policy = new ControlPlaneTenantCollectionLinkPolicy();

        var links = policy.GetItemLinks(CreateListItem(tenantId, status), user: null).ToArray();
        var lifecycle = links.Single(link => link.Rel == relation);

        await Assert.That(lifecycle.RouteName).IsEqualTo(routeName);
        await Assert.That(lifecycle.Method).IsEqualTo("POST");
        await Assert.That(lifecycle.PermissionResourceKind).IsEqualTo(ResourceKinds.InstanceSetting);
        await Assert.That(lifecycle.PermissionAction).IsEqualTo(AuthorizationActions.InstanceSettings.Update);
        await Assert.That(lifecycle.PermissionResourceId).IsEqualTo(TransitionControlPlaneTenantLifecycleCommand.SettingKey);
        // Control-plane lifecycle transitions are decided by instance authority alone; the target tenant and
        // status identify the row being acted on and are carried by the route, not by policy facts.
        await Assert.That(lifecycle.PermissionFacts).IsEqualTo(InstanceScopedAuthorizationFacts.Instance);
    }

    private static ControlPlaneTenantDetailDto CreateDetail(Guid tenantId, TenantStatusEnum status) => new()
    {
        Id = tenantId,
        FullName = "Demo Tenant",
        Slug = "demo",
        StatusId = (int)status,
        StatusCode = status.ToString().ToUpperInvariant(),
        StatusName = status.ToString(),
        IsActive = status == TenantStatusEnum.Active,
        CreatedAt = DateTime.UtcNow,
        LifecycleHistory = []
    };

    private static ControlPlaneTenantListItemDto CreateListItem(
        Guid tenantId,
        TenantStatusEnum status = TenantStatusEnum.Active) => new()
        {
            Id = tenantId,
            FullName = "Demo Tenant",
            Slug = "demo",
            StatusId = (int)status,
            StatusCode = status.ToString().ToUpperInvariant(),
            StatusName = status.ToString(),
            IsActive = status == TenantStatusEnum.Active,
            CreatedAt = DateTime.UtcNow
        };
}
