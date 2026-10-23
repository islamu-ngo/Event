namespace Explore.Application.Contracts.Persistence;

public interface IEventResourceAuditRetentionRepository
{
    /// <summary>A null cutoff purges every audit row in the bounded tenant batch for retention zero.</summary>
    Task<int> DeleteExpiredBatchAsync(
        Guid tenantId, DateTime? inclusiveCutoffUtc, int limit, CancellationToken cancellationToken);
}
