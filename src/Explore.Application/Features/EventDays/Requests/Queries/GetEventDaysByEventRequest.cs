using Explore.Application.Authorization;
using Explore.Application.DTOs.EventDay;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventDays.Requests.Queries;

public sealed record GetEventDaysByEventRequest(Guid EventId) : IQuery<List<EventDayListDto>>;

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ViewManagement)]
public sealed record GetManagedEventDaysByEventRequest : IQuery<List<EventDayListDto>>, ISecureRequest
{
    public Guid EventId { get; init; }

    string? ISecureRequest.ResourceId => EventId.ToString();
}
