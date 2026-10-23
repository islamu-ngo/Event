using Explore.Application.Contracts.Persistence;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Exceptions;
using Explore.Application.DTOs.EventAgendaItem.Validators;
using Explore.Application.Features.EventAgendaItems.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Services.Scheduling;
using Explore.Domain.ValueObjects;
using Explore.Application.Contracts.Operations;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.EventAgendaItems.Handlers.Commands;

public class CreateEventAgendaItemCommandHandler : ICommandHandler<CreateEventAgendaItemCommand, BaseCommandResponse<Guid>>
{
    private readonly IEventAgendaItemRepository _eventAgendaItemRepository;
    private readonly IEventRepository _eventRepository;
    private readonly IEventDayRepository _eventDayRepository;
    private readonly IEventScheduleProjectionCalculator _scheduleProjectionCalculator;
    private readonly IUnitOfWork _unitOfWork;
    private readonly EventLocationAttachmentService _eventLocationAttachmentService;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserService _currentUser;
    private readonly RequestAuthorization<CreateEventAgendaItemCommand> _authorization;
    private readonly ILogger<CreateEventAgendaItemCommandHandler> _logger;

    public CreateEventAgendaItemCommandHandler(
        IEventAgendaItemRepository eventAgendaItemRepository,
        IEventRepository eventRepository,
        IEventDayRepository eventDayRepository,
        IEventScheduleProjectionCalculator scheduleProjectionCalculator,
        IUnitOfWork unitOfWork,
        EventLocationAttachmentService eventLocationAttachmentService,
        ITenantContext tenantContext,
        ICurrentUserService currentUser,
        RequestAuthorization<CreateEventAgendaItemCommand> authorization,
        ILogger<CreateEventAgendaItemCommandHandler> logger)
    {
        _eventAgendaItemRepository = eventAgendaItemRepository;
        _eventRepository = eventRepository;
        _eventDayRepository = eventDayRepository;
        _scheduleProjectionCalculator = scheduleProjectionCalculator;
        _unitOfWork = unitOfWork;
        _eventLocationAttachmentService = eventLocationAttachmentService;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _authorization = authorization;
        _logger = logger;
    }

    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(CreateEventAgendaItemCommand request, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is not { } userId)
            return BaseCommandResponse.Authentication<Guid>();
        var validator = new CreateEventAgendaItemDtoValidator(_eventRepository);
        var validationResult = await validator.ValidateAsync(request.EventAgendaItemDto, cancellationToken);

        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(e => e.ErrorMessage),
                "Event agenda item creation failed.");
        }

        Guid id = Guid.CreateVersion7();
        DateTime createdAt = DateTime.UtcNow;
        return await _unitOfWork.ExecuteSerializableAsync(async token =>
        {
            await _authorization.AuthorizeAsync(request, _logger, token);
            var parentEvent = await _eventAgendaItemRepository.GetAuthorizedParentForMutationAsync(
                _tenantContext.TenantId, userId, request.EventAgendaItemDto.EventId, AuthorizationActions.Create, token);
            if (parentEvent is null)
                throw new AuthorizationException(ResourceKinds.EventAgendaItem, AuthorizationActions.Create);
            var agendaItem = new EventAgendaItem
            {
                Id = id,
                CreatedAt = createdAt,
                CreatedBy = userId,
                EventId = parentEvent.Id,
                TenantId = parentEvent.TenantId,
                Title = request.EventAgendaItemDto.Title,
                Description = request.EventAgendaItemDto.Description,
                LocationId = request.EventAgendaItemDto.LocationId,
                RoomId = request.EventAgendaItemDto.RoomId,
                KindId = request.EventAgendaItemDto.KindId,
                SortOrder = request.EventAgendaItemDto.SortOrder,
                Event = null!,
                Tenant = null!
            };
            agendaItem.Reschedule(
                UtcInstantRange.Create(request.EventAgendaItemDto.StartTime, request.EventAgendaItemDto.EndTime),
                parentEvent.EventTimeZoneId ?? parentEvent.Timezone ?? string.Empty,
                _scheduleProjectionCalculator);
            var matchingDay = await _eventDayRepository.FindByEventAndLocalDateAsync(parentEvent.Id, agendaItem.LocalStartDate, token);
            agendaItem.EventDayId = matchingDay?.Id;
            EventLocation eventLocation = await _eventLocationAttachmentService.ResolveAsync(
                parentEvent.Id,
                agendaItem.LocationId,
                agendaItem.EventLocationId,
                token);
            agendaItem.AssignEventLocation(eventLocation);
            await _eventAgendaItemRepository.Create(agendaItem);
            token.ThrowIfCancellationRequested();
            return BaseCommandResponse.Success(agendaItem.Id, "Event agenda item created successfully.");
        }, cancellationToken);
    }
}
