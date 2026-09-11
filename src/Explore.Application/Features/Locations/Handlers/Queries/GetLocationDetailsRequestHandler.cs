using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Location;
using Explore.Application.Features.Locations.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.Locations.Handlers.Queries;

public class GetLocationDetailsRequestHandler : IRequestHandler<GetLocationDetailsRequest, LocationDto>
{
    private readonly ILocationRepository _locationRepository;

    public GetLocationDetailsRequestHandler(
        ILocationRepository locationRepository)
    {
        _locationRepository = locationRepository;
    }

    public async Task<LocationDto> Handle(GetLocationDetailsRequest request, CancellationToken cancellationToken)
    {
        var location = await _locationRepository.GetById(request.Id);
        return LocationMapper.ToDetail(location)!;
    }
}
