using Explore.Application.DTOs.EventAgendaItem;
using MediatR;

namespace Explore.Application.Features.EventAgendaItems.Requests.Queries;

public sealed record GetEventAgendaItemsByEventRequest(Guid EventId) : IRequest<List<EventAgendaItemListDto>>;
