using System;
using System.Collections.Generic;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventSessionAgendaItem;

namespace Explore.Application.Features.EventSessionAgendaItems.Requests.Queries;

public sealed record GetAgendaItemsBySessionRequest : IQuery<List<EventSessionAgendaItemListDto>>
{
    public Guid EventSessionId { get; init; }
}
