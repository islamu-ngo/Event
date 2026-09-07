namespace Explore.Application.DTOs.EventReporting;

public sealed record ModerationReportSignalDto
{
    public Guid Id { get; init; }
    public Guid? ReportId { get; init; }
    public Guid EventId { get; init; }
    public int ProviderId { get; init; }
    public required string ProviderCode { get; init; }
    public required string ProviderName { get; init; }
    public required string SignalType { get; init; }
    public required string PolicyCode { get; init; }
    public decimal? Score { get; init; }
    public int VerdictId { get; init; }
    public required string VerdictCode { get; init; }
    public required string VerdictName { get; init; }
    public int? RecommendedActionId { get; init; }
    public string? RecommendedActionCode { get; init; }
    public string? RecommendedActionName { get; init; }
    public string? SafeSummary { get; init; }
    public DateTime CreatedAtUtc { get; init; }
}
