namespace Explore.Application.Models;

public sealed record IdempotencyCleanupResult(
    DateTime ExpiresBeforeUtc,
    int EligibleCount,
    int DeletedCount,
    bool DryRun);
