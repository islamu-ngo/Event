using Explore.Application.Caching;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventAgendaItem;
using Explore.Application.DTOs.EventAgendaItem.Validators;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventAgendaItems.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Services.Scheduling;
using Explore.Domain.ValueObjects;
using Explore.Application.Contracts.Operations;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.EventAgendaItems.Handlers.Commands;

public class UpdateEventAgendaItemCommandHandler : ICommandHandler<UpdateEventAgendaItemCommand, BaseCommandResponse<Guid>>
{
    private readonly IEventAgendaItemRepository _eventAgendaItemRepository;
    private readonly IEventRepository _eventRepository;
    private readonly IEventDayRepository _eventDayRepository;
    private readonly ILocationRepository _locationRepository;
    private readonly ILocationRoomRepository _locationRoomRepository;
    private readonly IScheduleItemKindRepository _scheduleItemKindRepository;
    private readonly IEventScheduleProjectionCalculator _scheduleProjectionCalculator;
    private readonly IUnitOfWork _unitOfWork;
    private readonly EventLocationAttachmentService _eventLocationAttachmentService;
    private readonly HybridCache _cache;
    private readonly IAuthorizationProvider _authorizationProvider;
    private readonly AuthorizationResourceContextResolver _authorizationResourceContextResolver;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserService _currentUser;
    private readonly RequestAuthorization<UpdateEventAgendaItemCommand> _authorization;
    private readonly ILogger<UpdateEventAgendaItemCommandHandler> _logger;

