using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Location;
using Explore.Application.Features.Locations.Requests.Queries;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Locations.Handlers.Queries;

public class GetLocationListRequestHandler : IRequestHandler<GetLocationListRequest, PaginatedResult<LocationListDto>>
{
    private readonly ILocationRepository _locationRepository;
    private readonly IMapper _mapper;

    public GetLocationListRequestHandler(
        ILocationRepository locationRepository,
        IMapper mapper)
    {
        _locationRepository = locationRepository;
        _mapper = mapper;
    }

    public async Task<PaginatedResult<LocationListDto>> Handle(GetLocationListRequest request, CancellationToken cancellationToken)
    {
        var (pageNumber, pageSize) = PaginatedResult<LocationListDto>.NormalizeParameters(request.PageNumber, request.PageSize);
        var (locations, totalCount) = await _locationRepository.GetLocationsWithDetailsPaged(pageNumber, pageSize, cancellationToken);
        var dtos = _mapper.Map<List<LocationListDto>>(locations);
        return PaginatedResult<LocationListDto>.Create(dtos, totalCount, pageNumber, pageSize);
    }
}
