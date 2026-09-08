using FluentValidation;

namespace Explore.Application.DTOs.EventCustomProperty.Validators;

public class SetEventCustomPropertyValueDtoValidator : AbstractValidator<SetEventCustomPropertyValueDto>
{
    public SetEventCustomPropertyValueDtoValidator()
    {
        RuleFor(x => x.EventCustomPropertyDefinitionId)
            .NotEmpty().WithMessage("EventCustomPropertyDefinitionId is required.");

        RuleFor(x => x.EventId)
            .NotEmpty().WithMessage("EventId is required.");

        RuleFor(x => x.Ordinal)
            .GreaterThanOrEqualTo(0).WithMessage("Ordinal must be 0 or greater.");
    }
}
