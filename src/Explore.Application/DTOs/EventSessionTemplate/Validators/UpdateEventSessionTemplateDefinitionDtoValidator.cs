using FluentValidation;

namespace Explore.Application.DTOs.EventSessionTemplate.Validators;

public class UpdateEventSessionTemplateDefinitionDtoValidator : AbstractValidator<UpdateEventSessionTemplateDefinitionDto>
{
    public UpdateEventSessionTemplateDefinitionDtoValidator()
    {
        Include(new CreateEventSessionTemplateDefinitionDtoValidator());

        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Id is required.");
    }
}
