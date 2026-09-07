using Explore.Application.Caching;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSessionAgendaItem.Validators;
using Explore.Application.Features.EventSessionAgendaItems.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Domain;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.EventSessionAgendaItems.Handlers.Commands;

public class UpdateEventSessionAgendaItemCommandHandler : IRequestHandler<UpdateEventSessionAgendaItemCommand, BaseCommandResponse<Guid>>
{
    private readonly IEventSessionAgendaItemRepository _agendaItemRepository;
    private readonly IEventSessionRepository _eventSessionRepository;
    private readonly ILocationRepository _locationRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly EventLocationAttachmentService _eventLocationAttachmentService;
    private readonly HybridCache _cache;

    public UpdateEventSessionAgendaItemCommandHandler(
        IEventSessionAgendaItemRepository agendaItemRepository,
        IEventSessionRepository eventSessionRepository,
        ILocationRepository locationRepository,
        IUnitOfWork unitOfWork,
        EventLocationAttachmentService eventLocationAttachmentService,
        HybridCache cache)
    {
        _agendaItemRepository = agendaItemRepository;
        _eventSessionRepository = eventSessionRepository;
        _locationRepository = locationRepository;
        _unitOfWork = unitOfWork;
        _eventLocationAttachmentService = eventLocationAttachmentService;
        _cache = cache;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(UpdateEventSessionAgendaItemCommand request, CancellationToken cancellationToken)
    {
        var validator = new UpdateEventSessionAgendaItemDtoValidator();
        var validationResult = await validator.ValidateAsync(request.AgendaItemDto, cancellationToken);

        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(e => e.ErrorMessage),
                "Agenda item update failed.");
        }

        var agendaItem = await _agendaItemRepository.GetById(request.EventSessionAgendaItemId);

        if (agendaItem == null)
        {
            return BaseCommandResponse.NotFound<Guid>("Agenda item not found.");
        }

        EventSession? currentSession = await _eventSessionRepository.GetById(agendaItem.EventSessionId);
        Guid destinationSessionId = request.AgendaItemDto.Relationship?.EventSessionId ?? agendaItem.EventSessionId;
        EventSession? parentSession = destinationSessionId == currentSession?.Id
            ? currentSession
            : await _eventSessionRepository.GetById(destinationSessionId);
        if (currentSession is null || parentSession is null || parentSession.TenantId != agendaItem.TenantId)
        {
            return BaseCommandResponse.NotFound<Guid>("Event session not found in the current tenant.");
        }

        request = request with
        {
            EventSessionId = agendaItem.EventSessionId,
            EventId = currentSession.EventId,
            TenantId = agendaItem.TenantId,
        };

        if (parentSession.EventId != currentSession.EventId)
        {
            return BaseCommandResponse.Validation<Guid>(
                ["Agenda items cannot move to a session from another event."],
                "Agenda items cannot move to a session from another event.");
        }

        string title = request.AgendaItemDto.Content?.Title ?? agendaItem.Title;
        DateTimeOffset startTime = request.AgendaItemDto.Schedule?.StartTime ?? agendaItem.StartTime;
        DateTimeOffset endTime = request.AgendaItemDto.Schedule?.EndTime ?? agendaItem.EndTime;
        if (string.IsNullOrWhiteSpace(title) || endTime <= startTime)
        {
            return BaseCommandResponse.Validation<Guid>(
                [string.IsNullOrWhiteSpace(title)
                    ? "Title is required."
                    : "EndTime must be after StartTime."],
                "Agenda item update failed.");
        }

        Guid? locationId = request.AgendaItemDto.Location?.Value.HasValue == true
            ? request.AgendaItemDto.Location.Value.Value
            : agendaItem.LocationId;
        if (locationId.HasValue)
        {
            Location? location = await _locationRepository.GetById(locationId.Value);
            if (location is null || location.TenantId != agendaItem.TenantId)
            {
                return BaseCommandResponse.NotFound<Guid>("Location was not found in the current tenant.");
            }
        }

        Guid? previousEventLocationId = agendaItem.EventLocationId;
        agendaItem.EventSessionId = parentSession.Id;
        agendaItem.Title = title;
        agendaItem.StartTime = startTime;
        agendaItem.EndTime = endTime;
        if (request.AgendaItemDto.Content?.Description.HasValue == true)
            agendaItem.Description = request.AgendaItemDto.Content.Description.Value;
        agendaItem.TenantId = parentSession.TenantId;
        agendaItem.EventSession = parentSession;

        await _unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            EventLocation eventLocation = await _eventLocationAttachmentService.ResolveAsync(
                parentSession.EventId,
                locationId,
                previousEventLocationId,
                token);
            agendaItem.AssignEventLocation(eventLocation);
            await _agendaItemRepository.Update(agendaItem);
            await _eventLocationAttachmentService.DetachIfUnreferencedAsync(previousEventLocationId, token);
        }, cancellationToken);

        await _cache.RemoveAsync($"event:detail:{parentSession.EventId}", cancellationToken);
        await _cache.RemoveByTagAsync(CacheTags.EventListByTenant(parentSession.TenantId), cancellationToken);

        return BaseCommandResponse.Success(agendaItem.Id, "Agenda item updated successfully.");
    }
}
