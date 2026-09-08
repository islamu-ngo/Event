using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.RegistrationMode;
using Explore.Application.Features.RegistrationModes.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.RegistrationModes.Handlers.Queries;

public class GetRegistrationModeDetailsRequestHandler : IRequestHandler<GetRegistrationModeDetailsRequest, RegistrationModeDto>
{
    private readonly IRegistrationModeRepository _registrationModeRepository;
    private readonly IMapper _mapper;

    public GetRegistrationModeDetailsRequestHandler(IRegistrationModeRepository registrationModeRepository, IMapper mapper)
    {
        _registrationModeRepository = registrationModeRepository;
        _mapper = mapper;
    }

    public async Task<RegistrationModeDto> Handle(GetRegistrationModeDetailsRequest request, CancellationToken cancellationToken)
    {
        var registrationMode = await _registrationModeRepository.GetById(request.Id);
        return _mapper.Map<RegistrationModeDto>(registrationMode);
    }
}
