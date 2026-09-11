using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.RegistrationMode;
using Explore.Application.Features.RegistrationModes.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.RegistrationModes.Handlers.Queries;

public class GetRegistrationModeListRequestHandler : IRequestHandler<GetRegistrationModeListRequest, List<RegistrationModeListDto>>
{
    private readonly IRegistrationModeRepository _registrationModeRepository;

    public GetRegistrationModeListRequestHandler(IRegistrationModeRepository registrationModeRepository)
    {
        _registrationModeRepository = registrationModeRepository;
    }

    public async Task<List<RegistrationModeListDto>> Handle(GetRegistrationModeListRequest request, CancellationToken cancellationToken)
    {
        var registrationModes = await _registrationModeRepository.GetAll();
        return registrationModes.Select(RegistrationMapper.ToListItem).ToList();
    }
}
