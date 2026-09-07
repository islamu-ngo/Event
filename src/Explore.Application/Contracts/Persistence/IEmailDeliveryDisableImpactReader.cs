// ABOUTME: Defines the transaction-bound, non-secret impact of disabling email delivery at one scope.
// ABOUTME: Carries actual affected scope revisions without exposing SMTP settings or credential coordinates.

using System.Collections.Immutable;

namespace Explore.Application.Contracts.Persistence;

public interface IEmailDeliveryDisableImpactReader
{
    Task<EmailDeliveryDisableImpactSnapshot?> ReadAsync(
        Guid? tenantId,
        CancellationToken cancellationToken = default);
}

public sealed record EmailDeliveryAffectedScope(Guid? TenantId, long Revision);

public sealed record EmailDeliveryDisableImpactSnapshot(
    Guid? TenantId,
    long Revision,
    bool IsLocked,
    ImmutableArray<EmailDeliveryAffectedScope> AffectedScopes)
{
    public bool CanDisable => !IsLocked && !AffectedScopes.IsDefaultOrEmpty;
}
