using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventAgendaItem.Validators;
using Explore.Application.Features.EventAgendaItems.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Services.Scheduling;
using Explore.Domain.ValueObjects;
using MediatR;

namespace Explore.Application.Features.EventAgendaItems.Handlers.Commands;

public class CreateEventAgendaItemCommandHandler : IRequestHandler<CreateEventAgendaItemCommand, BaseCommandResponse<Guid>>
{
    private readonly IEventAgendaItemRepository _eventAgendaItemRepository;
    private readonly IEventRepository _eventRepository;
    private readonly IEventDayRepository _eventDayRepository;
    private readonly IEventScheduleProjectionCalculator _scheduleProjectionCalculator;
    private readonly IUnitOfWork _unitOfWork;
    private readonly EventLocationAttachmentService _eventLocationAttachmentService;
    private readonly IMapper _mapper;

    public CreateEventAgendaItemCommandHandler(
        IEventAgendaItemRepository eventAgendaItemRepository,
        IEventRepository eventRepository,
        IEventDayRepository eventDayRepository,
        IEventScheduleProjectionCalculator scheduleProjectionCalculator,
        IUnitOfWork unitOfWork,
        EventLocationAttachmentService eventLocationAttachmentService,
        IMapper mapper)
    {
        _eventAgendaItemRepository = eventAgendaItemRepository;
        _eventRepository = eventRepository;
        _eventDayRepository = eventDayRepository;
        _scheduleProjectionCalculator = scheduleProjectionCalculator;
        _unitOfWork = unitOfWork;
        _eventLocationAttachmentService = eventLocationAttachmentService;
        _mapper = mapper;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(CreateEventAgendaItemCommand request, CancellationToken cancellationToken)
    {
        var validator = new CreateEventAgendaItemDtoValidator(_eventRepository);
        var validationResult = await validator.ValidateAsync(request.EventAgendaItemDto, cancellationToken);

        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(e => e.ErrorMessage),
                "Event agenda item creation failed.");
        }

        var parentEvent = await _eventRepository.GetById(request.EventAgendaItemDto.EventId);
        if (parentEvent == null)
        {
            return BaseCommandResponse.Validation<Guid>(
                ["Event not found in the current tenant."],
                "Event not found in the current tenant.");
        }

        var agendaItem = _mapper.Map<EventAgendaItem>(request.EventAgendaItemDto);
        agendaItem.TenantId = parentEvent.TenantId;

        agendaItem.Reschedule(
            UtcInstantRange.Create(request.EventAgendaItemDto.StartTime, request.EventAgendaItemDto.EndTime),
            parentEvent.EventTimeZoneId ?? parentEvent.Timezone ?? string.Empty,
            _scheduleProjectionCalculator);

        var matchingDay = await _eventDayRepository.FindByEventAndLocalDateAsync(
            parentEvent.Id, agendaItem.LocalStartDate, cancellationToken);
        agendaItem.EventDayId = matchingDay?.Id;

        agendaItem = await _unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            EventLocation eventLocation = await _eventLocationAttachmentService.ResolveAsync(
                parentEvent.Id,
                agendaItem.LocationId,
                agendaItem.EventLocationId,
                token);
            agendaItem.AssignEventLocation(eventLocation);
            return await _eventAgendaItemRepository.Create(agendaItem);
        }, cancellationToken);

        return BaseCommandResponse.Success(agendaItem.Id, "Event agenda item created successfully.");
    }
}
