using System.Collections.Generic;
using AutoMapper;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.UserAuthenticationToken;
using Explore.Application.Exceptions;
using Explore.Application.Features.UserAuthenticationTokens.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.UserAuthenticationTokens.Handlers.Queries;

public class GetUserAuthenticationTokenListRequestHandler : IRequestHandler<GetUserAuthenticationTokenListRequest, List<UserAuthenticationTokenListDto>>
{
    private readonly IUserAuthenticationTokenRepository _userAuthenticationTokenRepository;
    private readonly IMapper _mapper;
    private readonly ICurrentUserService _currentUserService;

    public GetUserAuthenticationTokenListRequestHandler(
        IUserAuthenticationTokenRepository userAuthenticationTokenRepository,
        IMapper mapper,
        ICurrentUserService currentUserService)
    {
        _userAuthenticationTokenRepository = userAuthenticationTokenRepository;
        _mapper = mapper;
        _currentUserService = currentUserService;
    }

    public async Task<List<UserAuthenticationTokenListDto>> Handle(GetUserAuthenticationTokenListRequest request, CancellationToken cancellationToken)
    {
        var currentUserId = _currentUserService.UserId
            ?? throw new AuthorizationException(ResourceKinds.User, AuthorizationActions.Users.View);

        var tokens = await _userAuthenticationTokenRepository.GetUserAuthenticationTokensWithDetailsForUser(
            currentUserId,
            cancellationToken);
        return _mapper.Map<List<UserAuthenticationTokenListDto>>(tokens);
    }
}
