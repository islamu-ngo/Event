using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Integrations.Listmonk.Requests.Commands;

public sealed record TestListmonkConnectionCommand : IRequest<BaseCommandResponse<Guid>>
{
}
