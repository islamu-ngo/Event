using Explore.Application.Contracts.Persistence;
using FluentValidation;

namespace Explore.Application.DTOs.EventDay.Validators;

public class UpdateEventDayDtoValidator : AbstractValidator<UpdateEventDayDto>
{
    private readonly IEventDayRepository _eventDayRepository;

    public UpdateEventDayDtoValidator(
        IEventRepository eventRepository,
        IEventDayRepository eventDayRepository)
    {
        _eventDayRepository = eventDayRepository;

        RuleFor(d => d)
            .Must(HasAnyGroup)
            .WithMessage("At least one event day update group must be provided.");

        When(d => d.Event is not null, () =>
        {
            RuleFor(d => d.Event!).SetValidator(new UpdateEventDayEventDtoValidator(eventRepository));
        });

        When(d => d.LocalDate is not null, () =>
        {
            RuleFor(d => d.LocalDate!).SetValidator(new UpdateEventDayLocalDateDtoValidator());
        });

        When(d => d.Label is not null, () =>
        {
            RuleFor(d => d.Label!).SetValidator(new UpdateEventDayLabelDtoValidator());
        });

        When(d => d.Description is not null, () =>
        {
            RuleFor(d => d.Description!).SetValidator(new UpdateEventDayDescriptionDtoValidator());
        });

        When(d => d.BannerText is not null, () =>
        {
            RuleFor(d => d.BannerText!).SetValidator(new UpdateEventDayBannerTextDtoValidator());
        });

        When(d => d.BannerImage is not null, () =>
        {
            RuleFor(d => d.BannerImage!).SetValidator(new UpdateEventDayBannerImageDtoValidator());
        });

        When(d => d.SortOrder is not null, () =>
        {
            RuleFor(d => d.SortOrder!).SetValidator(new UpdateEventDaySortOrderDtoValidator());
        });
    }

    public async Task<FluentValidation.Results.ValidationResult> ValidateAsync(
        UpdateEventDayDto dto,
        Guid eventDayId,
        Guid currentEventId,
        CancellationToken cancellationToken)
    {
        var validationResult = await base.ValidateAsync(dto, cancellationToken);
        if (!validationResult.IsValid || dto.LocalDate is null)
        {
            return validationResult;
        }

        var effectiveEventId = dto.Event?.EventId ?? currentEventId;
        var existing = await _eventDayRepository.FindByEventAndLocalDateAsync(
            effectiveEventId,
            dto.LocalDate.Value,
            cancellationToken);

        if (existing is not null && existing.Id != eventDayId)
        {
            validationResult.Errors.Add(new FluentValidation.Results.ValidationFailure(
                nameof(UpdateEventDayDto.LocalDate),
                "Another EventDay already exists for this event on the specified date."));
        }

        return validationResult;
    }

    private static bool HasAnyGroup(UpdateEventDayDto dto) =>
        dto.Event is not null ||
        dto.LocalDate is not null ||
        dto.Label is not null ||
        dto.Description is not null ||
        dto.BannerText is not null ||
        dto.BannerImage is not null ||
        dto.Publication is not null ||
        dto.SortOrder is not null ||
        dto.Registration is not null;
}

public sealed class UpdateEventDayEventDtoValidator : AbstractValidator<UpdateEventDayEventDto>
{
    public UpdateEventDayEventDtoValidator(IEventRepository eventRepository)
    {
        RuleFor(d => d.EventId)
            .NotEmpty().WithMessage("{PropertyName} is required.")
            .MustAsync(async (eventId, ct) => await eventRepository.Exists(eventId))
            .WithMessage("Event does not exist.");
    }
}

public sealed class UpdateEventDayLocalDateDtoValidator : AbstractValidator<UpdateEventDayLocalDateDto>
{
    public UpdateEventDayLocalDateDtoValidator()
    {
        RuleFor(d => d.Value)
            .NotEmpty().WithMessage("{PropertyName} is required.");
    }
}

public sealed class UpdateEventDayLabelDtoValidator : AbstractValidator<UpdateEventDayLabelDto>
{
    public UpdateEventDayLabelDtoValidator()
    {
        RuleFor(d => d.Value)
            .Must(value => value.HasValue)
            .WithMessage("Label must include an explicit field operation.");

        RuleFor(d => d.Value.Value)
            .MaximumLength(200).WithMessage("Label must not exceed 200 characters.")
            .When(d => d.Value is { HasValue: true, Value: not null });
    }
}

public sealed class UpdateEventDayDescriptionDtoValidator : AbstractValidator<UpdateEventDayDescriptionDto>
{
    public UpdateEventDayDescriptionDtoValidator()
    {
        RuleFor(d => d.Value)
            .Must(value => value.HasValue)
            .WithMessage("Description must include an explicit field operation.");

        RuleFor(d => d.Value.Value)
            .MaximumLength(2000).WithMessage("Description must not exceed 2000 characters.")
            .When(d => d.Value is { HasValue: true, Value: not null });
    }
}

public sealed class UpdateEventDayBannerTextDtoValidator : AbstractValidator<UpdateEventDayBannerTextDto>
{
    public UpdateEventDayBannerTextDtoValidator()
    {
        RuleFor(d => d.Value)
            .Must(value => value.HasValue)
            .WithMessage("BannerText must include an explicit field operation.");

        RuleFor(d => d.Value.Value)
            .MaximumLength(500).WithMessage("BannerText must not exceed 500 characters.")
            .When(d => d.Value is { HasValue: true, Value: not null });
    }
}

public sealed class UpdateEventDayBannerImageDtoValidator : AbstractValidator<UpdateEventDayBannerImageDto>
{
    public UpdateEventDayBannerImageDtoValidator()
    {
        RuleFor(d => d.Value)
            .Must(value => value.HasValue)
            .WithMessage("BannerImage must include an explicit field operation.");
    }
}

public sealed class UpdateEventDaySortOrderDtoValidator : AbstractValidator<UpdateEventDaySortOrderDto>
{
    public UpdateEventDaySortOrderDtoValidator()
    {
        RuleFor(d => d.Value)
            .GreaterThanOrEqualTo(0).WithMessage("{PropertyName} must be non-negative.");
    }
}
