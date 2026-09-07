using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.LocationRoom;
using Explore.Application.Features.LocationRooms.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.LocationRooms.Handlers.Queries;

public class GetLocationRoomsByLocationRequestHandler : IRequestHandler<GetLocationRoomsByLocationRequest, List<LocationRoomListDto>>
{
    private readonly ILocationRoomRepository _locationRoomRepository;
    private readonly IMapper _mapper;

    public GetLocationRoomsByLocationRequestHandler(
        ILocationRoomRepository locationRoomRepository,
        IMapper mapper)
    {
        _locationRoomRepository = locationRoomRepository;
        _mapper = mapper;
    }

    public async Task<List<LocationRoomListDto>> Handle(GetLocationRoomsByLocationRequest request, CancellationToken cancellationToken)
    {
        var rooms = await _locationRoomRepository.GetByLocationAsync(request.LocationId, cancellationToken);
        return _mapper.Map<List<LocationRoomListDto>>(rooms);
    }
}
