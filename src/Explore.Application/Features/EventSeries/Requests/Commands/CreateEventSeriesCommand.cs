using Explore.Application.DTOs.EventSeries;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EventSeries.Requests.Commands;

public sealed record CreateEventSeriesCommand : IRequest<BaseCommandResponse<Guid>>
{
    public CreateEventSeriesDto EventSeriesDto { get; init; } = null!;
}
