using FluentValidation;

namespace Explore.Application.DTOs.EventTemplate.Validators;

public class UpdateEventTemplateDefinitionDtoValidator : AbstractValidator<UpdateEventTemplateDefinitionDto>
{
    public UpdateEventTemplateDefinitionDtoValidator()
    {
        Include(new CreateEventTemplateDefinitionDtoValidator());

        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Id is required.");
    }
}
