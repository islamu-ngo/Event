using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Location;
using Explore.Application.Features.Locations.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.Locations.Handlers.Queries;

public class GetLocationDetailsRequestHandler : IRequestHandler<GetLocationDetailsRequest, LocationDto>
{
    private readonly ILocationRepository _locationRepository;
    private readonly IMapper _mapper;

    public GetLocationDetailsRequestHandler(
        ILocationRepository locationRepository,
        IMapper mapper)
    {
        _locationRepository = locationRepository;
        _mapper = mapper;
    }

    public async Task<LocationDto> Handle(GetLocationDetailsRequest request, CancellationToken cancellationToken)
    {
        var location = await _locationRepository.GetById(request.Id);
        return _mapper.Map<LocationDto>(location);
    }
}
