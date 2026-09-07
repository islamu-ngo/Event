namespace Explore.Application.Models;

public sealed record EmailDispatchRetentionCleanupResult(
    DateTime CutoffUtc,
    int TenantCount,
    int SucceededTenantCount,
    int FailedTenantCount,
    int EligibleCount,
    int RedactedCount,
    bool DryRun);
