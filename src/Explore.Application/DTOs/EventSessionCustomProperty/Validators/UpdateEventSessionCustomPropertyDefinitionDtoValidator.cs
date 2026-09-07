using Explore.Application.DTOs.CustomPropertyDefinition.Validators;
using FluentValidation;

namespace Explore.Application.DTOs.EventSessionCustomProperty.Validators;

public sealed class UpdateEventSessionCustomPropertyDefinitionDtoValidator : AbstractValidator<UpdateEventSessionCustomPropertyDefinitionDto>
{
    public UpdateEventSessionCustomPropertyDefinitionDtoValidator()
    {
        RuleFor(x => x).Must(x => x.Metadata is not null || x.Validation is not null || x.Options is not null)
            .WithMessage("At least one update group is required.");
        RuleFor(x => x.Metadata).Must(UpdateCustomPropertyDefinitionDtoValidator.HasMetadataUpdate).When(x => x.Metadata is not null)
            .WithMessage("Metadata must contain at least one update.");
        RuleFor(x => x.Validation).Must(UpdateCustomPropertyDefinitionDtoValidator.HasValidationUpdate).When(x => x.Validation is not null)
            .WithMessage("Validation must contain at least one update.");
        RuleFor(x => x.Options!.Items).NotNull().When(x => x.Options is not null)
            .WithMessage("Options.items is required when options is supplied.");
    }
}
