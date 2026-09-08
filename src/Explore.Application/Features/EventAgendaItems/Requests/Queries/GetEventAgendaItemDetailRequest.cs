using Explore.Application.DTOs.EventAgendaItem;
using MediatR;

namespace Explore.Application.Features.EventAgendaItems.Requests.Queries;

public sealed record GetEventAgendaItemDetailRequest(Guid Id) : IRequest<EventAgendaItemDto?>;
