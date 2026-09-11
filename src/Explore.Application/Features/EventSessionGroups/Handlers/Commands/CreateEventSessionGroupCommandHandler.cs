using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSessionGroup.Validators;
using Explore.Application.Features.EventSessionGroups.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Domain;
using MediatR;

namespace Explore.Application.Features.EventSessionGroups.Handlers.Commands;

public class CreateEventSessionGroupCommandHandler : IRequestHandler<CreateEventSessionGroupCommand, BaseCommandResponse<Guid>>
{
    private readonly IEventSessionGroupRepository _eventSessionGroupRepository;
    private readonly IEventRepository _eventRepository;
    private readonly ILocationRepository _locationRepository;
    private readonly ILocationRoomRepository _locationRoomRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly EventLocationAttachmentService _eventLocationAttachmentService;

    public CreateEventSessionGroupCommandHandler(
        IEventSessionGroupRepository eventSessionGroupRepository,
        IEventRepository eventRepository,
        ILocationRepository locationRepository,
        ILocationRoomRepository locationRoomRepository,
        IUnitOfWork unitOfWork,
        EventLocationAttachmentService eventLocationAttachmentService)
    {
        _eventSessionGroupRepository = eventSessionGroupRepository;
        _eventRepository = eventRepository;
        _locationRepository = locationRepository;
        _locationRoomRepository = locationRoomRepository;
        _unitOfWork = unitOfWork;
        _eventLocationAttachmentService = eventLocationAttachmentService;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(CreateEventSessionGroupCommand request, CancellationToken cancellationToken)
    {
        var validator = new CreateEventSessionGroupRequestDtoValidator(
            _eventRepository,
            _locationRepository,
            _locationRoomRepository);
        var validationResult = await validator.ValidateAsync(request.EventSessionGroup, cancellationToken);

        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(error => error.ErrorMessage),
                "Event session group creation failed.");
        }

        var parentEvent = await _eventRepository.GetById(request.EventSessionGroup.EventId);
        if (parentEvent is null)
        {
            return BaseCommandResponse.NotFound<Guid>("Event not found in the current tenant.");
        }

        if (await SlugExistsForEventAsync(
                request.EventSessionGroup.EventId,
                request.EventSessionGroup.Slug,
                cancellationToken))
        {
            return BaseCommandResponse.Validation<Guid>(
                ["Slug must be unique within the event."],
                "Event session group creation failed.");
        }

        // Identity, tenant, audit and relationship state are not client-mapped.
        var input = request.EventSessionGroup;
        var group = new EventSessionGroup
        {
            EventId = input.EventId,
            Event = null!,
            Tenant = null!,
            TenantId = parentEvent.TenantId,
            Name = input.Name,
            Slug = input.Slug,
            Description = input.Description,
            LocationId = input.LocationId,
            RoomId = input.RoomId,
            Color = input.Color,
            SortOrder = input.SortOrder,
            IsPublished = input.IsPublished
        };

        group = await _unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            EventLocation eventLocation = await _eventLocationAttachmentService.ResolveAsync(
                parentEvent.Id,
                group.LocationId,
                group.EventLocationId,
                token);
            group.AssignEventLocation(eventLocation);
            return await _eventSessionGroupRepository.Create(group);
        }, cancellationToken);

        return BaseCommandResponse.Success(group.Id, "Event session group created successfully.");
    }

    private async Task<bool> SlugExistsForEventAsync(Guid eventId, string? slug, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(slug))
            return false;

        var groups = await _eventSessionGroupRepository.GetActiveByEventAsync(eventId, cancellationToken);
        return groups.Any(group => string.Equals(group.Slug, slug.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}
