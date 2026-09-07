using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IExternalApiKeyQuotaRepository : IGenericRepository<ExternalApiKeyQuota, Guid>
{
    /// <summary>
    /// Gets the current active quota period for an API key.
    /// Returns null if no quota has been provisioned for the current period.
    /// </summary>
    Task<ExternalApiKeyQuota?> GetCurrentPeriodQuota(Guid externalApiKeyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lazily provisions a new quota period row if one does not already exist.
    /// Uses INSERT ON CONFLICT DO NOTHING for race-safe idempotent creation.
    /// Returns the quota row (existing or newly created).
    /// </summary>
    Task<ExternalApiKeyQuota> LazyProvisionPeriod(
        Guid externalApiKeyId,
        DateOnly periodStart,
        DateOnly periodEnd,
        int creditLimit,
        int rolloverCredits,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically decrements credits for a quota period.
    /// Uses conditional UPDATE (credits_used + amount &lt;= credit_limit) for race-safe enforcement.
    /// Returns true if credits were successfully consumed, false if insufficient credits.
    /// </summary>
    Task<bool> TryConsumeCredits(Guid quotaId, int amount, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically increments request count for a quota period without consuming credits.
    /// Used for unlimited keys or separate request tracking.
    /// </summary>
    Task IncrementRequestCount(Guid quotaId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all quota periods for an API key, ordered by period start descending.
    /// </summary>
    Task<IReadOnlyList<ExternalApiKeyQuota>> GetQuotaHistory(Guid externalApiKeyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets aggregated usage data for all API keys belonging to a tenant within a date range.
    /// Returns per-key totals of credits used and request count. Does not expose secret material.
    /// </summary>
    Task<IReadOnlyList<TenantApiKeyUsageSummary>> GetUsageByTenant(Guid tenantId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets aggregated usage across all API keys (platform-wide) within a date range.
    /// For instance-admin reporting. Does not expose secret material.
    /// </summary>
    Task<IReadOnlyList<TenantApiKeyUsageSummary>> GetUsagePlatformWide(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
}

/// <summary>
/// Aggregated usage summary per API key for reporting. Does not expose secret material (hash, full key ID).
/// </summary>
public record TenantApiKeyUsageSummary(
    Guid ApiKeyId,
    string ApiKeyName,
    Guid? TenantId,
    int OwnerType,
    Guid OwnerId,
    long TotalRequestCount,
    int TotalCreditsUsed,
    int CreditLimit);
