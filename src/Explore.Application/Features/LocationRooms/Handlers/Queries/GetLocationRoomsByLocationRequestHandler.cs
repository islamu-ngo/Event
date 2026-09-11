using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.LocationRoom;
using Explore.Application.Features.LocationRooms.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.LocationRooms.Handlers.Queries;

public class GetLocationRoomsByLocationRequestHandler : IRequestHandler<GetLocationRoomsByLocationRequest, List<LocationRoomListDto>>
{
    private readonly ILocationRoomRepository _locationRoomRepository;

    public GetLocationRoomsByLocationRequestHandler(
        ILocationRoomRepository locationRoomRepository)
    {
        _locationRoomRepository = locationRoomRepository;
    }

    public async Task<List<LocationRoomListDto>> Handle(GetLocationRoomsByLocationRequest request, CancellationToken cancellationToken)
    {
        var rooms = await _locationRoomRepository.GetByLocationAsync(request.LocationId, cancellationToken);
        return rooms.Select(LocationRoomMapper.ToListItem).ToList();
    }
}
