using Explore.Domain.Enums;

namespace Explore.Application.DTOs.EventReporting;

public sealed record TriageModerationReportRequestDto
{
    public Guid CaseId { get; init; }
    public Guid ExpectedCaseConcurrencyStamp { get; init; }
    public required string QueueCode { get; init; }
    public EventReportPriority Priority { get; init; }
}
