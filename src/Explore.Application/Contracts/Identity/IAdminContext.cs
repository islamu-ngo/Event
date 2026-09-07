// DB-first authority model: identity from claims, authority from role assignments and memberships.

namespace Explore.Application.Contracts.Identity;

/// <summary>
/// Resolves the current user's administrative authority across the Instance > Tenant > Organization > Group hierarchy.
/// Uses a DB-first authority model where identity comes from authenticated claims and
/// authorization comes from database relationships.
/// Implementations should cache the "Authority Profile" for performance (5-minute sliding window).
/// </summary>
public interface IAdminContext
{
    /// <summary>
    /// Gets the current authenticated user's ID.
    /// </summary>
    Guid? UserId { get; }

    /// <summary>
    /// Resolves the current user's internal ID.
    /// If the token has an internal_user_id claim, it returns that.
    /// Otherwise, it performs a database lookup via external login or email.
    /// Result is cached for the duration of the request.
    /// </summary>
    Task<Guid?> ResolveUserIdAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets whether the current user is an Instance Administrator.
    /// Resolved from platform-scoped role assignments.
    /// </summary>
    Task<bool> IsInstanceAdminAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets whether the specified user is an Instance Administrator.
    /// Use this overload when the caller already knows the userId (e.g. during claims transformation
    /// where HttpContext.User is not yet set to the authenticated principal).
    /// </summary>
    Task<bool> IsInstanceAdminAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets whether the current user is a Tenant Administrator for the specified tenant.
    /// Resolved strictly from tenant membership assignments.
    /// </summary>
    Task<bool> IsTenantAdminAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets whether the current user is an Organization Administrator (Creator, CoOwner, or Admin role).
    /// Resolved strictly from the OrganizationMembers database table.
    /// </summary>
    Task<bool> IsOrganizationAdminAsync(Guid organizationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all tenant IDs where the current user has administrative rights.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetAdminTenantIdsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all tenant IDs where the specified user has administrative rights.
    /// Use this overload when the caller already knows the userId.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetAdminTenantIdsAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all organization IDs where the current user has administrative rights.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetAdminOrganizationIdsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all organization IDs where the specified user has administrative rights.
    /// Use this overload when the caller already knows the userId.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetAdminOrganizationIdsAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Guid>> GetAdminOrganizationIdsAsync(
        Guid userId,
        Guid tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets whether the current user is a Group Administrator for the specified group.
    /// Resolved strictly from the GroupMembers database table (RoleId == GroupAdmin).
    /// </summary>
    Task<bool> IsGroupAdminAsync(Guid groupId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all group IDs where the current user has administrative rights.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetAdminGroupIdsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all group IDs where the specified user has administrative rights.
    /// Use this overload when the caller already knows the userId.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetAdminGroupIdsAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Guid>> GetAdminGroupIdsAsync(
        Guid userId,
        Guid tenantId,
        CancellationToken cancellationToken = default);
}
