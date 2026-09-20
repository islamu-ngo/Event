using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.UserAuthenticationToken;

namespace Explore.Application.Features.UserAuthenticationTokens.Requests.Queries;

public sealed record GetUserAuthenticationTokenDetailsRequest(Guid Id = default) : IQuery<UserAuthenticationTokenDto?>;
