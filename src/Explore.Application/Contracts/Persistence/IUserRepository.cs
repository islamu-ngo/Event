using System;
using System.Collections.Generic;
using System.Text;
using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IUserRepository : IGenericRepository<User, Guid>
{
    Task<User?> GetUserWithDetails(Guid id);
    Task<User?> GetUserWithDetails(Guid id, CancellationToken cancellationToken);
    Task<User?> GetUserByEmail(string email);
    Task<IReadOnlyList<User>> GetUsersByNormalizedEmailAsync(
        string email,
        CancellationToken cancellationToken = default);
    Task<bool> ExistsByEmail(string email);
    Task<List<User>> GetUsersByIdsAsync(List<Guid> ids);

    /// <summary>
    /// Permanently deletes PII data for a user (GDPR erasure).
    /// Uses ExecuteDeleteAsync for efficient bulk deletion without loading entities.
    /// </summary>
    /// <returns>Number of PII records deleted.</returns>
    Task<int> ForgetPiiAsync(Guid userId);
}
