using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.RegistrationScope;
using Explore.Application.Features.RegistrationScopes.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.RegistrationScopes.Handlers.Queries;

public class GetRegistrationScopeListRequestHandler : IRequestHandler<GetRegistrationScopeListRequest, List<RegistrationScopeListDto>>
{
    private readonly IRegistrationScopeRepository _registrationScopeRepository;
    private readonly IMapper _mapper;

    public GetRegistrationScopeListRequestHandler(IRegistrationScopeRepository registrationScopeRepository, IMapper mapper)
    {
        _registrationScopeRepository = registrationScopeRepository;
        _mapper = mapper;
    }

    public async Task<List<RegistrationScopeListDto>> Handle(GetRegistrationScopeListRequest request, CancellationToken cancellationToken)
    {
        var registrationScopes = await _registrationScopeRepository.GetAll();
        return _mapper.Map<List<RegistrationScopeListDto>>(registrationScopes);
    }
}
