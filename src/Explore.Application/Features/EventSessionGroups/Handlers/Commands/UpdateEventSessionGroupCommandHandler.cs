using Explore.Application.Caching;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSessionGroup.Validators;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventSessionGroups.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Domain;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.EventSessionGroups.Handlers.Commands;

public class UpdateEventSessionGroupCommandHandler : IRequestHandler<UpdateEventSessionGroupCommand, BaseCommandResponse<Guid>>
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

    public async Task<BaseCommandResponse<Guid>> Handle(UpdateEventSessionGroupCommand request, CancellationToken cancellationToken)
    {
        var validator = new UpdateEventSessionGroupRequestDtoValidator();
        var validationResult = await validator.ValidateAsync(request.EventSessionGroup, cancellationToken);

        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(error => error.ErrorMessage),
                "Event session group update failed.");
        }

        var group = await _eventSessionGroupRepository.GetForUpdateAsync(request.EventSessionGroupId, cancellationToken);
        if (group is null)
        {
            return BaseCommandResponse.NotFound<Guid>("Event session group not found.");
        }

        request = request with
        {
            EventId = group.EventId,
            TenantId = group.TenantId,
        };

        if (group.ConcurrencyStamp != request.ExpectedConcurrencyStamp)
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

        string name = request.EventSessionGroup.Metadata?.Name ?? group.Name;
        string? slug = request.EventSessionGroup.Metadata?.Slug.HasValue == true
            ? request.EventSessionGroup.Metadata.Slug.Value
            : group.Slug;
        Guid? locationId = request.EventSessionGroup.Placement?.LocationId.HasValue == true
            ? request.EventSessionGroup.Placement.LocationId.Value
            : group.LocationId;
        Guid? roomId = request.EventSessionGroup.Placement?.RoomId.HasValue == true
            ? request.EventSessionGroup.Placement.RoomId.Value
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
        if (request.EventSessionGroup.Metadata?.Description.HasValue == true)
            group.Description = request.EventSessionGroup.Metadata.Description.Value;
        if (request.EventSessionGroup.Metadata?.Color.HasValue == true)
            group.Color = request.EventSessionGroup.Metadata.Color.Value;
        if (request.EventSessionGroup.Ordering?.SortOrder is { } sortOrder)
            group.SortOrder = sortOrder;
        if (request.EventSessionGroup.Publication?.IsPublished is { } isPublished)
            group.IsPublished = isPublished;

        await _unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            if (request.EventSessionGroup.Placement?.LocationId.HasValue == true)
            {
                EventLocation eventLocation = await _eventLocationAttachmentService.ResolveAsync(
                    group.EventId,
                    locationId,
                    previousEventLocationId,
                    token);
                group.AssignEventLocation(eventLocation);
            }
            if (request.EventSessionGroup.Placement?.RoomId.HasValue == true)
                group.RoomId = roomId;
            await _eventSessionGroupRepository.Update(group);
            if (request.EventSessionGroup.Placement?.LocationId.HasValue == true)
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
