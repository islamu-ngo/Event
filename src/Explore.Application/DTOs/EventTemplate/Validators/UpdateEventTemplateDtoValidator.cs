using FluentValidation;

namespace Explore.Application.DTOs.EventTemplate.Validators;

public class UpdateEventTemplateDtoValidator : AbstractValidator<UpdateEventTemplateDto>
{
    public UpdateEventTemplateDtoValidator()
    {
        RuleFor(x => x)
            .Must(x => x.Metadata is not null || x.Definitions is not null)
            .WithMessage("At least one update group is required.");

        RuleFor(x => x.Metadata!)
            .Must(metadata => metadata.TemplateKey is not null ||
                metadata.DisplayName is not null ||
                metadata.Description.HasValue ||
                metadata.EventTypeId.HasValue ||
                metadata.Version.HasValue ||
                metadata.IsPublished.HasValue ||
                metadata.IsActive.HasValue ||
                metadata.SortOrder.HasValue)
            .WithMessage("Metadata must contain at least one operation.")
            .When(x => x.Metadata is not null);

        RuleFor(x => x.Definitions!.Items)
            .NotNull().WithMessage("Definitions.items is required when the definitions group is supplied.")
            .When(x => x.Definitions is not null);
    }
}
