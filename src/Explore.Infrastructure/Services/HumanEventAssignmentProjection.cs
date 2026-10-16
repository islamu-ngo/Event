using Cerbos.Sdk.Builder;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Domain.Constants;
using Explore.Domain.Enums;

namespace Explore.Infrastructure.Services;

/// <summary>
/// Pure projection of the ordinary Event authority and Cerbos input shapes. No I/O, ambient
/// identity, clock sampling, moderation sorting or Local-versus-Cerbos strengthening.
/// </summary>
public static class HumanEventAssignmentProjection
{
    public static EventAuthoritySnapshot Authority(HumanEventAssignmentSourceFacts facts, DateTime evaluationTimeUtc)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (evaluationTimeUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Assignment evaluation requires UTC.", nameof(evaluationTimeUtc));
        var active = facts.Assignments.Where(row => row.Status == (int)EventRoleAssignmentStatus.Active
            && row.StartsAtUtc <= evaluationTimeUtc && (row.ExpiresAtUtc is null || row.ExpiresAtUtc > evaluationTimeUtc));
        var assignments = active.Join(facts.Roles, row => row.RoleId, role => role.Id,
            (row, role) => (Assignment: row, Role: role)).ToArray();
        var roleIds = assignments.Select(row => row.Assignment.RoleId).ToHashSet();
        var permissionsByRole = facts.Permissions.Where(row => roleIds.Contains(row.RoleId) && row.IsActive)
            .GroupBy(row => row.RoleId).ToDictionary(group => group.Key,
                group => group.Select(row => row.MasterCode).ToHashSet(StringComparer.Ordinal));
        var events = new Dictionary<Guid, EventAuthorityForUser>();
        foreach (Guid eventId in facts.EventIds)
        {
            var rows = assignments.Where(row => row.Assignment.EventId == eventId).ToArray();
            HashSet<string> roles = new(StringComparer.Ordinal), permissions = new(StringComparer.Ordinal);
            foreach (var row in rows)
            {
                roles.Add(row.Role.MasterCode);
                if (permissionsByRole.TryGetValue(row.Assignment.RoleId, out var codes)) permissions.UnionWith(codes);
            }
            events.Add(eventId, new(roles, permissions,
                rows.Any(row => row.Assignment.RoleId == (int)RoleEnum.EventOwner),
                rows.Any(row => row.Assignment.RoleId == (int)RoleEnum.EventManager)
                    || permissions.Contains(PermissionCodes.EventManageTeam)));
        }
        return new(facts.TenantId, facts.PrincipalId, events);
    }

    /// <summary>
    /// Mutates only the supplied SDK principal, just like the original enrichment. The parent
    /// builds that principal from the matching HumanPrincipalSourceFacts, preserves its original
    /// list multiplicity, and samples nowUtc separately AFTER source retrieval.
    /// </summary>
    public static void EnrichPrincipal(Principal principal, HumanEventAssignmentSourceFacts facts,
        DateTime assignmentEvaluationUtc, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(principal);
        if (nowUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Cerbos nowUtc requires UTC.", nameof(nowUtc));
        var snapshot = Authority(facts, assignmentEvaluationUtc);
        if (snapshot.Events.Count == 0) return;
        var assignments = new Dictionary<string, AttributeValue>();
        foreach (var (eventId, authority) in snapshot.Events)
        {
            assignments[eventId.ToString()] = AttributeValue.MapValue(new Dictionary<string, AttributeValue>
            {
                ["tenantId"] = AttributeValue.StringValue(facts.TenantId.ToString()),
                ["roles"] = AttributeValue.ListValue(authority.RoleCodes.Select(AttributeValue.StringValue).ToArray()),
                ["permissions"] = AttributeValue.ListValue(authority.PermissionCodes.Select(AttributeValue.StringValue).ToArray())
            });
        }
        principal.WithAttribute("eventAssignments", AttributeValue.MapValue(assignments));
        principal.WithAttribute("nowUtc", AttributeValue.StringValue(nowUtc.ToString("o")));
    }

    /// <summary>Original persisted Event attributes, usable for Event/manage-registration-channels.</summary>
    public static EventAuthorizationFacts? Resource(HumanEventAssignmentSourceFacts facts, Guid eventId)
    {
        var parent = facts.Events.SingleOrDefault(row => row.Id == eventId);
        if (parent is null || parent.IsDeleted) return null;
        var actor = facts.Actors.SingleOrDefault(row => row.Id == parent.ActorId && !row.IsDeleted);
        // IncludeStandardDetails includes the required creating Actor. Its soft-delete
        // filter suppresses the parent through the required join, not just the owner IDs.
        if (actor is null) return null;
        var organizer = facts.Actors.SingleOrDefault(row => row.Id == parent.OrganizerActorId && !row.IsDeleted);
        return new(parent.TenantId, parent.Id, parent.ActorId, actor?.UserId, actor?.OrganizationId,
            actor?.GroupId, parent.OrganizerActorId, organizer?.UserId, organizer?.OrganizationId, organizer?.GroupId,
            facts.ProvenanceTypes.SingleOrDefault(row => row.Id == parent.EventProvenanceTypeId)?.MasterCode
                ?? parent.EventProvenanceTypeId.ToString(),
            parent.SubmittedByUserId);
    }

    /// <summary>
    /// Separate original organizer-control predicate, not a new gate on ordinary Event policy.
    /// Administrator status is intentionally absent from this calculation.
    /// </summary>
    public static bool ControlsOrganizer(HumanEventAssignmentSourceFacts facts, Guid eventId)
    {
        var parent = facts.Events.SingleOrDefault(row => row.Id == eventId);
        if (parent is null || parent.IsDeleted
            || !facts.Actors.Any(row => row.Id == parent.ActorId && !row.IsDeleted)) return false;
        var actor = facts.Actors.SingleOrDefault(row => row.Id == parent.OrganizerActorId && !row.IsDeleted && !row.IsSuspended);
        if (actor is null) return false;
        if (actor.UserId == facts.PrincipalId && facts.TenantMembers.Any(member =>
            !member.IsDeleted && member.StatusId == (int)TenantUserStatusEnum.Active && member.ActorId == actor.Id)) return true;
        var createRoles = facts.Permissions.Where(row => row.IsActive && row.MasterCode == PermissionCodes.EventCreate)
            .Select(row => row.RoleId).ToHashSet();
        bool Controls(IEnumerable<HumanAssignmentOwnerMemberFact> members, Guid? ownerId) =>
            ownerId.HasValue && members.Any(member => member.OwnerId == ownerId && !member.IsDeleted
                && !member.PlacementIsDeleted && !member.IsSuspended && member.IsOrganizerEligible
                && member.ApprovalStatusId == (int)ApprovalStatusEnum.Approved && createRoles.Contains(member.RoleId));
        return Controls(facts.Organizations, actor.OrganizationId) || Controls(facts.Groups, actor.GroupId);
    }
}
