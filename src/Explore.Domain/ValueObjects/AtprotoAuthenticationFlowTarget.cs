namespace Explore.Domain.ValueObjects;

public enum AtprotoAuthenticationFlowKind
{
    Targeted = 1,
    UnscopedVerification = 2,
}

/// <summary>
/// Identifies an OAuth flow's intended tenant scope, without granting membership or session authority.
/// </summary>
public sealed record AtprotoAuthenticationFlowTarget
{
    public AtprotoAuthenticationFlowTarget(AtprotoAuthenticationFlowKind kind, Guid? tenantId)
    {
        switch (kind)
        {
            case AtprotoAuthenticationFlowKind.Targeted:
                if (tenantId is null || tenantId == Guid.Empty)
                {
                    throw new ArgumentException("A targeted authentication flow requires an exact nonempty tenant.", nameof(tenantId));
                }
                break;
            case AtprotoAuthenticationFlowKind.UnscopedVerification:
                if (tenantId is not null)
                {
                    throw new ArgumentException("An unscoped verification flow cannot carry a tenant.", nameof(tenantId));
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }

        Kind = kind;
        TenantId = tenantId;
    }

    public AtprotoAuthenticationFlowKind Kind { get; }
    public Guid? TenantId { get; }

    public static AtprotoAuthenticationFlowTarget Targeted(Guid tenantId) =>
        new(AtprotoAuthenticationFlowKind.Targeted, tenantId);

    public static AtprotoAuthenticationFlowTarget UnscopedVerification() =>
        new(AtprotoAuthenticationFlowKind.UnscopedVerification, null);

    public override string ToString() => nameof(AtprotoAuthenticationFlowTarget);
}
