using Explore.Application.Authorization;
using Explore.Application.Responses;
using Explore.Domain.Enums;
using MediatR;

namespace Explore.Application.Features.EventReporting.Requests.Commands;

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ModerateLight)]
public sealed record DecideEventReportCommand : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid EventId { get; init; }
    public Guid ReportId { get; init; }
    public Guid CaseId { get; init; }
    public Guid ExpectedCaseConcurrencyStamp { get; init; }
    public EventReportDecisionKind DecisionKind { get; init; }
    public required string ReasonCode { get; init; }
    public string? SafeNote { get; init; }
    public Guid? DuplicateGroupId { get; init; }

    string? ISecureRequest.ResourceId => EventId == Guid.Empty ? null : EventId.ToString();
}
