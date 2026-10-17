using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

/// <summary>Reads active memberships for one resolved user on an independent Global lease.</summary>
public interface IUserTenantMembershipRepository
{
    Task<List<TenantUser>> GetActiveTenantsForUserAsync(
        Guid userId, CancellationToken cancellationToken = default);
}
