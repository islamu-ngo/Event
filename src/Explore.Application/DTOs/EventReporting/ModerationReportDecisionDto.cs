namespace Explore.Application.DTOs.EventReporting;

public sealed record ModerationReportDecisionDto
{
    public Guid Id { get; init; }
    public Guid CaseId { get; init; }
    public Guid ReportId { get; init; }
    public int DecisionSourceId { get; init; }
    public required string DecisionSourceCode { get; init; }
    public required string DecisionSourceName { get; init; }
    public int DecisionKindId { get; init; }
    public required string DecisionKindCode { get; init; }
    public required string DecisionKindName { get; init; }
    public required string ReasonCode { get; init; }
    public string? SafeNote { get; init; }
    public string? ExternalDecisionId { get; init; }
    public DateTime CreatedAtUtc { get; init; }
}
