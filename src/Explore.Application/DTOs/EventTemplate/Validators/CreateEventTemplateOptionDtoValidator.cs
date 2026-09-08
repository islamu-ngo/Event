using FluentValidation;

namespace Explore.Application.DTOs.EventTemplate.Validators;

public class CreateEventTemplateOptionDtoValidator : AbstractValidator<CreateEventTemplateOptionDto>
{
    public CreateEventTemplateOptionDtoValidator()
    {
        RuleFor(x => x.Namespace)
            .NotEmpty().WithMessage("Namespace is required.")
            .MaximumLength(100).WithMessage("Namespace must not exceed 100 characters.");

        RuleFor(x => x.Key)
            .NotEmpty().WithMessage("Key is required.")
            .MaximumLength(100).WithMessage("Key must not exceed 100 characters.");

        RuleFor(x => x.DisplayName)
            .NotEmpty().WithMessage("DisplayName is required.")
            .MaximumLength(200).WithMessage("DisplayName must not exceed 200 characters.");

        RuleFor(x => x.Description)
            .MaximumLength(500).WithMessage("Description must not exceed 500 characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.Description));

        RuleFor(x => x.Value)
            .NotEmpty().WithMessage("Value is required.")
            .MaximumLength(500).WithMessage("Value must not exceed 500 characters.");
    }
}
