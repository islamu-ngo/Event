using Explore.Application.DTOs.EventSeries;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventSeries.Requests.Commands;

public sealed record CreateEventSeriesCommand : ICommand<BaseCommandResponse<Guid>>
{
    public CreateEventSeriesDto EventSeriesDto { get; init; } = null!;
}
