using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.RegistrationMode;
using Explore.Application.Features.RegistrationModes.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.RegistrationModes.Handlers.Queries;

public class GetRegistrationModeListRequestHandler : IRequestHandler<GetRegistrationModeListRequest, List<RegistrationModeListDto>>
{
    private readonly IRegistrationModeRepository _registrationModeRepository;
    private readonly IMapper _mapper;

    public GetRegistrationModeListRequestHandler(IRegistrationModeRepository registrationModeRepository, IMapper mapper)
    {
        _registrationModeRepository = registrationModeRepository;
        _mapper = mapper;
    }

    public async Task<List<RegistrationModeListDto>> Handle(GetRegistrationModeListRequest request, CancellationToken cancellationToken)
    {
        var registrationModes = await _registrationModeRepository.GetAll();
        return _mapper.Map<List<RegistrationModeListDto>>(registrationModes);
    }
}
