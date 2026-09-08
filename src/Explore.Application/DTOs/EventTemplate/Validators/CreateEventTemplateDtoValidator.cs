using FluentValidation;

namespace Explore.Application.DTOs.EventTemplate.Validators;

public class CreateEventTemplateDtoValidator : AbstractValidator<CreateEventTemplateDto>
{
    public CreateEventTemplateDtoValidator()
    {
        RuleFor(x => x.TemplateKey)
            .NotEmpty().WithMessage("TemplateKey is required.")
            .MaximumLength(100).WithMessage("TemplateKey must not exceed 100 characters.");

        RuleFor(x => x.DisplayName)
            .NotEmpty().WithMessage("DisplayName is required.")
            .MaximumLength(200).WithMessage("DisplayName must not exceed 200 characters.");

        RuleFor(x => x.Description)
            .MaximumLength(500).WithMessage("Description must not exceed 500 characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.Description));

        RuleFor(x => x.Version)
            .GreaterThan(0).WithMessage("Version must be greater than 0.");

        RuleForEach(x => x.Definitions)
            .SetValidator(new CreateEventTemplateDefinitionDtoValidator());
    }
}
