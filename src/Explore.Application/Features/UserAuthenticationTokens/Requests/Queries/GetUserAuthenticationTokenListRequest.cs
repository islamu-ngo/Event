using System.Collections.Generic;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.UserAuthenticationToken;

namespace Explore.Application.Features.UserAuthenticationTokens.Requests.Queries;

public sealed record GetUserAuthenticationTokenListRequest : IQuery<List<UserAuthenticationTokenListDto>>
{
}
