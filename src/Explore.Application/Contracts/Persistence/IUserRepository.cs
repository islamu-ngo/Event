using System;
using System.Collections.Generic;
using System.Text;
using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IUserRepository : IGenericRepository<User, Guid>
{
    /// <summary>Reads only the filtered identity enrollment subject's ID and deletion state, without tracking or profile data.</summary>
    Task<User?> GetForIdentityEnrollmentAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Reads the filtered User and personal Actor binding without profile or storage navigation data.</summary>
    Task<User?> GetLocalIdentityBindingAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Reads the filtered, tracked account without loading its private profile for synchronization.</summary>
    Task<User?> GetIdentityAccountForSynchronizationAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Saves the tracked account and only its email mirror within the original account transaction.
    /// Null email or verification observations preserve the corresponding stored value.
    /// </summary>
    Task SaveIdentityProfileAsync(
        User user, string? normalizedEmail, bool? emailVerified, CancellationToken cancellationToken);

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
