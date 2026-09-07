using Explore.Application.Authorization;
using Explore.Application.DTOs.EventAgendaItem;
using MediatR;

namespace Explore.Application.Features.EventAgendaItems.Requests.Queries;

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ViewManagement)]
public sealed record GetManagedEventAgendaItemsByEventRequest
    : IRequest<List<EventAgendaItemListDto>>, ISecureRequest
{
    public Guid EventId { get; init; }

    string? ISecureRequest.ResourceId => EventId.ToString();
}

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ViewManagement)]
public sealed record GetManagedEventAgendaItemDetailRequest
    : IRequest<EventAgendaItemDto?>, ISecureRequest
{
    public Guid EventId { get; init; }
    public Guid Id { get; init; }

    string? ISecureRequest.ResourceId => EventId.ToString();
}
