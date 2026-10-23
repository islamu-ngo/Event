using Explore.Application.Features.Management;
using Explore.Domain.Enums;

namespace Event.Application.UnitTests.Features.Management;

public sealed class TenantActivationCapacityAssessmentTests
{
    [Test]
    public async Task SingleTenantAllowsOneSlotRegardlessOfConfiguredMultiTenantMaximum()
    {
        var result = TenantActivationCapacityPolicy.Assess(true, false, DeploymentMode.SingleTenant, 50, 0, 0);

        await Assert.That(result).IsEqualTo(new TenantActivationCapacityAssessment(
            true, DeploymentMode.SingleTenant, 1, 0, 0, 1, null, null));
    }

    [Test]
    public async Task MissingPersistedModeDefaultsToSingleTenant()
    {
        var result = TenantActivationCapacityPolicy.Assess(true, false, null, 50, 1, 0);

        await Assert.That(result).IsEqualTo(new TenantActivationCapacityAssessment(
            false, DeploymentMode.SingleTenant, 1, 1, 0, 0,
            "tenant_provisioning_capacity_exhausted", "This Event instance has no available tenant capacity."));
    }

    [Test]
    public async Task MultiTenantSubtractsBothActiveTenantsAndPendingReservations()
    {
        var result = TenantActivationCapacityPolicy.Assess(true, false, DeploymentMode.MultiTenant, 8, 3, 2);

        await Assert.That(result).IsEqualTo(new TenantActivationCapacityAssessment(
            true, DeploymentMode.MultiTenant, 8, 3, 2, 3, null, null));
    }

    [Test]
    [Arguments(3, 2)]
    [Arguments(5, 0)]
    [Arguments(4, 3)]
    public async Task ReservationsExhaustCapacityAndAvailabilityNeverBecomesNegative(int active, int reserved)
    {
        var result = TenantActivationCapacityPolicy.Assess(true, false, DeploymentMode.MultiTenant, 5, active, reserved);

        await Assert.That(result).IsEqualTo(new TenantActivationCapacityAssessment(
            false, DeploymentMode.MultiTenant, 5, active, reserved, 0,
            "tenant_provisioning_capacity_exhausted", "This Event instance has no available tenant capacity."));
    }

    [Test]
    public async Task MultiTenantWithNoConfiguredCapacityRefusesBeforeCountingSources()
    {
        var result = TenantActivationCapacityPolicy.Assess(true, false, DeploymentMode.MultiTenant, 0, 3, 2);

        await Assert.That(result).IsEqualTo(new TenantActivationCapacityAssessment(
            false, DeploymentMode.MultiTenant, 0, 0, 0, 0,
            "tenant_provisioning_capacity_not_configured", "Managed tenant provisioning capacity is not configured."));
    }

    [Test]
    [Arguments(DeploymentMode.SingleTenant)]
    [Arguments(null)]
    public async Task RequiredMultiTenantModeRefusesEvenWhenSingleTenantHasSpace(DeploymentMode? mode)
    {
        var result = TenantActivationCapacityPolicy.Assess(true, true, mode, 0, 0, 0);

        await Assert.That(result).IsEqualTo(new TenantActivationCapacityAssessment(
            false, DeploymentMode.SingleTenant, 1, 0, 0, 0,
            "tenant_provisioning_requires_multi_tenant", "Managed tenant provisioning is unavailable in SingleTenant mode."));
    }

    [Test]
    public async Task RequiredMultiTenantModeAllowsConfiguredAvailableCapacity()
    {
        var result = TenantActivationCapacityPolicy.Assess(true, true, DeploymentMode.MultiTenant, 4, 1, 1);

        await Assert.That(result).IsEqualTo(new TenantActivationCapacityAssessment(
            true, DeploymentMode.MultiTenant, 4, 1, 1, 2, null, null));
    }

    [Test]
    public async Task UnmanagedInstancesDoNotApplyManagedModeOrCapacityRestrictions()
    {
        var result = TenantActivationCapacityPolicy.Assess(false, true, DeploymentMode.MultiTenant, 0, 20, 10);

        await Assert.That(result).IsEqualTo(new TenantActivationCapacityAssessment(
            true, DeploymentMode.SingleTenant, int.MaxValue, 0, 0, int.MaxValue, null, null));
    }
}
