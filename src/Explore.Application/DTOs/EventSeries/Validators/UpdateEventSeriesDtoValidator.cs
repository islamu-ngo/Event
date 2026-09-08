using Explore.Application.DTOs.EventSeries;
using FluentValidation;

namespace Explore.Application.DTOs.EventSeries.Validators;

public class UpdateEventSeriesDtoValidator : AbstractValidator<UpdateEventSeriesDto>
{
    public UpdateEventSeriesDtoValidator()
    {
        RuleFor(dto => dto)
            .Must(HaveAtLeastOneGroup)
            .WithMessage("At least one event series update group must be provided.");

        When(dto => dto.Title is not null, () =>
        {
            RuleFor(dto => dto.Title!.Value)
                .NotEmpty().WithMessage("Title is required.")
                .MaximumLength(200).WithMessage("Title must not exceed 200 characters.");
        });

        When(dto => dto.Description is not null, () =>
        {
            RuleFor(dto => dto.Description!.Value.HasValue)
                .Equal(true)
                .WithMessage("Description must specify an explicit field operation.");

            RuleFor(dto => dto.Description!.Value.Value)
                .MaximumLength(2000)
                .When(dto => dto.Description!.Value.HasValue && dto.Description.Value.Value is not null)
                .WithMessage("Description must not exceed 2000 characters.");
        });

        When(dto => dto.Slug is not null, () =>
        {
            RuleFor(dto => dto.Slug!.Value.HasValue)
                .Equal(true)
                .WithMessage("Slug must specify an explicit field operation.");

            RuleFor(dto => dto.Slug!.Value.Value)
                .MaximumLength(200)
                .When(dto => dto.Slug!.Value.HasValue && dto.Slug.Value.Value is not null)
                .WithMessage("Slug must not exceed 200 characters.");
        });

        When(dto => dto.FeaturedImage is not null, () =>
        {
            RuleFor(dto => dto.FeaturedImage!.Value.HasValue)
                .Equal(true)
                .WithMessage("FeaturedImage must specify an explicit field operation.");
        });
    }

    private static bool HaveAtLeastOneGroup(UpdateEventSeriesDto dto)
    {
        return dto.Title is not null
            || dto.Description is not null
            || dto.Slug is not null
            || dto.FeaturedImage is not null
            || dto.Publication is not null;
    }
}
