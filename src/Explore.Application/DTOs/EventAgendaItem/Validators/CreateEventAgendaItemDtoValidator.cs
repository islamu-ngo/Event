using Explore.Application.Contracts.Persistence;
using FluentValidation;

namespace Explore.Application.DTOs.EventAgendaItem.Validators;

public class CreateEventAgendaItemDtoValidator : AbstractValidator<CreateEventAgendaItemDto>
{
    public CreateEventAgendaItemDtoValidator(IEventRepository eventRepository)
    {
        RuleFor(d => d.EventId)
            .NotEmpty().WithMessage("{PropertyName} is required.")
            .MustAsync(async (eventId, ct) => await eventRepository.Exists(eventId))
            .WithMessage("Event does not exist.");

        RuleFor(d => d.Title)
            .NotEmpty().WithMessage("{PropertyName} is required.")
            .MaximumLength(300).WithMessage("{PropertyName} must not exceed 300 characters.");

        RuleFor(d => d.Description)
            .MaximumLength(2000).WithMessage("{PropertyName} must not exceed 2000 characters.");

        RuleFor(d => d.StartTime)
            .NotEmpty().WithMessage("{PropertyName} is required.");

        RuleFor(d => d.EndTime)
            .NotEmpty().WithMessage("{PropertyName} is required.")
            .GreaterThan(d => d.StartTime).WithMessage("EndTime must be after StartTime.");

        RuleFor(d => d.SortOrder)
            .GreaterThanOrEqualTo(0).WithMessage("{PropertyName} must be non-negative.");
    }
}
