namespace Explore.Application.DTOs.EventReporting;

public sealed record OspreySignalCallbackItemDto
{
    public required string SignalType { get; init; }
    public required string PolicyCode { get; init; }
    public decimal? Score { get; init; }
    public string? Verdict { get; init; }
    public string? RecommendedAction { get; init; }
    public string? SafeSummary { get; init; }
    public string? ExternalSignalId { get; init; }
    public string? CorrelationId { get; init; }
    public DateTime? CreatedAtUtc { get; init; }
}
