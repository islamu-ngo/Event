namespace Explore.Application.DTOs.EventReporting;

public sealed record EventReportReasonOptionDto
{
    public int ReasonId { get; init; }
    public required string ReasonCode { get; init; }
    public required string ReasonName { get; init; }
    public required string Description { get; init; }
}
