namespace Explore.Application.DTOs.EventReporting;

public sealed record ModerationReportQueueItemDto
{
    public Guid Id { get; init; }
    public Guid EventId { get; init; }
    public int ReporterKindId { get; init; }
    public required string ReporterKindCode { get; init; }
    public required string ReporterKindName { get; init; }
    public int SourceKindId { get; init; }
    public required string SourceKindCode { get; init; }
    public required string SourceKindName { get; init; }
    public int StatusId { get; init; }
    public required string StatusCode { get; init; }
    public required string StatusName { get; init; }
    public int PriorityId { get; init; }
    public required string PriorityCode { get; init; }
    public required string PriorityName { get; init; }
    public int? SeverityHintId { get; init; }
    public string? SeverityHintCode { get; init; }
    public string? SeverityHintName { get; init; }
    public int? ReasonId { get; init; }
    public required string ReasonCode { get; init; }
    public required string ReasonName { get; init; }
    public string? SubcategoryCode { get; init; }
    public required bool ReportCaseUpdatesConsent { get; init; }
    public required bool ReportFollowUpContactConsent { get; init; }
    public DateTime SubmittedAtUtc { get; init; }
    public DateTime? LastUpdatedAtUtc { get; init; }
    public DateTime? ClosedAtUtc { get; init; }
    public ModerationReportCaseDto? CurrentCase { get; init; }
    public int DecisionCount { get; init; }
    public int SignalCount { get; init; }
    public int ExternalLinkCount { get; init; }
}
