using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;

namespace Event.Domain.UnitTests.ValueObjects;

public sealed class TenantLifecycleAccessPolicyTests
{
    [Test]
    [Arguments(TenantStatusEnum.Provisioning)]
    [Arguments(TenantStatusEnum.Suspended)]
    [Arguments(TenantStatusEnum.Archived)]
    [Arguments(TenantStatusEnum.Purged)]
    public async Task NonActiveTenantNeverHasPublicAccess(TenantStatusEnum status) =>
        await Assert.That(TenantLifecycleAccessPolicy.AllowsPublic((int)status)).IsFalse();

    [Test]
    public async Task ActiveTenantHasPublicAccess() =>
        await Assert.That(TenantLifecycleAccessPolicy.AllowsPublic((int)TenantStatusEnum.Active)).IsTrue();

    [Test]
    [Arguments(null)]
    [Arguments(0)]
    [Arguments(99)]
    public async Task UnknownLifecycleFailsClosed(int? status) =>
        await Assert.That(TenantLifecycleAccessPolicy.AllowsPublic(status)).IsFalse();

    [Test]
    public async Task ProvisioningAllowsOnlyExactManagementAuthority()
    {
        Guid tenantId = Guid.CreateVersion7();
        await Assert.That(TenantLifecycleAccessPolicy.AllowsManagement(tenantId, 1, tenantId, false)).IsTrue();
        await Assert.That(TenantLifecycleAccessPolicy.AllowsManagement(tenantId, 1, null, true)).IsTrue();
        await Assert.That(TenantLifecycleAccessPolicy.AllowsManagement(tenantId, 1, Guid.CreateVersion7(), false)).IsFalse();
        await Assert.That(TenantLifecycleAccessPolicy.AllowsManagement(tenantId, 1, null, false)).IsFalse();
    }

    [Test]
    public async Task ManagementCannotAuthorizeUnknownOrAbsentTenant()
    {
        Guid tenantId = Guid.CreateVersion7();
        await Assert.That(TenantLifecycleAccessPolicy.AllowsManagement(null, 1, null, true)).IsFalse();
        await Assert.That(TenantLifecycleAccessPolicy.AllowsManagement(Guid.Empty, 1, Guid.Empty, true)).IsFalse();
        await Assert.That(TenantLifecycleAccessPolicy.AllowsManagement(tenantId, null, tenantId, true)).IsFalse();
        await Assert.That(TenantLifecycleAccessPolicy.AllowsManagement(tenantId, 99, tenantId, true)).IsFalse();
        await Assert.That(TenantLifecycleAccessPolicy.AllowsManagement(tenantId, (int)TenantStatusEnum.Purged, tenantId, true)).IsFalse();
    }
}
