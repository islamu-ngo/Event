using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Location;
using Explore.Application.Features.Locations.Requests.Queries;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Locations.Handlers.Queries;

public class GetLocationListRequestHandler : IQueryHandler<GetLocationListRequest, PaginatedResult<LocationListDto>>
{
    private readonly ILocationRepository _locationRepository;

    public GetLocationListRequestHandler(
        ILocationRepository locationRepository)
    {
        _locationRepository = locationRepository;
    }

    public async Task<PaginatedResult<LocationListDto>> QueryAsync(GetLocationListRequest request, CancellationToken cancellationToken)
    {
        var (pageNumber, pageSize) = PaginatedResult<LocationListDto>.NormalizeParameters(request.PageNumber, request.PageSize);
        var (locations, totalCount) = await _locationRepository.GetLocationsWithDetailsPaged(pageNumber, pageSize, cancellationToken);
        var dtos = locations.Select(LocationMapper.ToListItem).ToList();
        return PaginatedResult<LocationListDto>.Create(dtos, totalCount, pageNumber, pageSize);
    }
}
