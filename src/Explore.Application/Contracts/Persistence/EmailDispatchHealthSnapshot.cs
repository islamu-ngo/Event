using System.Collections.ObjectModel;

namespace Explore.Application.Contracts.Persistence;

public sealed record EmailDispatchHealthSnapshot
{
    public int DueDispatchCount { get; }
    public int RetryScheduledCount { get; }
    public int StaleProcessingCount { get; }
    public int DeadLetteredCount { get; }
    public int UnknownCount { get; }
    public int ParkedCount { get; }
    public DateTime? OldestDueCreatedAt { get; }
    public bool OptionalReminderDeferralActive { get; }
    public bool GlobalPaused { get; }
    public bool GlobalSmtpRateLimitOverrideActive { get; }
    public IReadOnlyDictionary<Guid, int> TenantBacklogSample { get; }

    public EmailDispatchHealthSnapshot(
        int dueDispatchCount,
        int retryScheduledCount,
        int staleProcessingCount,
        int deadLetteredCount,
        int unknownCount,
        int parkedCount,
        DateTime? oldestDueCreatedAt,
        bool optionalReminderDeferralActive,
        bool globalPaused,
        bool globalSmtpRateLimitOverrideActive,
        IReadOnlyDictionary<Guid, int> tenantBacklogSample)
    {
        DueDispatchCount = dueDispatchCount;
        RetryScheduledCount = retryScheduledCount;
        StaleProcessingCount = staleProcessingCount;
        DeadLetteredCount = deadLetteredCount;
        UnknownCount = unknownCount;
        ParkedCount = parkedCount;
        OldestDueCreatedAt = oldestDueCreatedAt;
        OptionalReminderDeferralActive = optionalReminderDeferralActive;
        GlobalPaused = globalPaused;
        GlobalSmtpRateLimitOverrideActive = globalSmtpRateLimitOverrideActive;
        TenantBacklogSample = new ReadOnlyDictionary<Guid, int>(tenantBacklogSample.ToDictionary());
    }
}
