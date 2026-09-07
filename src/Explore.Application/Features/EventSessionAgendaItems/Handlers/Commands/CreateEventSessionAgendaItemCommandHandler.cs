using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSessionAgendaItem.Validators;
using Explore.Application.Features.EventSessionAgendaItems.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Domain;
using MediatR;

namespace Explore.Application.Features.EventSessionAgendaItems.Handlers.Commands;

public class CreateEventSessionAgendaItemCommandHandler : IRequestHandler<CreateEventSessionAgendaItemCommand, BaseCommandResponse<Guid>>
{
    private readonly IEventSessionAgendaItemRepository _agendaItemRepository;
    private readonly IEventSessionRepository _eventSessionRepository;
    private readonly ILocationRepository _locationRepository;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly EventLocationAttachmentService _eventLocationAttachmentService;
    private readonly IMapper _mapper;

    public CreateEventSessionAgendaItemCommandHandler(
        IEventSessionAgendaItemRepository agendaItemRepository,
        IEventSessionRepository eventSessionRepository,
        ILocationRepository locationRepository,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork,
        EventLocationAttachmentService eventLocationAttachmentService,
        IMapper mapper)
    {
        _agendaItemRepository = agendaItemRepository;
        _eventSessionRepository = eventSessionRepository;
        _locationRepository = locationRepository;
        _tenantContext = tenantContext;
        _unitOfWork = unitOfWork;
        _eventLocationAttachmentService = eventLocationAttachmentService;
        _mapper = mapper;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(CreateEventSessionAgendaItemCommand request, CancellationToken cancellationToken)
    {
        var validator = new CreateEventSessionAgendaItemDtoValidator(_eventSessionRepository, _locationRepository);
        var validationResult = await validator.ValidateAsync(request.AgendaItemDto, cancellationToken);

        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(e => e.ErrorMessage),
                "Agenda item creation failed.");
        }

        var agendaItem = _mapper.Map<EventSessionAgendaItem>(request.AgendaItemDto);

        // Set TenantId from the request context
        agendaItem.TenantId = _tenantContext.TenantId;

        EventSession? parentSession = await _eventSessionRepository.GetById(request.AgendaItemDto.EventSessionId);
        if (parentSession is null || parentSession.TenantId != agendaItem.TenantId)
        {
            return BaseCommandResponse.NotFound<Guid>("Event session not found in the current tenant.");
        }

        agendaItem.EventSession = parentSession;
        agendaItem = await _unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            EventLocation eventLocation = await _eventLocationAttachmentService.ResolveAsync(
                parentSession.EventId,
                agendaItem.LocationId,
                agendaItem.EventLocationId,
                token);
            agendaItem.AssignEventLocation(eventLocation);
            return await _agendaItemRepository.Create(agendaItem);
        }, cancellationToken);

        return BaseCommandResponse.Success(agendaItem.Id, "Agenda item created successfully.");
    }
}
