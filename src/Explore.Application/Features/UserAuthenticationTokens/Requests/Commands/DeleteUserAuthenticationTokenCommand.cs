using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.UserAuthenticationTokens.Requests.Commands;

public sealed record DeleteUserAuthenticationTokenCommand(Guid Id = default) : ICommand;
