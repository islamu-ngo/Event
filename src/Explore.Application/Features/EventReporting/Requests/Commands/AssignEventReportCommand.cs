using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.Responses;

namespace Explore.Application.Features.EventReporting.Requests.Commands;

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ModerateLight)]
public sealed record AssignEventReportCommand : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid EventId { get; init; }
    public Guid ReportId { get; init; }
    public Guid CaseId { get; init; }
    public Guid AssigneeUserId { get; init; }
    public Guid ExpectedCaseConcurrencyStamp { get; init; }

    string? ISecureRequest.ResourceId => EventId == Guid.Empty ? null : EventId.ToString();
}
