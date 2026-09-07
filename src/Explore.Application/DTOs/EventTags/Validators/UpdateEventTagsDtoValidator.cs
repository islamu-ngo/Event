using System;
using FluentValidation;

namespace Explore.Application.DTOs.EventTags.Validators;

public class UpdateEventTagsDtoValidator : AbstractValidator<UpdateEventTagsDto>
{
    public UpdateEventTagsDtoValidator()
    {
        RuleFor(x => x)
            .Must(dto => dto.Event is not null || dto.Tag is not null)
            .WithMessage("At least one event tag update group must be provided.");

        When(x => x.Event is not null, () =>
        {
            RuleFor(x => x.Event!.EventId)
                .NotEmpty()
                .WithMessage("EventId is required.");
        });

        When(x => x.Tag is not null, () =>
        {
            RuleFor(x => x.Tag!.TagId)
                .NotEmpty()
                .WithMessage("TagId is required.");
        });
    }
}
