using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Localization.Requests.Commands;

public sealed record TestTmsConnectionCommand : IRequest<BaseCommandResponse<Guid>>
{
}
