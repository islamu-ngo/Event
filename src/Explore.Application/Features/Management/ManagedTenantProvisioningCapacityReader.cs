using Explore.Application.DTOs.Management;
using Explore.Domain.Enums;

namespace Explore.Application.Features.Management;

public sealed class ManagedTenantProvisioningCapacityReader(TenantActivationCapacityPolicy capacityPolicy)
{
    public async Task<ManagementTenantProvisioningCapacityDto> ReadAsync(
        CancellationToken cancellationToken,
        DeploymentMode? knownPersistedMode = null)
    {
        TenantActivationCapacityAssessment assessment = await capacityPolicy.EvaluateAsync(
            requireMultiTenant: true,
            knownPersistedMode: knownPersistedMode,
            cancellationToken: cancellationToken);

        return new ManagementTenantProvisioningCapacityDto(
            assessment.Maximum,
            assessment.Active,
            assessment.Reserved,
            assessment.Available,
            assessment.Allowed,
            assessment.FailureCode);
    }
}
