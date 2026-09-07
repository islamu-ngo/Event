using Explore.Application.DTOs.ManagedProviderProvisioning;
using Explore.Application.DTOs.Management;
using Explore.Application.Responses;

namespace Explore.Application.Features.ManagedProviderProvisioning;

public interface IManagedProviderClientProvisioner
{
    Task<BaseCommandResponse<ManagedProviderClientProvisioningResultDto>> EnsureAsync(
        ManagedProviderClientProvisioningDto provisioningDto,
        ManagementTenantProvisioningRequestDto? managementRequest,
        Guid? operationId,
        Guid? expectedOutboxMessageId,
        CancellationToken cancellationToken);
}
