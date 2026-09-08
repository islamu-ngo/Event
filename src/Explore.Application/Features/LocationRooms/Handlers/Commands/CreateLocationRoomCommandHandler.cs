using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.LocationRoom.Validators;
using Explore.Application.Features.LocationRooms.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using MediatR;

namespace Explore.Application.Features.LocationRooms.Handlers.Commands;

public class CreateLocationRoomCommandHandler : IRequestHandler<CreateLocationRoomCommand, BaseCommandResponse<Guid>>
{
    private readonly ILocationRoomRepository _locationRoomRepository;
    private readonly ILocationRepository _locationRepository;
    private readonly IMapper _mapper;

    public CreateLocationRoomCommandHandler(
        ILocationRoomRepository locationRoomRepository,
        ILocationRepository locationRepository,
        IMapper mapper)
    {
        _locationRoomRepository = locationRoomRepository;
        _locationRepository = locationRepository;
        _mapper = mapper;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(CreateLocationRoomCommand request, CancellationToken cancellationToken)
    {
        var validator = new CreateLocationRoomDtoValidator(_locationRepository);
        var validationResult = await validator.ValidateAsync(request.LocationRoomDto, cancellationToken);

        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(e => e.ErrorMessage),
                "Room creation failed.");
        }

        var parentLocation = await _locationRepository.GetById(request.LocationRoomDto.LocationId);
        if (parentLocation == null)
        {
            return BaseCommandResponse.Validation<Guid>(
                ["Location not found in the current tenant."],
                "Location not found in the current tenant.");
        }

        var room = _mapper.Map<LocationRoom>(request.LocationRoomDto);
        room.TenantId = parentLocation.TenantId;

        room = await _locationRoomRepository.Create(room);

        return BaseCommandResponse.Success(room.Id, "Room created successfully.");
    }
}
