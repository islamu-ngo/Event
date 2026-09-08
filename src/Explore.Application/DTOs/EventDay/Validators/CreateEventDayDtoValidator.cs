using Explore.Application.Contracts.Persistence;
using FluentValidation;

namespace Explore.Application.DTOs.EventDay.Validators;

public class CreateEventDayDtoValidator : AbstractValidator<CreateEventDayDto>
{
    public CreateEventDayDtoValidator(
        IEventRepository eventRepository,
        IEventDayRepository eventDayRepository)
    {
        RuleFor(d => d.EventId)
            .NotEmpty().WithMessage("{PropertyName} is required.")
            .MustAsync(async (eventId, ct) => await eventRepository.Exists(eventId))
            .WithMessage("Event does not exist.");

        RuleFor(d => d.LocalDate)
            .NotEmpty().WithMessage("{PropertyName} is required.");

        RuleFor(d => d)
            .MustAsync(async (dto, ct) =>
            {
                var existing = await eventDayRepository.FindByEventAndLocalDateAsync(dto.EventId, dto.LocalDate, ct);
                return existing is null;
            })
            .WithMessage("An EventDay already exists for this event on the specified date.")
            .WithName("LocalDate");

        RuleFor(d => d.Label)
            .MaximumLength(200).WithMessage("{PropertyName} must not exceed 200 characters.");

        RuleFor(d => d.Description)
            .MaximumLength(2000).WithMessage("{PropertyName} must not exceed 2000 characters.");

        RuleFor(d => d.BannerText)
            .MaximumLength(500).WithMessage("{PropertyName} must not exceed 500 characters.");

        RuleFor(d => d.SortOrder)
            .GreaterThanOrEqualTo(0).WithMessage("{PropertyName} must be non-negative.");
    }
}
