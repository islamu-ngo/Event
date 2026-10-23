using System.Collections.Immutable;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Security;

internal sealed class HumanAccountMembershipDefinitionQueries
{
    private readonly ExploreDbContext context;
    private readonly RelationalConnectionIdentity connection;
    private readonly HumanCommitSubject? subject;
    private readonly HumanAuthenticationLifetime? lifetime;
    private readonly PostgresNativeQueueEnrollment? enrollment;
    private readonly Guid? tenant;

    internal HumanAccountMembershipDefinitionQueries(ExploreDbContext context, RelationalConnectionIdentity connection,
        Guid? tenant, HumanCommitSubject? subject, HumanAuthenticationLifetime? lifetime,
        PostgresNativeQueueEnrollment? enrollment)
    {
        this.context = context;
        this.connection = connection;
        this.tenant = tenant;
        if (context.Database.IsNpgsql())
        {
            ArgumentNullException.ThrowIfNull(subject);
            ArgumentNullException.ThrowIfNull(lifetime);
            ArgumentNullException.ThrowIfNull(enrollment);
            enrollment.Validate();
            if (subject.AccountKey is null || subject.AccountKey != lifetime.AccountKey)
                throw new UnauthorizedAccessException("Account policy sources require original authenticated custody.");
            this.subject = subject;
            this.lifetime = lifetime;
            this.enrollment = enrollment;
        }
    }

    internal async Task<(ImmutableArray<HumanPrincipalRoleFact>, ImmutableArray<HumanPrincipalPermissionFact>)>
        ReadAsync(CancellationToken token)
    {
        if (context.Database.IsNpgsql())
            return await AccountMembershipNativeDefinitions.ReadAsync(
                context, connection, subject!, lifetime!, enrollment!, tenant, token);
        // The comparison flags execute in the database, preserving its string semantics.
        var roles = await context.Roles.AsNoTracking().OrderBy(role => role.Id)
            .Select(role => new HumanPrincipalRoleFact(role.Id, role.RoleScopeId, role.MasterCode,
                role.MasterCode == "platform.admin")).ToListAsync(token);
        var permissions = await context.Set<RolePermission>().AsNoTracking()
            .OrderBy(mapping => mapping.RoleId).ThenBy(mapping => mapping.PermissionId)
            .Select(mapping => new HumanPrincipalPermissionFact(mapping.RoleId, mapping.PermissionId,
                mapping.Permission.MasterCode, mapping.Permission.IsActive,
                mapping.Permission.MasterCode == PermissionCodes.EventCreate,
                mapping.Permission.MasterCode == PermissionCodes.EventManageFinance)
            {
                MatchesGroupMemberCreateCode = mapping.Permission.MasterCode == PermissionCodes.GroupMemberCreate,
                MatchesGroupMemberUpdateCode = mapping.Permission.MasterCode == PermissionCodes.GroupMemberUpdate,
                MatchesGroupMemberDeleteCode = mapping.Permission.MasterCode == PermissionCodes.GroupMemberDelete
            }).ToListAsync(token);
        return (roles.ToImmutableArray(), permissions.ToImmutableArray());
    }
}
