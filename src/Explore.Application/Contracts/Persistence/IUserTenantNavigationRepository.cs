using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

/// <summary>Reads only the live user's identity and saved tenant preference, without PII.</summary>
public interface IUserTenantNavigationRepository
{
    Task<User?> GetNavigationUserAsync(Guid userId, CancellationToken cancellationToken);
}
