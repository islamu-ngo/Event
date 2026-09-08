using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Authentication.Atproto.Requests.Commands;

public sealed record ProbeAtprotoTransientCommand : IRequest<BaseCommandResponse<Guid>>;
