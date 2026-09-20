using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Location;
using Explore.Application.Features.Locations.Requests.Queries;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Locations.Handlers.Queries;

public class GetLocationDetailsRequestHandler : IQueryHandler<GetLocationDetailsRequest, LocationDto?>
{
    private readonly ILocationRepository _locationRepository;

    public GetLocationDetailsRequestHandler(
        ILocationRepository locationRepository)
    {
        _locationRepository = locationRepository;
    }

    public async Task<LocationDto?> QueryAsync(GetLocationDetailsRequest request, CancellationToken cancellationToken)
    {
        var location = await _locationRepository.GetById(request.Id);
        return LocationMapper.ToDetail(location);
    }
}
