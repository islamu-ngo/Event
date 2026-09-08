using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IEventRoleAssignmentRepository : IGenericRepository<EventRoleAssignment, Guid>
{
    Task<EventRoleAssignment?> GetByEventUserRoleAsync(
        Guid tenantId,
        Guid eventId,
        Guid userId,
        int roleId,
        CancellationToken cancellationToken);

    Task<EventRoleAssignment?> GetOpenByEventUserRoleAsync(
        Guid tenantId,
        Guid eventId,
        Guid userId,
        int roleId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<EventRoleAssignment>> GetEffectiveForUserAndEventsAsync(
        Guid tenantId,
        Guid userId,
        IReadOnlyCollection<Guid> eventIds,
        DateTime utcNow,
        CancellationToken cancellationToken);

    Task<bool> HasAnotherEffectiveOwnerAsync(
        Guid tenantId,
        Guid eventId,
        Guid excludedAssignmentId,
        DateTime utcNow,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<EventRoleAssignment>> GetTeamMembersForEventAsync(
        Guid tenantId,
        Guid eventId,
        bool includeInactive,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<EventRoleAssignment>> GetEffectiveOwnersForEventAsync(
        Guid tenantId,
        Guid eventId,
        DateTime utcNow,
        CancellationToken cancellationToken);
}
