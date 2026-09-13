using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.LocationRoom;
using Explore.Application.Features.LocationRooms.Requests.Queries;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.LocationRooms.Handlers.Queries;

public class GetLocationRoomDetailRequestHandler : IQueryHandler<GetLocationRoomDetailRequest, LocationRoomDto?>
{
    private readonly ILocationRoomRepository _locationRoomRepository;

    public GetLocationRoomDetailRequestHandler(
        ILocationRoomRepository locationRoomRepository)
    {
        _locationRoomRepository = locationRoomRepository;
    }

    public async Task<LocationRoomDto?> QueryAsync(GetLocationRoomDetailRequest request, CancellationToken cancellationToken)
    {
        var room = await _locationRoomRepository.GetById(request.Id);
        if (room == null)
            return null;

        return LocationRoomMapper.ToDetail(room);
    }
}
