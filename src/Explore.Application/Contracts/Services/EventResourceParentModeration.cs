using System.Collections;
using System.Collections.Immutable;
using Explore.Application.Authorization;

namespace Explore.Application.Contracts.Services;

/// <summary>An immutable set with structural equality for the frozen native event policy contract.</summary>
public sealed class EventModerationSet<T> : IReadOnlyCollection<T>, IEquatable<EventModerationSet<T>> where T : notnull
{
    private readonly ImmutableHashSet<T> values;

    public EventModerationSet(IEnumerable<T> values) => this.values = values.ToImmutableHashSet();
    public int Count => values.Count;
    public bool Contains(T value) => values.Contains(value);
    public IEnumerator<T> GetEnumerator() => values.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public bool Equals(EventModerationSet<T>? other) => other is not null && values.SetEquals(other.values);
    public override bool Equals(object? obj) => obj is EventModerationSet<T> other && Equals(other);
    public override int GetHashCode() => values.Aggregate(0, (hash, value) => hash ^ value.GetHashCode());
}

/// <summary>Canonical native permission lists preserve multiplicity, which custom policies may inspect.</summary>
public sealed class EventModerationList<T> : IReadOnlyCollection<T>, IEquatable<EventModerationList<T>> where T : notnull, IComparable<T>
{
    private readonly T[] values;

    public EventModerationList(IEnumerable<T> values) => this.values = values.Order().ToArray();
    public int Count => values.Length;
    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)values).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public bool Equals(EventModerationList<T>? other) => other is not null && values.SequenceEqual(other.values);
    public override bool Equals(object? obj) => obj is EventModerationList<T> other && Equals(other);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (T value in values) hash.Add(value);
        return hash.ToHashCode();
    }
}

/// <summary>Native event scope is independent of resource binding scope. Native event policy version is default.</summary>
public sealed record EventResourceParentPolicyRoute(string Scope, string PolicyVersion = "default")
{
    public bool IsUsable => Scope is not null && Scope == Scope.Trim() && PolicyVersion == "default";
}

public sealed record EventModerationEventAssignment(
    Guid EventId, Guid TenantId, EventModerationSet<string> Roles, EventModerationSet<string> Permissions);

/// <summary>All native non-clock user principal attributes; no live enrichment is allowed in the adapter.</summary>
public sealed record EventModerationPrincipal(
    Guid UserId,
    bool IsInstanceAdmin,
    EventModerationSet<Guid> AdminTenantIds,
    EventModerationSet<Guid> AdminOrganizationIds,
    EventModerationSet<Guid> AdminGroupIds,
    EventModerationList<Guid> EventCreateOrganizationIds,
    EventModerationList<Guid> EventCreateGroupIds,
    EventModerationList<Guid> EventFinanceOrganizationIds,
    EventModerationList<Guid> EventFinanceGroupIds,
    EventModerationSet<EventModerationEventAssignment> EventAssignments)
{
    public bool CanModerate(Guid tenantId) => IsInstanceAdmin || AdminTenantIds.Contains(tenantId);
}

/// <summary>Shares the enclosing resource route's endpoint, deployment and epoch, never a generic cached route.</summary>
public sealed record EventResourceParentModeration(
    EventResourceParentPolicyRoute Route,
    EventModerationPrincipal Principal,
    EventAuthorizationFacts Resource);
