using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventSeries.Requests.Commands;

public sealed record DeleteEventSeriesCommand(Guid Id = default) : ICommand<BaseCommandResponse<bool>>;
