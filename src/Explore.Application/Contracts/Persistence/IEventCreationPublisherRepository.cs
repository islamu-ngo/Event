using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Detached own memberships and their active event-create permission mappings. Membership
/// inclusion does not require a publishing grant or an actor. The prepared human owns the read.
/// </summary>
public interface IEventCreationPublisherRepository
{
    Task<(IReadOnlyList<OrganizationMember> Organizations, IReadOnlyList<GroupMember> Groups,
        IReadOnlyList<RolePermission> Permissions)> ReadAsync(CancellationToken cancellationToken = default);
}
