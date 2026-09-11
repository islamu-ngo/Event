using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.RegistrationMode;
using Explore.Application.Features.RegistrationModes.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.RegistrationModes.Handlers.Queries;

public class GetRegistrationModeDetailsRequestHandler : IRequestHandler<GetRegistrationModeDetailsRequest, RegistrationModeDto?>
{
    private readonly IRegistrationModeRepository _registrationModeRepository;

    public GetRegistrationModeDetailsRequestHandler(IRegistrationModeRepository registrationModeRepository)
    {
        _registrationModeRepository = registrationModeRepository;
    }

    public async Task<RegistrationModeDto?> Handle(GetRegistrationModeDetailsRequest request, CancellationToken cancellationToken)
    {
        var registrationMode = await _registrationModeRepository.GetById(request.Id);
        return registrationMode is null ? null : RegistrationMapper.ToDetail(registrationMode);
    }
}
