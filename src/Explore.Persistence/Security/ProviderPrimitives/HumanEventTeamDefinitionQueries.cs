using System.Collections.Immutable;
using Explore.Application.Contracts.Persistence;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Security;

internal static class HumanEventTeamDefinitionQueries
{
    internal static async Task<EventTeamPolicyFacts> ReadAsync(ExploreDbContext context,
        RelationalConnectionIdentity connection, PostgresNativeQueueEnrollment? enrollment,
        Guid principal, Guid tenant, EventTeamSourceTarget target, CancellationToken token)
    {
        if (context.Database.IsNpgsql())
            return await EventTeamNativeDefinitions.ReadAsync(
                context, connection, enrollment!, principal, tenant, target, token);
        var roles = await context.Roles.AsNoTracking().OrderBy(row => row.Id)
            .Select(row => new EventTeamRoleFact(row.Id, row.RoleScopeId, row.MasterCode, row.FullName, row.Description))
            .ToListAsync(token);
        var permissions = await context.Permissions.AsNoTracking().OrderBy(row => row.Id)
            .Select(row => new EventTeamPermissionFact(row.Id, row.MasterCode, row.IsActive, row.IsFiltered))
            .ToListAsync(token);
        var mappings = await context.RolePermissions.AsNoTracking().OrderBy(row => row.RoleId).ThenBy(row => row.PermissionId)
            .Select(row => new EventTeamRolePermissionFact(row.RoleId, row.PermissionId)).ToListAsync(token);
        var assignments = await context.EventRoleAssignments.AsNoTracking()
            .Where(row => row.TenantId == tenant && row.EventId == target.EventId).OrderBy(row => row.Id)
            .Select(row => new EventTeamAssignmentFact(row.Id, row.TenantId, row.EventId, row.UserId, row.RoleId,
                (int)row.Status, row.StartsAtUtc, row.ExpiresAtUtc, row.RevokedAtUtc, row.RevokedByUserId,
                row.Version, row.CreatedBy)).ToListAsync(token);
        return new(roles.ToImmutableArray(), permissions.ToImmutableArray(), mappings.ToImmutableArray(),
            assignments.ToImmutableArray(), null);
    }
}
