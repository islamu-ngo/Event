using Explore.Application.Authorization;
using Explore.Application.DTOs.EventDay;
using MediatR;

namespace Explore.Application.Features.EventDays.Requests.Queries;

public sealed record GetEventDaysByEventRequest(Guid EventId) : IRequest<List<EventDayListDto>>;

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ViewManagement)]
public sealed record GetManagedEventDaysByEventRequest : IRequest<List<EventDayListDto>>, ISecureRequest
{
    public Guid EventId { get; init; }

    string? ISecureRequest.ResourceId => EventId.ToString();
}
