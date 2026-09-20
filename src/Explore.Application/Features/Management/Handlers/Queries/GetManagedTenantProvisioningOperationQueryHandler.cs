using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Management;
using Explore.Application.Features.Management.Requests.Queries;
using Explore.Domain;

namespace Explore.Application.Features.Management.Handlers.Queries;

public sealed class GetManagedTenantProvisioningOperationQueryHandler(
    IManagedTenantProvisioningOperationRepository operationRepository)
    : IQueryHandler<GetManagedTenantProvisioningOperationQuery,
        ManagementTenantProvisioningOperationDto?>
{
    public async Task<ManagementTenantProvisioningOperationDto?> QueryAsync(
        GetManagedTenantProvisioningOperationQuery request,
        CancellationToken cancellationToken = default)
    {
        ManagedTenantProvisioningOperation? operation =
            await operationRepository.GetByManagedInstanceAndIdAsNoTrackingAsync(
                request.ManagedInstanceId,
                request.OperationId,
                cancellationToken);
        return operation is null ? null : ManagedTenantProvisioningRequestCodec.ToDto(operation);
    }
}
