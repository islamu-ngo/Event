using Explore.Application.Contracts.Operations;
using Explore.Application.Responses;

namespace Explore.Application.Features.Authentication.Atproto.Requests.Commands;

public sealed record ProbeAtprotoTransientCommand : ICommand<BaseCommandResponse<Guid>>;
