using Explore.Domain.Enums;

namespace Explore.Application.DTOs.EventReporting;

public sealed record DecideModerationReportRequestDto
{
    public Guid CaseId { get; init; }
    public Guid ExpectedCaseConcurrencyStamp { get; init; }
    public EventReportDecisionKind DecisionKind { get; init; }
    public required string ReasonCode { get; init; }
    public string? SafeNote { get; init; }
    public Guid? DuplicateGroupId { get; init; }
}
