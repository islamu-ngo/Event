using Explore.Application.DTOs.RegistrationScope;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.RegistrationScopes.Requests.Queries;

public sealed record GetRegistrationScopeListRequest : IQuery<List<RegistrationScopeListDto>>
{
}
