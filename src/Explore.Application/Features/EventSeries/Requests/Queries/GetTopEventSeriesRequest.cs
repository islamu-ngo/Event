using Explore.Application.DTOs.EventSeries;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EventSeries.Requests.Queries;

public sealed record GetTopEventSeriesRequest : IRequest<EventSeriesDto?>;
