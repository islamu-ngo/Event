using System.Text.Json;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;

namespace Explore.Application.Features.EventRoleAssignments.Handlers.Commands;

public abstract class EventRoleAssignmentCommandHandlerBase
{
    protected static BaseCommandResponse<Guid> Failure(string message, string failureCode, Guid? id = null) =>
        BaseCommandResponse.Failure(
            failureCode,
            message,
            [message],
            id ?? Guid.Empty);

    protected static BaseCommandResponse<Guid> Success(Guid id, string message) =>
        BaseCommandResponse.Success(id, message);

    protected static async Task<Event?> GetEventInTenantAsync(
        IEventRepository eventRepository,
        Guid tenantId,
        Guid eventId)
    {
        var @event = await eventRepository.GetById(eventId);
        return @event is not null && @event.TenantId == tenantId ? @event : null;
    }

    protected static async Task<bool> UserExistsAsync(IUserRepository userRepository, Guid userId)
    {
        return await userRepository.Exists(userId);
    }

    protected static async Task<bool> HasOwnershipTransferAuthorityAsync(
        IEventAuthoritySnapshotService snapshotService,
        Guid tenantId,
        Guid eventId,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        var snapshot = await snapshotService.GetForUserAndEventsAsync(
            tenantId, actorUserId, new[] { eventId }, cancellationToken);

        return snapshot.Events.TryGetValue(eventId, out var authority) &&
               authority.PermissionCodes.Contains(PermissionCodes.EventTransferOwnership);
    }

    protected static BaseCommandResponse<Guid> AuthorityFailure(EventRoleAssignmentAuthorityResult result)
    {
        return Failure(
            result.ErrorMessage ?? "You do not have authority to assign this event role.",
            result.FailureCode ?? EventRoleAuthorityFailureCodes.AuthorityMissing);
    }

    protected static string RoleCodeFor(int roleId)
    {
        return Enum.IsDefined(typeof(RoleEnum), roleId)
            ? ((RoleEnum)roleId).ToString()
            : "unknown";
    }

    protected static async Task WriteAuditAsync(
        IAuditLogRepository auditLogRepository,
        Guid tenantId,
        Guid entityId,
        string action,
        Guid actorUserId,
        object? oldValues,
        object? newValues,
        IReadOnlyCollection<string>? affectedColumns = null)
    {
        var audit = new AuditLog
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Tenant = null!,
            EntityType = nameof(EventRoleAssignment),
            EntityId = entityId.ToString(),
            Action = action,
            OldValues = oldValues is null ? null : JsonSerializer.Serialize(oldValues),
            NewValues = newValues is null ? null : JsonSerializer.Serialize(newValues),
            AffectedColumns = affectedColumns is null ? null : JsonSerializer.Serialize(affectedColumns),
            ActorId = actorUserId,
            Timestamp = DateTime.UtcNow
        };

        await auditLogRepository.Create(audit);
    }
}
