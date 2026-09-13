using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventDay.Validators;
using Explore.Application.Features.EventDays.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventDays.Handlers.Commands;

public class CreateEventDayCommandHandler : ICommandHandler<CreateEventDayCommand, BaseCommandResponse<Guid>>
{
    private readonly IEventDayRepository _eventDayRepository;
    private readonly IEventRepository _eventRepository;
    private readonly IStorageObjectRepository _storageObjectRepository;

    public CreateEventDayCommandHandler(
        IEventDayRepository eventDayRepository,
        IEventRepository eventRepository,
        IStorageObjectRepository storageObjectRepository)
    {
        _eventDayRepository = eventDayRepository;
        _eventRepository = eventRepository;
        _storageObjectRepository = storageObjectRepository;
    }

    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(CreateEventDayCommand request, CancellationToken cancellationToken)
    {
        var validator = new CreateEventDayDtoValidator(_eventRepository, _eventDayRepository);
        var validationResult = await validator.ValidateAsync(request.EventDayDto, cancellationToken);

        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(e => e.ErrorMessage),
                "Event day creation failed.");
        }

        var parentEvent = await _eventRepository.GetById(request.EventDayDto.EventId);
        if (parentEvent == null)
        {
            return BaseCommandResponse.Validation<Guid>(
                ["Event not found in the current tenant."],
                "Event not found in the current tenant.");
        }

        if (!await ImageReferenceEligibility.AreEligibleAsync(
                _storageObjectRepository,
                parentEvent.TenantId,
                request.EventDayDto.BannerImageId))
        {
            return BaseCommandResponse.Validation<Guid>(
                ["Banner image must be an active public safe-raster object in the current tenant."],
                "Event day creation failed.");
        }

        var eventDay = new EventDay
        {
            EventId = request.EventDayDto.EventId,
            LocalDate = request.EventDayDto.LocalDate,
            Label = request.EventDayDto.Label,
            Description = request.EventDayDto.Description,
            BannerText = request.EventDayDto.BannerText,
            BannerImageId = request.EventDayDto.BannerImageId,
            IsPublished = request.EventDayDto.IsPublished,
            SortOrder = request.EventDayDto.SortOrder,
            AllowsDayScopeRegistration = request.EventDayDto.AllowsDayScopeRegistration,
            Event = null!,
            Tenant = null!
        };
        eventDay.TenantId = parentEvent.TenantId;

        eventDay = await _eventDayRepository.Create(eventDay);

        return BaseCommandResponse.Success(eventDay.Id, "Event day created successfully.");
    }
}
