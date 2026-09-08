namespace Explore.Infrastructure.NotificationFanout;

public sealed class NotificationFanoutProcessorSettings
{
    public const string SectionName = "NotificationFanoutProcessor";

    public bool Enabled { get; set; } = true;
    public int PollingIntervalSeconds { get; set; } = 5;
    public int PageSize { get; set; } = 250;
    public int MaxClaimsPerRound { get; set; } = 8;
    public int MaxActiveClaims { get; set; } = 8;
    public int MaxActiveClaimsPerTenant { get; set; } = 2;
    public int ClaimLeaseSeconds { get; set; } = 120;
    public int OptionalReminderBacklogHighWatermark { get; set; } = 1000;
    public int OptionalReminderBacklogLowWatermark { get; set; } = 500;
    public int HealthDueOccurrenceWarningThreshold { get; set; } = 1000;
    public int HealthExpiredClaimWarningThreshold { get; set; } = 1;
    public int HealthOldestDueWarningSeconds { get; set; } = 900;
    public string ConsumerId { get; set; } = Environment.MachineName;
}
