using Explore.Application.DTOs.UserAuthenticationToken;
using MediatR;

namespace Explore.Application.Features.UserAuthenticationTokens.Requests.Queries;

public sealed record GetUserAuthenticationTokenDetailsRequest(Guid Id = default) : IRequest<UserAuthenticationTokenDto?>;
