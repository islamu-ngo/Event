using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Domain.Constants;
using Explore.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Authorization;

/// <summary>
/// Instance management is exempt from tenant resolution by design. These checks belong to the
/// installation, so a HAL batch must not build or read a tenant authority profile merely because
/// the batch contains more than two links.
/// </summary>
public sealed class InstanceScopedBatchAuthorizationTests
{
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task InstanceScopedBatchDoesNotRequireTenantResolution(bool instanceAdmin)
    {
        var admin = Substitute.For<IAdminContext>();
        admin.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(instanceAdmin);

        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(_ => throw new InvalidOperationException(
            "Instance-scoped authorization must not resolve a tenant."));

        var provider = new FallbackAuthorizationService(
            admin,
            Substitute.For<IMachinePrincipalAccessor>(),
            Substitute.For<IEventAuthoritySnapshotService>(),
            Substitute.For<IOrganizationMemberRepository>(),
            Substitute.For<IGroupMemberRepository>(),
            Substitute.For<IHierarchicalSettingsResolver>(),
            tenant,
            NullLogger<FallbackAuthorizationService>.Instance);

        AuthorizationRequest[] checks =
        [
            new(ResourceKinds.InstanceSetting, "control-plane.authentication",
                AuthorizationActions.InstanceSettings.View, Facts: InstanceScopedAuthorizationFacts.Instance),
            new(ResourceKinds.InstanceSetting, "control-plane.authorization",
                AuthorizationActions.InstanceSettings.View, Facts: InstanceScopedAuthorizationFacts.Instance),
            new(ResourceKinds.InstanceSetting, "control-plane.tenants",
                AuthorizationActions.InstanceSettings.View, Facts: InstanceScopedAuthorizationFacts.Instance),
            new(ResourceKinds.Tenant, "new-tenant", AuthorizationActions.Tenants.Create)
        ];

        var decisions = await provider.AuthorizeBatchAsync(checks);

        await Assert.That(decisions.Count).IsEqualTo(checks.Length);
        await Assert.That(decisions.All(decision => decision.IsAllowed == instanceAdmin)).IsTrue();
    }

    [Test]
    [Arguments(2, false, false)]
    [Arguments(3, false, false)]
    [Arguments(4, false, false)]
    [Arguments(5, false, false)]
    [Arguments(2, true, false)]
    [Arguments(3, true, false)]
    [Arguments(3, false, true)]
    public async Task MixedBatchRequiresTenantForInstanceAdminAuthority(int count, bool tenantCreation, bool selectedTenant)
    {
        var admin = Substitute.For<IAdminContext>();
        admin.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(true);
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(selectedTenant ? Guid.NewGuid() : Guid.Empty);

        var provider = new FallbackAuthorizationService(
            admin,
            Substitute.For<IMachinePrincipalAccessor>(),
            Substitute.For<IEventAuthoritySnapshotService>(),
            Substitute.For<IOrganizationMemberRepository>(),
            Substitute.For<IGroupMemberRepository>(),
            Substitute.For<IHierarchicalSettingsResolver>(),
            tenant,
            NullLogger<FallbackAuthorizationService>.Instance);

        AuthorizationRequest[] checks =
        [
            tenantCreation
                ? new(ResourceKinds.Tenant, "new-tenant", AuthorizationActions.Tenants.Create)
                : new(ResourceKinds.InstanceSetting, "control-plane.authentication",
                    AuthorizationActions.InstanceSettings.View, Facts: InstanceScopedAuthorizationFacts.Instance),
            new(ResourceKinds.User, Guid.NewGuid().ToString(), AuthorizationActions.Users.View),
            tenantCreation
                ? new(ResourceKinds.Tenant, "another-tenant", AuthorizationActions.Tenants.Create)
                : new(ResourceKinds.InstanceSetting, "control-plane.authorization",
                    AuthorizationActions.InstanceSettings.View, Facts: InstanceScopedAuthorizationFacts.Instance),
            new(ResourceKinds.EventResource, Guid.NewGuid().ToString(), AuthorizationActions.View),
            new(ResourceKinds.Event, Guid.NewGuid().ToString(), AuthorizationActions.Events.ReviewDiscoveryIdentity)
        ];

        var decisions = await provider.AuthorizeBatchAsync(checks.Take(count).ToArray());

        await Assert.That(decisions.Count).IsEqualTo(count);
        await Assert.That(decisions.All(decision => decision.IsAllowed == selectedTenant)).IsTrue();
    }
}
