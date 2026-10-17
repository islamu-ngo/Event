using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

/// <summary>Retains original authenticated human custody until publisher membership results are released.</summary>
public interface IHumanEventCreationPublisherReader
{
    Task<(IReadOnlyList<OrganizationMember> Organizations, IReadOnlyList<GroupMember> Groups,
        IReadOnlyList<RolePermission> Permissions)> ReadAsync(
        Guid tenantId, Guid userId, CancellationToken cancellationToken = default);
}
