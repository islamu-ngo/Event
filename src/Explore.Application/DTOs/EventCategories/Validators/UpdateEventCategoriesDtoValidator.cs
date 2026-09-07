using System;
using FluentValidation;

namespace Explore.Application.DTOs.EventCategories.Validators;

public class UpdateEventCategoriesDtoValidator : AbstractValidator<UpdateEventCategoriesDto>
{
    public UpdateEventCategoriesDtoValidator()
    {
        RuleFor(x => x)
            .Must(dto => dto.Event is not null || dto.Category is not null)
            .WithMessage("At least one event category update group must be provided.");

        When(x => x.Event is not null, () =>
        {
            RuleFor(x => x.Event!.EventId)
                .NotEmpty()
                .WithMessage("EventId is required.");
        });

        When(x => x.Category is not null, () =>
        {
            RuleFor(x => x.Category!.CategoryId)
                .NotEmpty()
                .WithMessage("CategoryId is required.");
        });
    }
}
