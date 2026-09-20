using Explore.Application.DTOs.EventSeries;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventSeries.Requests.Queries;

public sealed record GetTopEventSeriesRequest : IQuery<EventSeriesDto?>;
