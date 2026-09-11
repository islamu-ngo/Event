using Explore.Application.Mappings;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.UserAuthenticationToken;
using Explore.Application.Exceptions;
using Explore.Application.Features.UserAuthenticationTokens.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.UserAuthenticationTokens.Handlers.Queries;

public class GetUserAuthenticationTokenDetailsRequestHandler : IRequestHandler<GetUserAuthenticationTokenDetailsRequest, UserAuthenticationTokenDto?>
{
    private readonly IUserAuthenticationTokenRepository _userAuthenticationTokenRepository;
    private readonly ICurrentUserService _currentUserService;

    public GetUserAuthenticationTokenDetailsRequestHandler(
        IUserAuthenticationTokenRepository userAuthenticationTokenRepository,
        ICurrentUserService currentUserService)
    {
        _userAuthenticationTokenRepository = userAuthenticationTokenRepository;
        _currentUserService = currentUserService;
    }

    public async Task<UserAuthenticationTokenDto?> Handle(GetUserAuthenticationTokenDetailsRequest request, CancellationToken cancellationToken)
    {
        var currentUserId = _currentUserService.UserId
            ?? throw new AuthorizationException(ResourceKinds.User, AuthorizationActions.Users.View);

        var token = await _userAuthenticationTokenRepository.GetUserAuthenticationTokenWithDetailsForUser(
            request.Id,
            currentUserId,
            cancellationToken);
        if (token == null)
        {
            return null;
        }

        return UserMapper.ToTokenDetail(token);
    }
}
