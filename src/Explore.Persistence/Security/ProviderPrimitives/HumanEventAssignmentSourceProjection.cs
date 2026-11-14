using System.Collections.Immutable;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Security;

/// <summary>Detached assignment facts bounded by the original tenant, actor and event selection.</summary>
internal static class HumanEventAssignmentSourceProjection
{
    internal static async Task<HumanEventAssignmentSourceFacts> ReadAsync(
        ExploreDbContext context, Guid tenant, Guid actor, ImmutableArray<Guid> eventIds,
        Action requireCurrent, CancellationToken cancellationToken)
    {
        async Task<ImmutableArray<T>> Rows<T>(IQueryable<T> query)
        {
            requireCurrent();
            var rows = await query.ToArrayAsync(cancellationToken);
            requireCurrent();
            return rows.ToImmutableArray();
        }
        requireCurrent();
        Guid[] selectedEvents = eventIds.ToArray();
        var events = Source<Explore.Domain.Event>(context).Where(row => row.TenantId == tenant && selectedEvents.Contains(row.Id));
        var assignments = Source<EventRoleAssignment>(context)
            .Where(row => row.TenantId == tenant && row.UserId == actor && selectedEvents.Contains(row.EventId));
        var organizations = Source<OrganizationMember>(context).Where(row => row.TenantId == tenant && row.UserId == actor);
        var groups = Source<GroupMember>(context).Where(row => row.TenantId == tenant && row.UserId == actor);
        var roles = Source<Role>(context).Where(role => assignments.Any(row => row.RoleId == role.Id)
            || organizations.Any(row => row.RoleId == role.Id) || groups.Any(row => row.RoleId == role.Id));

        var eventFacts = await Rows(events.Select(row => new HumanAssignmentEventFact(row.Id, row.TenantId,
            row.ActorId, row.OrganizerActorId, row.EventProvenanceTypeId, row.SubmittedByUserId,
            row.IsDeleted, row.ConcurrencyStamp)));
        var assignmentFacts = await Rows(assignments.Select(row => new HumanEventAssignmentFact(row.Id,
            row.TenantId, row.EventId, row.UserId, row.RoleId, (int)row.Status, row.StartsAtUtc,
            row.ExpiresAtUtc, row.RevokedAtUtc, row.Version)));
        var roleFacts = await Rows(roles.Select(row => new HumanAssignmentRoleFact(row.Id, row.MasterCode)));
        var permissions = await Rows(from mapping in Source<RolePermission>(context)
                                     join permission in Source<Permission>(context) on mapping.PermissionId equals permission.Id
                                     where assignments.Any(row => row.RoleId == mapping.RoleId)
                                         || organizations.Any(row => row.RoleId == mapping.RoleId) || groups.Any(row => row.RoleId == mapping.RoleId)
                                     select new HumanAssignmentPermissionFact(mapping.RoleId, mapping.PermissionId,
                                         permission.MasterCode, permission.IsActive));
        var actors = await Rows(Source<Actor>(context)
            .Where(row => events.Any(parent => parent.ActorId == row.Id || parent.OrganizerActorId == row.Id))
            .Select(row => new HumanAssignmentActorFact(row.Id, row.UserId, row.OrganizationId,
                row.GroupId, row.IsDeleted, row.IsSuspended)));
        var members = await Rows(Source<TenantUser>(context).Where(row => row.TenantId == tenant && row.UserId == actor)
            .Select(row => new HumanAssignmentTenantMemberFact(row.Id, row.ActorId, row.StatusId, row.IsDeleted)));
        var organizationFacts = await Rows(from member in organizations
                                           join placement in Source<OrganizationTenant>(context)
                                               on new { Id = member.OrganizationTenantId, member.TenantId } equals new { placement.Id, placement.TenantId }
                                           select new HumanAssignmentOwnerMemberFact(member.Id, placement.Id, placement.OrganizationId,
                                               member.RoleId, member.IsDeleted, placement.IsDeleted, placement.IsSuspended,
                                               placement.IsOrganizerEligible, placement.ApprovalStatusId));
        var groupFacts = await Rows(from member in groups
                                    join placement in Source<GroupTenant>(context)
                                        on new { Id = member.GroupTenantId, member.TenantId } equals new { placement.Id, placement.TenantId }
                                    select new HumanAssignmentOwnerMemberFact(member.Id, placement.Id, placement.GroupId,
                                        member.RoleId, member.IsDeleted, placement.IsDeleted, placement.IsSuspended,
                                        placement.IsOrganizerEligible, placement.ApprovalStatusId));
        var provenance = await Rows(Source<EventProvenanceType>(context)
            .Where(row => events.Any(parent => parent.EventProvenanceTypeId == row.Id))
            .Select(row => new HumanAssignmentProvenanceFact(row.Id, row.MasterCode)));
        requireCurrent();
        return new(actor, tenant, eventIds, eventFacts, assignmentFacts, roleFacts, permissions,
            actors, members, organizationFacts, groupFacts, provenance);
    }

    private static IQueryable<T> Source<T>(ExploreDbContext context) where T : class =>
        // Authority includes deleted candidates; every tenant-owned row is bounded explicitly above.
        context.Set<T>().AsNoTracking().IgnoreQueryFilters([QueryFilterNames.Tenant, QueryFilterNames.SoftDelete]);
}
