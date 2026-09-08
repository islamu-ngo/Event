using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.LocationRoom;
using Explore.Application.Features.LocationRooms.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.LocationRooms.Handlers.Queries;

public class GetLocationRoomDetailRequestHandler : IRequestHandler<GetLocationRoomDetailRequest, LocationRoomDto?>
{
    private readonly ILocationRoomRepository _locationRoomRepository;
    private readonly IMapper _mapper;

    public GetLocationRoomDetailRequestHandler(
        ILocationRoomRepository locationRoomRepository,
        IMapper mapper)
    {
        _locationRoomRepository = locationRoomRepository;
        _mapper = mapper;
    }

    public async Task<LocationRoomDto?> Handle(GetLocationRoomDetailRequest request, CancellationToken cancellationToken)
    {
        var room = await _locationRoomRepository.GetById(request.Id);
        if (room == null)
            return null;

        return _mapper.Map<LocationRoomDto>(room);
    }
}
