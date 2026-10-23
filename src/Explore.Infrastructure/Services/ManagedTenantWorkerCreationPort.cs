using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.ManagedProviderProvisioning;
using Explore.Application.Exceptions;
using Explore.Application.Responses;

namespace Explore.Infrastructure.Services;

/// <summary>
/// Keeps the original admitted Runtime delivery alive until its independent
/// creator has physically settled and disposed. Human authority is never resolved.
/// </summary>
public sealed class ManagedTenantWorkerCreationPort(
    IManagedTenantWorkerCreationAdmission admission, IManagedTenantWorkerCreationOwnerFactory creators)
    : IManagedTenantWorkerCreationPort
{
    public async Task<BaseCommandResponse<ManagedProviderClientProvisioningResultDto>> CreateAsync(
        Guid operationId, Guid outboxMessageId, CancellationToken cancellationToken = default)
    {
        bool settled = false;
        try
        {
            await using var invocation = await admission.AcquireAsync(operationId, outboxMessageId, cancellationToken);
            await using var creator = await creators.CreateAsync(invocation, cancellationToken);
            var result = await creator.CreateAsync(cancellationToken);
            settled = true;
            return result;
        }
        catch (Exception exception) when (settled && exception is not HumanMutationOutcomeUnknownException)
        {
            throw new HumanMutationOutcomeUnknownException(exception);
        }
    }
}
