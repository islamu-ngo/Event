using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using FluentValidation;

namespace Explore.Application.DTOs.EventSession.Validators;

public class CreateEventSessionDtoValidator : AbstractValidator<CreateEventSessionDto>
{
    private readonly IEventRepository _eventRepository;
    private readonly ILocationRepository _locationRepository;
    private readonly IRegistrationModeRepository _registrationModeRepository;
    private readonly IEventSessionKindRepository _eventSessionKindRepository;
    private readonly IEventSessionTemplateRepository _eventSessionTemplateRepository;
    private readonly IEventSessionRepository _eventSessionRepository;

    public CreateEventSessionDtoValidator(
        IEventRepository eventRepository,
        ILocationRepository locationRepository,
        IRegistrationModeRepository registrationModeRepository,
        IEventSessionKindRepository eventSessionKindRepository,
        IEventSessionTemplateRepository eventSessionTemplateRepository,
        IEventSessionRepository eventSessionRepository)
    {
        _eventRepository = eventRepository;
        _locationRepository = locationRepository;
        _registrationModeRepository = registrationModeRepository;
        _eventSessionKindRepository = eventSessionKindRepository;
        _eventSessionTemplateRepository = eventSessionTemplateRepository;
        _eventSessionRepository = eventSessionRepository;

        RuleFor(p => p.EventId)
            .NotEmpty().WithMessage("{PropertyName} is required.")
            .MustAsync(async (id, cancellation) =>
            {
                var exists = await _eventRepository.Exists(id);
                return exists;
            }).WithMessage("{PropertyName} does not exist.");

        RuleFor(p => p.StartTime)
            .NotEmpty().WithMessage("{PropertyName} is required.");

        RuleFor(p => p.EndTimeType)
            .IsInEnum().WithMessage("Invalid session end-time type.");

        RuleFor(p => p.EndTime)
            .NotEmpty().When(p => p.EndTimeType == SessionEndTimeType.Fixed)
            .WithMessage("{PropertyName} is required when EndTimeType is Fixed.");

        RuleFor(p => p.EndTime)
            .Empty().When(p => p.EndTimeType == SessionEndTimeType.OpenEnded)
            .WithMessage("{PropertyName} must be empty when EndTimeType is OpenEnded.");

        RuleFor(p => p.EndTime)
            .GreaterThan(p => p.StartTime)
            .When(p => p.EndTime.HasValue)
            .WithMessage("{PropertyName} must be after StartTime.");

        RuleFor(p => p.LocationId)
            .MustAsync(async (id, cancellation) =>
            {
                if (!id.HasValue) return true;
                var exists = await _locationRepository.Exists(id.Value);
                return exists;
            }).WithMessage("{PropertyName} does not exist.");

        RuleFor(p => p.Title)
            .MaximumLength(500).When(p => !string.IsNullOrEmpty(p.Title))
            .WithMessage("{PropertyName} must not exceed 500 characters.");

        RuleFor(p => p.EventSessionKindId)
            .MustAsync(async (id, cancellation) =>
            {
                if (!id.HasValue) return true;
                var exists = await _eventSessionKindRepository.Exists(id.Value);
                return exists;
            }).WithMessage("{PropertyName} does not exist.");

        RuleFor(p => p.Description)
            .MaximumLength(500).When(p => !string.IsNullOrEmpty(p.Description))
            .WithMessage("{PropertyName} must not exceed 500 characters.");

        RuleFor(p => p.Slug)
            .MaximumLength(500).When(p => !string.IsNullOrEmpty(p.Slug))
            .WithMessage("{PropertyName} must not exceed 500 characters.");

        RuleFor(p => p.MaxAudienceAttendees)
            .GreaterThan(0).When(p => p.MaxAudienceAttendees.HasValue)
            .WithMessage("{PropertyName} must be greater than 0.");

        RuleFor(p => p.RegistrationModeId)
            .MustAsync(async (id, cancellation) =>
            {
                if (!id.HasValue) return true;
                var exists = await _registrationModeRepository.Exists(id.Value);
                return exists;
            }).WithMessage("{PropertyName} does not exist.");

        RuleFor(p => p.IslamicAspect!.StartTimeType)
            .IsInEnum()
            .When(p => p.IslamicAspect is not null)
            .WithMessage("Invalid Islamic session start-time type.");

        RuleFor(p => p.IslamicAspect!.OffsetMinutes)
            .InclusiveBetween(
                EventSessionIslamicAspect.MinOffsetMinutes,
                EventSessionIslamicAspect.MaxOffsetMinutes)
            .When(p => p.IslamicAspect?.OffsetMinutes is not null)
            .WithMessage(EventSessionIslamicAspectValidationRules.OffsetRangeMessage);

        RuleFor(p => p.IslamicAspect!.EndOffsetMinutes)
            .InclusiveBetween(
                EventSessionIslamicAspect.MinOffsetMinutes,
                EventSessionIslamicAspect.MaxOffsetMinutes)
            .When(p => p.IslamicAspect?.EndOffsetMinutes is not null)
            .WithMessage(EventSessionIslamicAspectValidationRules.OffsetRangeMessage);

        RuleFor(p => p)
            .Must(p => EventSessionIslamicAspectValidationRules.HasValidSchedulingState(p.IslamicAspect, p.LocationId))
            .WithMessage(EventSessionIslamicAspectValidationRules.SchedulingStateMessage);

        RuleFor(p => p.SessionTemplateId)
            .MustAsync(async (id, cancellation) =>
            {
                if (!id.HasValue) return true;
                return await _eventSessionTemplateRepository.Exists(id.Value);
            })
            .When(p => p.SessionTemplateId.HasValue)
            .WithMessage("Event session template does not exist.");

        // Friendly same-room overlap check. The repository re-check and PostgreSQL exclusion constraint
        // remain authoritative for racing writes and non-validator persistence paths.
        RuleFor(p => p)
            .MustAsync(async (dto, cancellation) =>
            {
                if (!dto.RoomId.HasValue || !dto.EndTime.HasValue) return true;
                var conflicts = await _eventSessionRepository.GetOverlappingSessionsInRoomAsync(
                    dto.RoomId.Value,
                    dto.StartTime,
                    dto.EndTime.Value,
                    excludeSessionId: null,
                    cancellation);
                return conflicts.Count == 0;
            })
            .When(p => p.RoomId.HasValue && p.EndTime.HasValue && p.EndTime > p.StartTime)
            .WithMessage("The selected room is already booked for an overlapping time range.");

        // TenantId is set by the handler from ITenantContext, not by the client
        // No validation needed here
    }
}
