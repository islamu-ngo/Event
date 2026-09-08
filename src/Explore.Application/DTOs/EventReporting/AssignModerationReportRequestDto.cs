namespace Explore.Application.DTOs.EventReporting;

public sealed record AssignModerationReportRequestDto
{
    public Guid CaseId { get; init; }
    public Guid ExpectedCaseConcurrencyStamp { get; init; }
    public Guid AssigneeUserId { get; init; }
}
