using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.UserAuthenticationTokens.Requests.Commands;

namespace Explore.Application.Features.UserAuthenticationTokens.Handlers.Commands;

public class DeleteUserAuthenticationTokenCommandHandler : ICommandHandler<DeleteUserAuthenticationTokenCommand>
{
    private readonly IUserAuthenticationTokenRepository _userAuthenticationTokenRepository;
    private readonly ICurrentUserService _currentUserService;

    public DeleteUserAuthenticationTokenCommandHandler(
        IUserAuthenticationTokenRepository userAuthenticationTokenRepository,
        ICurrentUserService currentUserService)
    {
        _userAuthenticationTokenRepository = userAuthenticationTokenRepository;
        _currentUserService = currentUserService;
    }

    public async Task ExecuteAsync(DeleteUserAuthenticationTokenCommand request, CancellationToken cancellationToken = default)
    {
        var currentUserId = _currentUserService.UserId
            ?? throw new AuthorizationException(ResourceKinds.User, AuthorizationActions.Users.Update);

        var token = await _userAuthenticationTokenRepository.GetByIdForUser(
            request.Id,
            currentUserId,
            cancellationToken);
        if (token == null)
        {
            return;
        }

        await _userAuthenticationTokenRepository.Delete(token);
    }
}
