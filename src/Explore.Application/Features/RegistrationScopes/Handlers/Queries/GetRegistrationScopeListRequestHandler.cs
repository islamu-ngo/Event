using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.RegistrationScope;
using Explore.Application.Features.RegistrationScopes.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.RegistrationScopes.Handlers.Queries;

public class GetRegistrationScopeListRequestHandler : IRequestHandler<GetRegistrationScopeListRequest, List<RegistrationScopeListDto>>
{
    private readonly IRegistrationScopeRepository _registrationScopeRepository;

    public GetRegistrationScopeListRequestHandler(IRegistrationScopeRepository registrationScopeRepository)
    {
        _registrationScopeRepository = registrationScopeRepository;
    }

    public async Task<List<RegistrationScopeListDto>> Handle(GetRegistrationScopeListRequest request, CancellationToken cancellationToken)
    {
        var registrationScopes = await _registrationScopeRepository.GetAll();
        return registrationScopes.Select(RegistrationMapper.ToListItem).ToList();
    }
}
