// Called by PolicySyncService after permission changes to ensure fresh authorization decisions.

namespace Explore.Application.Contracts.Identity;

/// <summary>
/// Invalidates cached admin authority profiles. When admin memberships change
/// (e.g., user promoted to tenant admin, removed from org), call these methods
/// to force the next authorization check to re-query the database.
/// </summary>
public interface IAdminCacheInvalidator
{
    /// <summary>
    /// Invalidates all cached authority data for a specific user.
    /// Call when the user's admin memberships change (instance, tenant, or org level).
    /// </summary>
    void InvalidateUser(Guid userId);

    /// <summary>
    /// Invalidates all cached authority data for all users.
    /// Call after bulk permission changes (e.g., full policy sync).
    /// </summary>
    void InvalidateAll();
}
