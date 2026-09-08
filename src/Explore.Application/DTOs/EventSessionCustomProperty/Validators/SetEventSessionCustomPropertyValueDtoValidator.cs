using FluentValidation;

namespace Explore.Application.DTOs.EventSessionCustomProperty.Validators;

public class SetEventSessionCustomPropertyValueDtoValidator : AbstractValidator<SetEventSessionCustomPropertyValueDto>
{
    public SetEventSessionCustomPropertyValueDtoValidator()
    {
        RuleFor(x => x.EventSessionCustomPropertyDefinitionId)
            .NotEmpty().WithMessage("EventSessionCustomPropertyDefinitionId is required.");

        RuleFor(x => x.EventSessionId)
            .NotEmpty().WithMessage("EventSessionId is required.");

        RuleFor(x => x.Ordinal)
            .GreaterThanOrEqualTo(0).WithMessage("Ordinal must be 0 or greater.");
    }
}
