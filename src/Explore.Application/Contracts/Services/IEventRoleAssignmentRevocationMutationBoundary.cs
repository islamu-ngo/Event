using Explore.Application.Responses;

namespace Explore.Application.Contracts.Services;

public interface IEventRoleAssignmentRevocationMutationBoundary
{
    Task<BaseCommandResponse<Guid>> ExecuteAsync(Guid tenantId, Guid eventId, Guid assignmentId, Guid auditId,
        Func<Guid, CancellationToken, Task<BaseCommandResponse<Guid>>> operation, CancellationToken cancellationToken);
}
