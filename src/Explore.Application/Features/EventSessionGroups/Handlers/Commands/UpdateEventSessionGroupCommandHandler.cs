using Explore.Application.Caching;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSessionGroup.Validators;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventSessionGroups.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Domain;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.EventSessionGroups.Handlers.Commands;

public class UpdateEventSessionGroupCommandHandler : ICommandHandler<UpdateEventSessionGroupCommand, BaseCommandResponse<Guid>>
{
    private readonly IEventSessionGroupRepository _eventSessionGroupRepository;
    private readonly IEventRepository _eventRepository;
    private readonly ILocationRepository _locationRepository;
    private readonly ILocationRoomRepository _locationRoomRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly EventLocationAttachmentService _eventLocationAttachmentService;
    private readonly HybridCache _cache;

    public UpdateEventSessionGroupCommandHandler(
        IEventSessionGroupRepository eventSessionGroupRepository,
        IEventRepository eventRepository,
        ILocationRepository locationRepository,
        ILocationRoomRepository locationRoomRepository,
        IUnitOfWork unitOfWork,
        EventLocationAttachmentService eventLocationAttachmentService,
        HybridCache cache)
    {
        _eventSessionGroupRepository = eventSessionGroupRepository;
        _eventRepository = eventRepository;
        _locationRepository = locationRepository;
        _locationRoomRepository = locationRoomRepository;
        _unitOfWork = unitOfWork;
        _eventLocationAttachmentService = eventLocationAttachmentService;
        _cache = cache;
    }

    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(UpdateEventSessionGroupCommand command, CancellationToken cancellationToken = default)
    {
        var validator = new UpdateEventSessionGroupRequestDtoValidator();
        var validationResult = await validator.ValidateAsync(command.EventSessionGroup, cancellationToken);

        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(error => error.ErrorMessage),
                "Event session group update failed.");
        }

        var group = await _eventSessionGroupRepository.GetForUpdateAsync(command.EventSessionGroupId, cancellationToken);
        if (group is null)
        {
            return BaseCommandResponse.NotFound<Guid>("Event session group not found.");
        }

        command = command with
        {
            EventId = group.EventId,
            TenantId = group.TenantId,
        };

        if (group.ConcurrencyStamp != command.ExpectedConcurrencyStamp)
        {
            throw new ConcurrencyConflictException(
                ConcurrencyConflictException.ConcurrentUpdate,
                "The event session group was modified by another request. Reload and retry.",
                nameof(EventSessionGroup),
                group.Id.ToString());
        }

        Event? parentEvent = await _eventRepository.GetById(group.EventId);
        if (parentEvent is null || parentEvent.TenantId != group.TenantId)
            return ValidationFailure("Event session group parent event was not found in the current tenant.");

        string name = command.EventSessionGroup.Metadata?.Name ?? group.Name;
        string? slug = command.EventSessionGroup.Metadata?.Slug.HasValue == true
            ? command.EventSessionGroup.Metadata.Slug.Value
            : group.Slug;
        Guid? locationId = command.EventSessionGroup.Placement?.LocationId.HasValue == true
            ? command.EventSessionGroup.Placement.LocationId.Value
            : group.LocationId;
        Guid? roomId = command.EventSessionGroup.Placement?.RoomId.HasValue == true
            ? command.EventSessionGroup.Placement.RoomId.Value
            : group.RoomId;

        if (string.IsNullOrWhiteSpace(name))
            return ValidationFailure("Event session group name is required.");

        if (locationId.HasValue)
        {
            Location? location = await _locationRepository.GetById(locationId.Value);
            if (location is null || location.TenantId != group.TenantId)
                return ValidationFailure("Location does not belong to the current tenant.");
        }

        if (roomId.HasValue)
        {
            LocationRoom? room = await _locationRoomRepository.GetById(roomId.Value);
            if (room is null || !locationId.HasValue || room.LocationId != locationId.Value)
                return ValidationFailure("Room must belong to the selected location.");
        }

        if (await SlugExistsForEventAsync(
                group.EventId,
                slug,
                group.Id,
                cancellationToken))
        {
            return BaseCommandResponse.Validation<Guid>(
                ["Slug must be unique within the event."],
                "Event session group update failed.");
        }

        Guid? previousEventLocationId = group.EventLocationId;
        group.Name = name;
        group.Slug = slug;
        if (command.EventSessionGroup.Metadata?.Description.HasValue == true)
            group.Description = command.EventSessionGroup.Metadata.Description.Value;
        if (command.EventSessionGroup.Metadata?.Color.HasValue == true)
            group.Color = command.EventSessionGroup.Metadata.Color.Value;
        if (command.EventSessionGroup.Ordering?.SortOrder is { } sortOrder)
            group.SortOrder = sortOrder;
        if (command.EventSessionGroup.Publication?.IsPublished is { } isPublished)
            group.IsPublished = isPublished;

        await _unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            if (command.EventSessionGroup.Placement?.LocationId.HasValue == true)
            {
                EventLocation eventLocation = await _eventLocationAttachmentService.ResolveAsync(
                    group.EventId,
                    locationId,
                    previousEventLocationId,
                    token);
                group.AssignEventLocation(eventLocation);
            }
            if (command.EventSessionGroup.Placement?.RoomId.HasValue == true)
                group.RoomId = roomId;
            await _eventSessionGroupRepository.Update(group);
            if (command.EventSessionGroup.Placement?.LocationId.HasValue == true)
                await _eventLocationAttachmentService.DetachIfUnreferencedAsync(previousEventLocationId, token);
        }, cancellationToken);

        await _cache.RemoveAsync($"event:detail:{group.EventId}", cancellationToken);
        await _cache.RemoveByTagAsync(CacheTags.EventListByTenant(group.TenantId), cancellationToken);

        return BaseCommandResponse.Success(group.Id, "Event session group updated successfully.");
    }

    private static BaseCommandResponse<Guid> ValidationFailure(string message) =>
        BaseCommandResponse.Validation<Guid>([message], message);

    private async Task<bool> SlugExistsForEventAsync(Guid eventId, string? slug, Guid currentGroupId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(slug))
            return false;

        var groups = await _eventSessionGroupRepository.GetActiveByEventAsync(eventId, cancellationToken);
        return groups.Any(group => group.Id != currentGroupId
            && string.Equals(group.Slug, slug.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}
