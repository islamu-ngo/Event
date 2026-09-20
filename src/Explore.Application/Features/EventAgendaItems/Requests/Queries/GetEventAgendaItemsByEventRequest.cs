using Explore.Application.DTOs.EventAgendaItem;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventAgendaItems.Requests.Queries;

public sealed record GetEventAgendaItemsByEventRequest(Guid EventId) : IQuery<List<EventAgendaItemListDto>>;
