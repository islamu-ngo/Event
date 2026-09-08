using Explore.Application.DTOs.RegistrationScope;
using MediatR;

namespace Explore.Application.Features.RegistrationScopes.Requests.Queries;

public sealed record GetRegistrationScopeListRequest : IRequest<List<RegistrationScopeListDto>>
{
}
