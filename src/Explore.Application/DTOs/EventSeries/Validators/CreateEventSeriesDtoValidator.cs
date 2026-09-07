using Explore.Application.DTOs.EventSeries;
using FluentValidation;

namespace Explore.Application.DTOs.EventSeries.Validators;

public class CreateEventSeriesDtoValidator : AbstractValidator<CreateEventSeriesDto>
{
    public CreateEventSeriesDtoValidator()
    {
        RuleFor(p => p.Title)
            .NotEmpty().WithMessage("{PropertyName} is required.")
            .NotNull()
            .MaximumLength(200).WithMessage("{PropertyName} must not exceed 200 characters.");

        RuleFor(p => p.ActorId)
            .NotEmpty().WithMessage("{PropertyName} is required.");
    }
}
