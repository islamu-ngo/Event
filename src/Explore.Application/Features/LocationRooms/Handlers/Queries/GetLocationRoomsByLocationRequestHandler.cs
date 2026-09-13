using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.LocationRoom;
using Explore.Application.Features.LocationRooms.Requests.Queries;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.LocationRooms.Handlers.Queries;

public class GetLocationRoomsByLocationRequestHandler : IQueryHandler<GetLocationRoomsByLocationRequest, List<LocationRoomListDto>>
{
    private readonly ILocationRoomRepository _locationRoomRepository;

    public GetLocationRoomsByLocationRequestHandler(
        ILocationRoomRepository locationRoomRepository)
    {
        _locationRoomRepository = locationRoomRepository;
    }

    public async Task<List<LocationRoomListDto>> QueryAsync(GetLocationRoomsByLocationRequest request, CancellationToken cancellationToken)
    {
        var rooms = await _locationRoomRepository.GetByLocationAsync(request.LocationId, cancellationToken);
        return rooms.Select(LocationRoomMapper.ToListItem).ToList();
    }
}
