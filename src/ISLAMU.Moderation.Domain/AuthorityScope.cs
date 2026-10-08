namespace ISLAMU.Moderation.Domain;

public enum ModerationScopeKind
{
    TrustDomain = 1,
    Solution = 2,
    Instance = 3,
    Tenant = 4,
    Community = 5,
    Resource = 6
}

/// <summary>A qualified jurisdiction whose ancestry remains part of its identity.</summary>
public sealed record AuthorityScope
{
    private AuthorityScope(ModerationScopeKind kind, Guid id, AuthorityScope? parent)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A jurisdiction identifier is required.", nameof(id));
        }

        Kind = kind;
        Id = id;
        Parent = parent;
    }

    public ModerationScopeKind Kind { get; }
    public Guid Id { get; }
    public AuthorityScope? Parent { get; }
    public Guid TrustDomainId => Parent?.TrustDomainId ?? Id;

    public static AuthorityScope TrustDomain(Guid id) => new(ModerationScopeKind.TrustDomain, id, null);
    public static AuthorityScope Solution(AuthorityScope parent, Guid id) =>
        CreateChild(ModerationScopeKind.Solution, parent, id, ModerationScopeKind.TrustDomain);

    public static AuthorityScope Instance(AuthorityScope parent, Guid id) =>
        CreateChild(ModerationScopeKind.Instance, parent, id, ModerationScopeKind.Solution);

    public static AuthorityScope Tenant(AuthorityScope parent, Guid id) =>
        CreateChild(ModerationScopeKind.Tenant, parent, id, ModerationScopeKind.Instance);

    public static AuthorityScope Community(AuthorityScope parent, Guid id) =>
        CreateChild(ModerationScopeKind.Community, parent, id, ModerationScopeKind.Tenant);

    public static AuthorityScope Resource(AuthorityScope parent, Guid id)
    {
        ArgumentNullException.ThrowIfNull(parent);
        if (parent.Kind is not (ModerationScopeKind.Tenant or ModerationScopeKind.Community))
        {
            throw new ArgumentException("A resource must belong to a tenant or community.", nameof(parent));
        }

        return new AuthorityScope(ModerationScopeKind.Resource, id, parent);
    }

    public bool Contains(AuthorityScope target)
    {
        ArgumentNullException.ThrowIfNull(target);
        for (AuthorityScope? current = target; current is not null; current = current.Parent)
        {
            if (this == current)
            {
                return true;
            }
        }

        return false;
    }

    public override string ToString() => Kind.ToString();

    private static AuthorityScope CreateChild(
        ModerationScopeKind kind, AuthorityScope parent, Guid id, ModerationScopeKind parentKind)
    {
        ArgumentNullException.ThrowIfNull(parent);
        if (parent.Kind != parentKind)
        {
            throw new ArgumentException("The jurisdiction parent does not match its scope.", nameof(parent));
        }

        return new AuthorityScope(kind, id, parent);
    }
}
