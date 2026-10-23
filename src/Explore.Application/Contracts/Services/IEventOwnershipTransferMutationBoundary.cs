using Explore.Application.Responses;

namespace Explore.Application.Contracts.Services;

public interface IEventOwnershipTransferMutationBoundary
{
    Task<BaseCommandResponse<Guid>> ExecuteAsync(Guid tenantId, Guid eventId, Guid currentOwnerAssignmentId,
        Guid newOwnerUserId, Guid auditId, Func<Guid, CancellationToken, Task<BaseCommandResponse<Guid>>> operation,
        CancellationToken cancellationToken);
}
