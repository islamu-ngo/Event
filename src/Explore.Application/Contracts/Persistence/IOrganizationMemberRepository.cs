using System;
using System.Collections.Generic;
using System.Text;
using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IOrganizationMemberRepository : IGenericRepository<OrganizationMember, Guid>
{
    Task<List<User>> GetUsersByOrganization(Guid organizationId);
    Task<List<Organization>> GetOrganizationsByUser(Guid userId);
    Task<bool> Exists(Guid organizationId, Guid userId);
    Task<OrganizationMember?> GetOrganizationMemberWithDetails(Guid id, CancellationToken cancellationToken = default);
    Task<List<OrganizationMember>> GetOrganizationMembersWithDetails();
    Task<List<OrganizationMember>> GetMembersByOrganizationId(Guid organizationId);
    Task<List<OrganizationMember>> GetInvitesByEmail(string email);

    /// <summary>
    /// Gets the organization member record for a specific user in an organization.
    /// </summary>
    Task<OrganizationMember?> GetByOrganizationAndUser(Guid organizationId, Guid userId);

    /// <summary>
    /// Checks if the user's role in the organization has the specified permission
    /// via the RolePermission join table. Falls back to legacy role-based check
    /// when no permissions are seeded yet (transitional safety).
    /// </summary>
    Task<bool> HasPermissionInOrganization(Guid organizationId, Guid userId, string permissionMasterCode);

    /// <summary>
    /// Returns the IDs of all organizations where the user's role has the specified permission.
    /// Falls back to legacy admin role check when no permissions are seeded yet.
    /// </summary>
    Task<List<Guid>> GetOrganizationIdsWhereUserHasPermission(
        Guid userId,
        string permissionMasterCode,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all organization memberships for a user, including organization details and role.
    /// </summary>
    Task<List<OrganizationMember>> GetMembershipsByUser(
        Guid userId,
        CancellationToken cancellationToken = default);
}
