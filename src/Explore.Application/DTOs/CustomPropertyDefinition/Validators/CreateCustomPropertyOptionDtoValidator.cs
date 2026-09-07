using FluentValidation;

namespace Explore.Application.DTOs.CustomPropertyDefinition.Validators;

public class CreateCustomPropertyOptionDtoValidator : AbstractValidator<CreateCustomPropertyOptionDto>
{
    public CreateCustomPropertyOptionDtoValidator()
    {
        RuleFor(x => x.Namespace)
            .NotEmpty().WithMessage("Option namespace is required.")
            .MaximumLength(100).WithMessage("Option namespace must not exceed 100 characters.");

        RuleFor(x => x.Key)
            .NotEmpty().WithMessage("Option key is required.")
            .MaximumLength(100).WithMessage("Option key must not exceed 100 characters.");

        RuleFor(x => x.DisplayName)
            .NotEmpty().WithMessage("Option display name is required.")
            .MaximumLength(200).WithMessage("Option display name must not exceed 200 characters.");

        RuleFor(x => x.Description)
            .MaximumLength(500).WithMessage("Option description must not exceed 500 characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.Description));

        RuleFor(x => x.Value)
            .NotEmpty().WithMessage("Option value is required.")
            .MaximumLength(500).WithMessage("Option value must not exceed 500 characters.");
    }
}
