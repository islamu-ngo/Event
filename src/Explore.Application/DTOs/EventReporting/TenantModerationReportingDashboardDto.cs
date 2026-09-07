namespace Explore.Application.DTOs.EventReporting;

public sealed record TenantModerationReportingDashboardDto
{
    public Guid TenantId { get; init; }
    public DateTime GeneratedAtUtc { get; init; }
    public TenantModerationReportQueueHealthDto QueueHealth { get; init; } = new();
    public TenantModerationProviderSyncHealthDto ProviderSyncHealth { get; init; } = new();
}

public sealed record TenantModerationReportQueueHealthDto
{
    public int SubmittedReports { get; init; }
    public int InReviewReports { get; init; }
    public int ClosedReports { get; init; }
    public int OpenCases { get; init; }
    public int AssignedCases { get; init; }
    public int WaitingExternalCases { get; init; }
    public int WaitingReporterCases { get; init; }
    public int DecisionReadyCases { get; init; }
}

public sealed record TenantModerationProviderSyncHealthDto
{
    public int PendingSyncs { get; init; }
    public int StuckPendingSyncs { get; init; }
    public int FailedSyncs { get; init; }
    public int DisabledSyncs { get; init; }
    public int IgnoredSyncs { get; init; }
}
