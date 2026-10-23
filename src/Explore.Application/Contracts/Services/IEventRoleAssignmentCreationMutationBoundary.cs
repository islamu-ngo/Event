using Explore.Application.Responses;
using Explore.Domain.Enums;

namespace Explore.Application.Contracts.Services;

/// <summary>Commits a new assignment, its audit and qualification invalidation under current native authority.</summary>
public interface IEventRoleAssignmentCreationMutationBoundary
{
    Task<BaseCommandResponse<Guid>> ExecuteAsync(Guid tenantId, Guid eventId, Guid assignmentId, Guid targetUserId,
        int roleId, EventRoleAssignmentStatus status, DateTime startsAtUtc, DateTime? expiresAtUtc, Guid auditId,
        string? expectedEmail, Func<Guid, CancellationToken, Task<BaseCommandResponse<Guid>>> operation,
        CancellationToken cancellationToken);
}
