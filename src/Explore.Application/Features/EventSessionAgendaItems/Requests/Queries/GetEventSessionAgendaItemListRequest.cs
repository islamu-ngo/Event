using System.Collections.Generic;
using Explore.Application.DTOs.EventSessionAgendaItem;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EventSessionAgendaItems.Requests.Queries;

public sealed record GetEventSessionAgendaItemListRequest : IRequest<PaginatedResult<EventSessionAgendaItemListDto>>
{
    /// <summary>
    /// Gets or sets the page number (1-based). Defaults to 1.
    /// </summary>
    public int PageNumber { get; init; } = 1;

    /// <summary>
    /// Gets or sets the page size. Defaults to 20.
    /// </summary>
    public int PageSize { get; init; } = 20;
}
