namespace Explore.Application.Features.Authentication.Continuation;

public enum VerifiedAccountContinuationOutcome
{
    Destination,
    NoMembership,
    Unlinked,
    Unavailable
}

/// <summary>
/// A navigation decision, never login authority. Final bootstrap must revalidate
/// the verified account, erasure/session eligibility, membership and current routing.
/// </summary>
public sealed record VerifiedAccountContinuationResult
{
    private VerifiedAccountContinuationResult(
        VerifiedAccountContinuationOutcome outcome,
        Guid? tenantId = null,
        string? tenantSlug = null,
        bool hasMultipleTenants = false)
    {
        Outcome = outcome;
        TenantId = tenantId;
        TenantSlug = tenantSlug;
        HasMultipleTenants = hasMultipleTenants;
    }

    public VerifiedAccountContinuationOutcome Outcome { get; }
    public Guid? TenantId { get; }
    public string? TenantSlug { get; }
    public bool HasMultipleTenants { get; }

    internal static VerifiedAccountContinuationResult Destination(Guid tenantId, string slug, bool multiple) =>
        new(VerifiedAccountContinuationOutcome.Destination, tenantId, slug, multiple);

    internal static VerifiedAccountContinuationResult NoMembership() =>
        new(VerifiedAccountContinuationOutcome.NoMembership);

    internal static VerifiedAccountContinuationResult Unlinked() =>
        new(VerifiedAccountContinuationOutcome.Unlinked);

    internal static VerifiedAccountContinuationResult Unavailable() =>
        new(VerifiedAccountContinuationOutcome.Unavailable);
}
