namespace Explore.Application.DTOs.EventReporting;

public sealed record ModerationReportExternalLinkDto
{
    public Guid Id { get; init; }
    public Guid ReportId { get; init; }
    public Guid? CaseId { get; init; }
    public int ProviderId { get; init; }
    public required string ProviderCode { get; init; }
    public required string ProviderName { get; init; }
    public int SyncStateId { get; init; }
    public required string SyncStateCode { get; init; }
    public required string SyncStateName { get; init; }
    public DateTime? LastSyncedAtUtc { get; init; }
    public string? LastErrorCategory { get; init; }
    public int RetryCount { get; init; }
    public DateTime CreatedAtUtc { get; init; }
}
