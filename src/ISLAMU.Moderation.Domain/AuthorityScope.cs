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

    internal (ModerationScopeKind Kind, Guid TrustDomainId, Guid? SolutionId, Guid? InstanceId,
        Guid? TenantId, Guid? CommunityId, Guid? ResourceId) Coordinates =>
        (Kind, TrustDomainId, AncestorId(ModerationScopeKind.Solution), AncestorId(ModerationScopeKind.Instance),
            AncestorId(ModerationScopeKind.Tenant), AncestorId(ModerationScopeKind.Community),
            AncestorId(ModerationScopeKind.Resource));

    internal static AuthorityScope FromCoordinates(
        ModerationScopeKind kind, Guid trustDomainId, Guid? solutionId, Guid? instanceId,
        Guid? tenantId, Guid? communityId, Guid? resourceId)
    {
        var shapeIsValid = kind switch
        {
            ModerationScopeKind.TrustDomain => solutionId is null && instanceId is null
                && tenantId is null && communityId is null && resourceId is null,
            ModerationScopeKind.Solution => solutionId is not null && instanceId is null
                && tenantId is null && communityId is null && resourceId is null,
            ModerationScopeKind.Instance => solutionId is not null && instanceId is not null
                && tenantId is null && communityId is null && resourceId is null,
            ModerationScopeKind.Tenant => solutionId is not null && instanceId is not null
                && tenantId is not null && communityId is null && resourceId is null,
            ModerationScopeKind.Community => solutionId is not null && instanceId is not null
                && tenantId is not null && communityId is not null && resourceId is null,
            ModerationScopeKind.Resource => solutionId is not null && instanceId is not null
                && tenantId is not null && resourceId is not null,
            _ => false
        };
        if (!shapeIsValid)
        {
            throw new ArgumentException("The jurisdiction tag and ancestry coordinates are inconsistent.", nameof(kind));
        }

        var scope = TrustDomain(trustDomainId);
        if (solutionId is { } solution) scope = Solution(scope, solution);
        if (instanceId is { } instance) scope = Instance(scope, instance);
        if (tenantId is { } tenant) scope = Tenant(scope, tenant);
        if (communityId is { } community) scope = Community(scope, community);
        if (resourceId is { } resource) scope = Resource(scope, resource);
        return scope;
    }

    private Guid? AncestorId(ModerationScopeKind kind)
    {
        for (AuthorityScope? scope = this; scope is not null; scope = scope.Parent)
        {
            if (scope.Kind == kind)
            {
                return scope.Id;
            }
        }

        return null;
    }

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
