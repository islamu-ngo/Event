using Explore.Application.Contracts.Persistence;
using Explore.Domain.Services.Scheduling;
using FluentValidation;

namespace Explore.Application.DTOs.Event.Validators;

public sealed class UpdateEventDraftRequestDtoValidator : AbstractValidator<UpdateEventDraftRequestDto>
{
    public UpdateEventDraftRequestDtoValidator(
        IAudienceAgeRepository audienceAgeRepository,
        IAudienceGenderRepository audienceGenderRepository,
        IEventTypeRepository eventTypeRepository,
        IVisibilityTypeRepository visibilityTypeRepository,
        IEventFormatRepository eventFormatRepository,
        IStorageObjectRepository storageObjectRepository,
        IEventSeriesRepository eventSeriesRepository,
        IEventRegistrationPolicyRepository eventRegistrationPolicyRepository)
    {
        RuleFor(request => request.ExpectedConcurrencyStamp)
            .NotEmpty().WithMessage("{PropertyName} is required.");

        RuleFor(request => request.ExpectedParticipationConfigurationConcurrencyStamp)
            .NotEmpty().WithMessage("{PropertyName} is required.");

        RuleFor(request => request.Title)
            .NotEmpty().WithMessage("{PropertyName} is required.")
            .MaximumLength(500).WithMessage("{PropertyName} must not exceed 500 characters.");

        RuleFor(request => request.Subtitle)
            .MaximumLength(200).WithMessage("{PropertyName} must not exceed 200 characters.");

        RuleFor(request => request.Description)
            .MaximumLength(150).WithMessage("{PropertyName} must not exceed 150 characters.");

        RuleFor(request => request.Content)
            .MaximumLength(5000).WithMessage("{PropertyName} must not exceed 5000 characters.");

        RuleFor(request => request.Slug)
            .MaximumLength(500).WithMessage("{PropertyName} must not exceed 500 characters.");

        RuleFor(request => request.EventTypeId)
            .MustAsync(async (id, _) => !id.HasValue || await eventTypeRepository.Exists(id.Value))
            .WithMessage("{PropertyName} does not exist.");

        RuleFor(request => request.AudienceGenderId)
            .MustAsync(async (id, _) => !id.HasValue || await audienceGenderRepository.Exists(id.Value))
            .WithMessage("{PropertyName} does not exist.");

        RuleFor(request => request.AudienceAgeId)
            .MustAsync(async (id, _) => !id.HasValue || await audienceAgeRepository.Exists(id.Value))
            .WithMessage("{PropertyName} does not exist.");

        RuleFor(request => request.VisibilityTypeId)
            .NotEmpty().WithMessage("{PropertyName} is required.")
            .MustAsync(async (id, _) => await visibilityTypeRepository.Exists(id))
            .WithMessage("{PropertyName} does not exist.");

        RuleFor(request => request.EventFormatId)
            .NotEmpty().WithMessage("{PropertyName} is required.")
            .MustAsync(async (id, _) => await eventFormatRepository.Exists(id))
            .WithMessage("{PropertyName} does not exist.");

        RuleFor(request => request.FeaturedImageId)
            .MustAsync(async (id, _) => !id.HasValue || await storageObjectRepository.Exists(id.Value))
            .WithMessage("{PropertyName} does not exist.");

        RuleFor(request => request.BackgroundImageId)
            .MustAsync(async (id, _) => !id.HasValue || await storageObjectRepository.Exists(id.Value))
            .WithMessage("{PropertyName} does not exist.");

        RuleFor(request => request.ParticipationConfiguration)
            .NotNull().WithMessage("ParticipationConfiguration is required.")
            .SetValidator(new ConfigureEventParticipationDtoValidator());

        RuleFor(request => request.Timezone)
            .MaximumLength(500).When(request => !string.IsNullOrEmpty(request.Timezone))
            .WithMessage("{PropertyName} must not exceed 500 characters.");

        RuleFor(request => request.EventTimeZoneId)
            .MaximumLength(500).When(request => !string.IsNullOrEmpty(request.EventTimeZoneId))
            .WithMessage("{PropertyName} must not exceed 500 characters.");

        RuleFor(request => request)
            .Must(HaveValidScheduleTimeZone)
            .WithMessage("EventTimeZoneId/Timezone must be a valid system timezone id.");

        RuleFor(request => request)
            .Must(HaveConsistentTimeZoneAliases)
            .WithMessage("EventTimeZoneId and Timezone must match when both are provided.");

        RuleFor(request => request.EventSeriesId)
            .MustAsync(async (id, _) => !id.HasValue || await eventSeriesRepository.Exists(id.Value))
            .WithMessage("Event series does not exist.");

        RuleFor(request => request.RegistrationPolicyId)
            .MustAsync(async (id, _) => !id.HasValue || await eventRegistrationPolicyRepository.Exists(id.Value))
            .WithMessage("Registration policy does not exist.");

        RuleFor(request => request.SeriesOrder)
            .GreaterThanOrEqualTo(0)
            .When(request => request.SeriesOrder.HasValue)
            .WithMessage("{PropertyName} must be non-negative.");
    }

    private static bool HaveValidScheduleTimeZone(UpdateEventDraftRequestDto request)
    {
        return ScheduleTimeZoneResolver.IsValidOrBlank(request.EventTimeZoneId ?? request.Timezone);
    }

    private static bool HaveConsistentTimeZoneAliases(UpdateEventDraftRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.EventTimeZoneId) || string.IsNullOrWhiteSpace(request.Timezone))
        {
            return true;
        }

        try
        {
            return ScheduleTimeZoneResolver.NormalizeOrUtc(request.EventTimeZoneId)
                == ScheduleTimeZoneResolver.NormalizeOrUtc(request.Timezone);
        }
        catch (ArgumentException)
        {
            return true;
        }
    }
}
