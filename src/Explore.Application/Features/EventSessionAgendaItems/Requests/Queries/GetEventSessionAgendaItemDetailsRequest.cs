using System;
using Explore.Application.DTOs.EventSessionAgendaItem;
using MediatR;

namespace Explore.Application.Features.EventSessionAgendaItems.Requests.Queries;

public sealed record GetEventSessionAgendaItemDetailsRequest : IRequest<EventSessionAgendaItemDto?>
{
    public Guid Id { get; init; }
}
