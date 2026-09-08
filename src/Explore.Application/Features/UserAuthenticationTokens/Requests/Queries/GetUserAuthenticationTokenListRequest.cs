using System.Collections.Generic;
using Explore.Application.DTOs.UserAuthenticationToken;
using MediatR;

namespace Explore.Application.Features.UserAuthenticationTokens.Requests.Queries;

public sealed record GetUserAuthenticationTokenListRequest : IRequest<List<UserAuthenticationTokenListDto>>
{
}
