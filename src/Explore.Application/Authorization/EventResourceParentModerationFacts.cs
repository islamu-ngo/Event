using Explore.Application.Contracts.Services;

namespace Explore.Application.Authorization;

public sealed record EventModerationTimedAssignment(
    Guid EventId, string RoleCode, EventModerationSet<string> Permissions, EventResourceTimedAuthority Authority);

/// <summary>Preserves native assignment windows so final-time projection can invalidate an earlier remote decision.</summary>
public sealed class EventModerationPrincipalFacts
{
    private readonly EventModerationPrincipal principal;
    private readonly EventModerationSet<Guid> eventIds;
    private readonly IReadOnlyList<EventModerationTimedAssignment> assignments;
    private readonly Guid tenantId;

    public EventModerationPrincipalFacts(EventModerationPrincipal principal, Guid tenantId,
        IEnumerable<Guid> eventIds, IEnumerable<EventModerationTimedAssignment> assignments)
    {
        this.principal = principal;
        this.tenantId = tenantId;
        this.eventIds = new(eventIds);
        this.assignments = Array.AsReadOnly(assignments.ToArray());
    }

    public bool CanModerate => principal.CanModerate(tenantId);

    public EventModerationPrincipal Evaluate(DateTimeOffset now)
    {
        var effective = assignments.Where(value => value.Authority.IsEffectiveAt(now)).ToLookup(value => value.EventId);
        // Native batch enrichment includes an entry for every relevant parent, even without assignments.
        return principal with
        {
            EventAssignments = new(eventIds.Select(eventId => new EventModerationEventAssignment(
                eventId, tenantId, new(effective[eventId].Select(value => value.RoleCode)),
                new(effective[eventId].SelectMany(value => value.Permissions)))))
        };
    }
}

public sealed record EventResourceParentModerationFacts(
    EventModerationPrincipalFacts Principal, EventAuthorizationFacts Resource)
{
    public EventResourceParentModeration Evaluate(EventResourceParentPolicyRoute route, DateTimeOffset now) =>
        new(route, Principal.Evaluate(now), Resource);
}
