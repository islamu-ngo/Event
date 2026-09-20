using Explore.Application.DTOs.EventAgendaItem;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventAgendaItems.Requests.Queries;

public sealed record GetEventAgendaItemDetailRequest(Guid Id) : IQuery<EventAgendaItemDto?>;
