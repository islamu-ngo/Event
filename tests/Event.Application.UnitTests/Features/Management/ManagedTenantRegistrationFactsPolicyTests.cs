using Explore.Application.Features.Management;
using Explore.Application.Management;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Event.Application.UnitTests.Features.Management;

public sealed class ManagedTenantRegistrationFactsPolicyTests
{
    [Test]
    public async Task ARegisteredExactInstanceWithTheCurrentProtocolAndModeMayProceed()
    {
        var facts = Current();
        await Assert.That(ManagedTenantProvisioningRegistrationPolicy.EvaluateFacts(facts,
            facts.ManagedInstanceId, DeploymentMode.MultiTenant)).IsNull();
    }

    [Test]
    [Arguments(ManagedControlPlaneRegistrationStatus.Pending)]
    [Arguments(ManagedControlPlaneRegistrationStatus.Revoked)]
    public async Task AChangedRegistrationStatusRefuses(ManagedControlPlaneRegistrationStatus status)
    {
        var facts = Current() with { Status = status };
        await Assert.That(ManagedTenantProvisioningRegistrationPolicy.EvaluateFacts(facts,
            facts.ManagedInstanceId, DeploymentMode.MultiTenant)!.Code).IsEqualTo("managed_registration_unavailable");
    }

    [Test]
    public async Task AnotherInstanceCannotBorrowARegisteredSource()
    {
        var facts = Current();
        await Assert.That(ManagedTenantProvisioningRegistrationPolicy.EvaluateFacts(facts,
            Guid.CreateVersion7(), DeploymentMode.MultiTenant)!.Code).IsEqualTo("managed_registration_unavailable");
    }

    [Test]
    public async Task MissingRegistrationRefuses()
    {
        await Assert.That(ManagedTenantProvisioningRegistrationPolicy.EvaluateFacts(null,
            Guid.CreateVersion7(), DeploymentMode.MultiTenant)!.Code).IsEqualTo("managed_registration_unavailable");
    }

    [Test]
    public async Task AnIncompatibleProtocolIsNotRelabelledCurrent()
    {
        var facts = Current() with { ManagementApiVersion = "unrecognised" };
        await Assert.That(ManagedTenantProvisioningRegistrationPolicy.EvaluateFacts(facts,
            facts.ManagedInstanceId, DeploymentMode.MultiTenant)!.Code).IsEqualTo("managed_registration_api_incompatible");
    }

    [Test]
    public async Task AChangedDeploymentModeRefuses()
    {
        var facts = Current() with { DeploymentMode = DeploymentMode.SingleTenant };
        await Assert.That(ManagedTenantProvisioningRegistrationPolicy.EvaluateFacts(facts,
            facts.ManagedInstanceId, DeploymentMode.MultiTenant)!.Code).IsEqualTo("managed_registration_mode_incompatible");
    }

    private static ManagedTenantProvisioningRegistrationPolicy.Facts Current() =>
        new(ManagedControlPlaneRegistrationStatus.Registered, Guid.CreateVersion7(),
            ManagedControlPlaneContract.ManagementApiVersion, DeploymentMode.MultiTenant);
}
