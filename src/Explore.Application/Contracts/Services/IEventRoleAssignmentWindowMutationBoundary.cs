using Explore.Application.Responses;

namespace Explore.Application.Contracts.Services;

public interface IEventRoleAssignmentWindowMutationBoundary
{
    Task<BaseCommandResponse<Guid>> ExecuteAsync(Guid tenantId, Guid eventId, Guid assignmentId,
        DateTime startsAtUtc, DateTime? expiresAtUtc, Guid auditId,
        Func<Guid, CancellationToken, Task<BaseCommandResponse<Guid>>> operation, CancellationToken cancellationToken);
}
