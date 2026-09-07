using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.LocationRoom;
using Explore.Application.DTOs.LocationRoom.Validators;
using Explore.Application.Exceptions;
using Explore.Application.Features.LocationRooms.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using MediatR;

namespace Explore.Application.Features.LocationRooms.Handlers.Commands;

public class UpdateLocationRoomCommandHandler : IRequestHandler<UpdateLocationRoomCommand, BaseCommandResponse<Guid>>
{
    private readonly ILocationRoomRepository _locationRoomRepository;
    private readonly ILocationRepository _locationRepository;

    public UpdateLocationRoomCommandHandler(
        ILocationRoomRepository locationRoomRepository,
        ILocationRepository locationRepository)
    {
        _locationRoomRepository = locationRoomRepository;
        _locationRepository = locationRepository;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(UpdateLocationRoomCommand request, CancellationToken cancellationToken)
    {
        var validator = new UpdateLocationRoomDtoValidator(_locationRepository);
        var validationResult = await validator.ValidateAsync(request.UpdateLocationRoomDto, cancellationToken);

        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(e => e.ErrorMessage),
                "Room update failed.");
        }

        var room = await _locationRoomRepository.GetById(request.LocationRoomId);
        if (room == null)
        {
            return BaseCommandResponse.NotFound<Guid>("Room not found.");
        }

        if (room.ConcurrencyStamp != request.ExpectedConcurrencyStamp)
        {
            throw new ConcurrencyConflictException(
                ConcurrencyConflictException.ConcurrentUpdate,
                "The location room was modified by another request. Reload and retry.",
                nameof(LocationRoom),
                room.Id.ToString());
        }

        if (request.UpdateLocationRoomDto.Location is not null)
        {
            var parentLocation = await _locationRepository.GetById(request.UpdateLocationRoomDto.Location.LocationId);
            if (parentLocation == null || parentLocation.TenantId != room.TenantId)
            {
                return BaseCommandResponse.Validation<Guid>(
                    ["Location does not belong to the same tenant as the room."],
                    "Location does not belong to the same tenant as the room.");
            }

            if (parentLocation.Id != room.LocationId
                && await _locationRoomRepository.HasActiveScheduleReferencesAsync(
                    room.Id,
                    cancellationToken))
            {
                return BaseCommandResponse.Validation<Guid>(
                    ["A room used by an event schedule cannot be moved to another location."],
                    "A room used by an event schedule cannot be moved to another location.");
            }
        }

        ApplyLocation(room, request.UpdateLocationRoomDto.Location);
        ApplyName(room, request.UpdateLocationRoomDto.Name);
        ApplySlug(room, request.UpdateLocationRoomDto.Slug);
        ApplyDescription(room, request.UpdateLocationRoomDto.Description);
        ApplyCapacity(room, request.UpdateLocationRoomDto.Capacity);
        ApplySortOrder(room, request.UpdateLocationRoomDto.SortOrder);

        await _locationRoomRepository.Update(room);

        return BaseCommandResponse.Success(room.Id, "Room updated successfully.");
    }

    private static void ApplyLocation(LocationRoom room, UpdateLocationRoomLocationDto? group)
    {
        if (group is not null)
        {
            room.LocationId = group.LocationId;
        }
    }

    private static void ApplyName(LocationRoom room, UpdateLocationRoomNameDto? group)
    {
        if (group is not null)
        {
            room.Name = group.Value;
        }
    }

    private static void ApplySlug(LocationRoom room, UpdateLocationRoomSlugDto? group)
    {
        if (group?.Value.HasValue == true)
        {
            room.Slug = group.Value.Value;
        }
    }

    private static void ApplyDescription(LocationRoom room, UpdateLocationRoomDescriptionDto? group)
    {
        if (group?.Value.HasValue == true)
        {
            room.Description = group.Value.Value;
        }
    }

    private static void ApplyCapacity(LocationRoom room, UpdateLocationRoomCapacityDto? group)
    {
        if (group?.Value.HasValue == true)
        {
            room.Capacity = group.Value.Value;
        }
    }

    private static void ApplySortOrder(LocationRoom room, UpdateLocationRoomSortOrderDto? group)
    {
        if (group is not null)
        {
            room.SortOrder = group.Value;
        }
    }
}