    public UpdateEventAgendaItemCommandHandler(
        IEventAgendaItemRepository eventAgendaItemRepository,
        IEventRepository eventRepository,
        IEventDayRepository eventDayRepository,
        ILocationRepository locationRepository,
        ILocationRoomRepository locationRoomRepository,
        IScheduleItemKindRepository scheduleItemKindRepository,
        IEventScheduleProjectionCalculator scheduleProjectionCalculator,
        IUnitOfWork unitOfWork,
        EventLocationAttachmentService eventLocationAttachmentService,
        HybridCache cache,
        IAuthorizationProvider authorizationProvider,
        AuthorizationResourceContextResolver authorizationResourceContextResolver,
        ITenantContext tenantContext,
        ICurrentUserService currentUser,
        RequestAuthorization<UpdateEventAgendaItemCommand> authorization,
        ILogger<UpdateEventAgendaItemCommandHandler> logger)
    {
        _eventAgendaItemRepository = eventAgendaItemRepository;
        _eventRepository = eventRepository;
        _eventDayRepository = eventDayRepository;
        _locationRepository = locationRepository;
        _locationRoomRepository = locationRoomRepository;
        _scheduleItemKindRepository = scheduleItemKindRepository;
        _scheduleProjectionCalculator = scheduleProjectionCalculator;
        _unitOfWork = unitOfWork;
        _eventLocationAttachmentService = eventLocationAttachmentService;
        _cache = cache;
        _authorizationProvider = authorizationProvider;
        _authorizationResourceContextResolver = authorizationResourceContextResolver;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _authorization = authorization;
        _logger = logger;
    }

    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(UpdateEventAgendaItemCommand request, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is not { } userId)
            return BaseCommandResponse.Authentication<Guid>();
        var validator = new UpdateEventAgendaItemDtoValidator(
            _eventRepository,
            _locationRepository,
            _locationRoomRepository,
            _scheduleItemKindRepository);
        var validationResult = await validator.ValidateAsync(request.EventAgendaItemDto, cancellationToken);

        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(e => e.ErrorMessage),
                "Event agenda item update failed.");
        }

        Guid previousEventId = Guid.Empty;
        Guid parentEventId = Guid.Empty;
        var response = await _unitOfWork.ExecuteSerializableAsync(async token =>
        {
            var candidate = await _eventAgendaItemRepository.GetById(request.EventAgendaItemId);
            if (candidate is null)
                return BaseCommandResponse.NotFound<Guid>("Event agenda item not found.");
            previousEventId = candidate.EventId;
            parentEventId = request.EventAgendaItemDto.Event?.EventId ?? candidate.EventId;
            bool eventChanged = previousEventId != parentEventId;
            await _authorization.AuthorizeAsync(request, _logger, token);
            if (eventChanged)
                await AuthorizeDestinationAsync(request, parentEventId, token);

            Event? parentEvent = null;
            foreach (Guid eventId in new[] { previousEventId, parentEventId }.Distinct().Order())
            {
                Event? authorized = eventId == previousEventId
                    ? await _eventAgendaItemRepository.GetAuthorizedParentForMutationAsync(
                        _tenantContext.TenantId, userId, eventId, AuthorizationActions.Update, token)
                    : await _eventRepository.GetAuthorizedForUpdateAsync(
                        _tenantContext.TenantId, userId, eventId, token);
                if (authorized is null)
                    throw new AuthorizationException(ResourceKinds.EventAgendaItem, AuthorizationActions.Update);
                if (eventId == parentEventId)
                    parentEvent = authorized;
            }
            var agendaItem = await _eventAgendaItemRepository.GetForMutationAsync(request.EventAgendaItemId, token);
            if (agendaItem is null || agendaItem.EventId != previousEventId || parentEvent is null
                || agendaItem.TenantId != _tenantContext.TenantId)
                throw new AuthorizationException(ResourceKinds.EventAgendaItem, AuthorizationActions.Update);
            if (agendaItem.ConcurrencyStamp != request.ExpectedConcurrencyStamp)
                throw new ConcurrencyConflictException(
                    ConcurrencyConflictException.ConcurrentUpdate,
                    "The event agenda item was modified by another request. Reload and retry.",
                    nameof(EventAgendaItem), agendaItem.Id.ToString());
            var relationshipValidation = await ValidateLocationRoomRelationshipAsync(agendaItem, request.EventAgendaItemDto, token);
            if (!relationshipValidation.Success)
                return BaseCommandResponse.Validation<Guid>([relationshipValidation.Message], "Event agenda item update failed.");
            Guid? previousEventLocationId = agendaItem.EventLocationId;
            EventLocation eventLocation = await _eventLocationAttachmentService.ResolveAsync(
                parentEvent.Id,
                relationshipValidation.LocationId,
                previousEventLocationId,
                token);
            if (eventChanged)
            {
                await _eventAgendaItemRepository.MoveToEventAsync(
                    agendaItem,
                    parentEvent.Id,
                    eventLocation,
                    relationshipValidation.RoomId,
                    token);
            }
            else
            {
                agendaItem.AssignEventLocation(eventLocation);
                if (request.EventAgendaItemDto.Location is not null || request.EventAgendaItemDto.Room is not null)
                {
                    agendaItem.RoomId = relationshipValidation.RoomId;
                }
            }

            ApplyTitle(agendaItem, request.EventAgendaItemDto.Title);
            ApplyDescription(agendaItem, request.EventAgendaItemDto.Description);
            ApplyKind(agendaItem, request.EventAgendaItemDto.Kind);
            ApplySortOrder(agendaItem, request.EventAgendaItemDto.SortOrder);
            await ApplyScheduleAsync(agendaItem, parentEvent, eventChanged, request.EventAgendaItemDto.Schedule, token);
            await _eventAgendaItemRepository.Update(agendaItem);
            await _eventLocationAttachmentService.DetachIfUnreferencedAsync(previousEventLocationId, token);
            token.ThrowIfCancellationRequested();
            return BaseCommandResponse.Success(agendaItem.Id, "Event agenda item updated successfully.");
        }, cancellationToken);

        if (!response.IsSuccess)
            return response;
        if (previousEventId != parentEventId)
        {
            await _cache.RemoveByTagAsync(CacheTags.Event(previousEventId), cancellationToken);
        }

        await _cache.RemoveByTagAsync(CacheTags.Event(parentEventId), cancellationToken);
        await _cache.RemoveByTagAsync(CacheTags.EventListByTenant(_tenantContext.TenantId), cancellationToken);

        return response;
    }

    private async Task AuthorizeDestinationAsync(
        UpdateEventAgendaItemCommand request,
        Guid eventId,
        CancellationToken cancellationToken)
    {
        var resourceId = eventId.ToString();
        var context = await _authorizationResourceContextResolver.ResolveAsync(
            request, ResourceKinds.Event, AuthorizationActions.Update, resourceId, null, cancellationToken);
        if (context.Facts is null)
        {
            throw new AuthorizationException(ResourceKinds.Event, AuthorizationActions.Update);
        }

        var decision = await _authorizationProvider.AuthorizeAsync(new AuthorizationRequest(
            AuthorizationCapabilityCatalog.Require(ResourceKinds.Event, AuthorizationActions.Update),
            resourceId,
            Facts: context.Facts), cancellationToken);
        if (!decision.IsAllowed)
        {
            if (decision.ReasonCode == AuthorizationDecisionReasonCodes.ProviderUnavailable)
            {
                throw new AuthorizationProviderUnavailableException(ResourceKinds.Event, AuthorizationActions.Update);
            }

            throw new AuthorizationException(ResourceKinds.Event, AuthorizationActions.Update);
        }
    }

    private async Task<(bool Success, string Message, Guid? LocationId, Guid? RoomId)> ValidateLocationRoomRelationshipAsync(
        EventAgendaItem agendaItem,
        UpdateEventAgendaItemDto dto,
        CancellationToken cancellationToken)
    {
        var finalLocationId = dto.Location?.Value.HasValue == true ? dto.Location.Value.Value : agendaItem.LocationId;
        var finalRoomId = dto.Room?.Value.HasValue == true ? dto.Room.Value.Value : agendaItem.RoomId;

        if (finalLocationId.HasValue)
        {
            var location = await _locationRepository.GetById(finalLocationId.Value);
            if (location == null || location.TenantId != agendaItem.TenantId)
            {
                return (false, "Location does not belong to the same tenant as the agenda item.", finalLocationId, finalRoomId);
            }
        }

        if (finalRoomId.HasValue)
        {
            var room = await _locationRoomRepository.GetById(finalRoomId.Value);
            if (room == null || room.TenantId != agendaItem.TenantId)
            {
                return (false, "Room does not belong to the same tenant as the agenda item.", finalLocationId, finalRoomId);
            }

            if (!finalLocationId.HasValue)
            {
                finalLocationId = room.LocationId;
            }
            else if (finalLocationId.Value != room.LocationId)
            {
                return (false, "Room must belong to the selected location.", finalLocationId, finalRoomId);
            }
        }

        return (true, string.Empty, finalLocationId, finalRoomId);
    }

    private async Task ApplyScheduleAsync(
        EventAgendaItem agendaItem,
        Event parentEvent,
        bool eventChanged,
        UpdateEventAgendaItemScheduleDto? group,
        CancellationToken cancellationToken)
    {
        if (group is not null)
        {
            agendaItem.Reschedule(
                UtcInstantRange.Create(group.StartTime, group.EndTime),
                parentEvent.EventTimeZoneId ?? parentEvent.Timezone ?? string.Empty,
                _scheduleProjectionCalculator);
            await RelinkEventDayAsync(agendaItem, parentEvent.Id, cancellationToken);
            return;
        }

        if (!eventChanged)
        {
            return;
        }

        agendaItem.ReprojectLocalTimes(parentEvent.EventTimeZoneId ?? parentEvent.Timezone ?? string.Empty, _scheduleProjectionCalculator);
        await RelinkEventDayAsync(agendaItem, parentEvent.Id, cancellationToken);
    }

    private async Task RelinkEventDayAsync(EventAgendaItem agendaItem, Guid eventId, CancellationToken cancellationToken)
    {
        var matchingDay = await _eventDayRepository.FindByEventAndLocalDateAsync(
            eventId,
            agendaItem.LocalStartDate,
            cancellationToken);
        agendaItem.EventDayId = matchingDay?.Id;
    }

    private static void ApplyTitle(EventAgendaItem agendaItem, UpdateEventAgendaItemTitleDto? group)
    {
        if (group is not null)
        {
            agendaItem.Title = group.Value;
        }
    }

    private static void ApplyDescription(EventAgendaItem agendaItem, UpdateEventAgendaItemDescriptionDto? group)
    {
        if (group?.Value.HasValue == true)
        {
            agendaItem.Description = group.Value.Value;
        }
    }

    private static void ApplyKind(EventAgendaItem agendaItem, UpdateEventAgendaItemKindDto? group)
    {
        if (group?.Value.HasValue == true)
        {
            agendaItem.KindId = group.Value.Value;
        }
    }

    private static void ApplySortOrder(EventAgendaItem agendaItem, UpdateEventAgendaItemSortOrderDto? group)
    {
        if (group is not null)
        {
            agendaItem.SortOrder = group.Value;
        }
    }
}
