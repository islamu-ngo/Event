using Explore.Application.Authorization;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EventReporting.Requests.Commands;

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ModerateLight)]
public sealed record ExecuteReportDecisionCommand : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid EventId { get; init; }
    public Guid ReportId { get; init; }
    public Guid CaseId { get; init; }
    public Guid DecisionId { get; init; }
    public Guid ExpectedCaseConcurrencyStamp { get; init; }
    public string? CorrelationId { get; init; }

    string? ISecureRequest.ResourceId => EventId == Guid.Empty ? null : EventId.ToString();
}
