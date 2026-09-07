namespace Explore.Application.DTOs.EventReporting;

public sealed record EventReportOptionsDto
{
    public Guid EventId { get; init; }
    public bool IsReportable { get; init; }
    public string? UnavailableReasonCode { get; init; }
    public string? UnavailableReasonMessage { get; init; }
    public int MaxReporterTextLength { get; init; }
    public IReadOnlyList<EventReportReasonOptionDto> ReasonOptions { get; init; } = [];
}
