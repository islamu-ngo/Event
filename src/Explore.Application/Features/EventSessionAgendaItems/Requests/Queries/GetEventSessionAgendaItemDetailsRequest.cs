using System;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventSessionAgendaItem;

namespace Explore.Application.Features.EventSessionAgendaItems.Requests.Queries;

public sealed record GetEventSessionAgendaItemDetailsRequest : IQuery<EventSessionAgendaItemDto?>
{
    public Guid Id { get; init; }
}
