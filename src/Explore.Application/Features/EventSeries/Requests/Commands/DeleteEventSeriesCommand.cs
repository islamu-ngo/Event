using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EventSeries.Requests.Commands;

public sealed record DeleteEventSeriesCommand(Guid Id = default) : IRequest<BaseCommandResponse<bool>>;
