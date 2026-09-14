using System.Collections.Generic;
using Explore.Application.Mappings;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.UserAuthenticationToken;
using Explore.Application.Exceptions;
using Explore.Application.Features.UserAuthenticationTokens.Requests.Queries;

namespace Explore.Application.Features.UserAuthenticationTokens.Handlers.Queries;

public class GetUserAuthenticationTokenListRequestHandler : IQueryHandler<GetUserAuthenticationTokenListRequest, List<UserAuthenticationTokenListDto>>
{
    private readonly IUserAuthenticationTokenRepository _userAuthenticationTokenRepository;
    private readonly ICurrentUserService _currentUserService;

    public GetUserAuthenticationTokenListRequestHandler(
        IUserAuthenticationTokenRepository userAuthenticationTokenRepository,
        ICurrentUserService currentUserService)
    {
        _userAuthenticationTokenRepository = userAuthenticationTokenRepository;
        _currentUserService = currentUserService;
    }

    public async Task<List<UserAuthenticationTokenListDto>> QueryAsync(GetUserAuthenticationTokenListRequest request, CancellationToken cancellationToken = default)
    {
        var currentUserId = _currentUserService.UserId
            ?? throw new AuthorizationException(ResourceKinds.User, AuthorizationActions.Users.View);

        var tokens = await _userAuthenticationTokenRepository.GetUserAuthenticationTokensWithDetailsForUser(
            currentUserId,
            cancellationToken);
        return UserMapper.ToTokenList(tokens);
    }
}
