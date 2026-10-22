using Explore.Application.DTOs.Management;
using Explore.Application.Features.Management.Requests.Commands;
using Explore.Application.Responses;

namespace Explore.Application.Contracts.Persistence;

/// <summary>Selects the original instance data owner before scheduling dependencies are resolved.</summary>
public interface IManagedTenantProvisioningSchedulingPort
{
    Task<BaseCommandResponse<ManagementTenantProvisioningOperationDto>> ScheduleAsync(
        ScheduleManagedTenantProvisioningCommand request, CancellationToken cancellationToken = default);
}
