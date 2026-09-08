using FluentValidation;

namespace Explore.Application.DTOs.CustomPropertyProjection.Validators;

public class DrainDirtyScopesRequestDtoValidator : AbstractValidator<DrainDirtyScopesRequestDto>
{
    public DrainDirtyScopesRequestDtoValidator()
    {
        RuleFor(x => x.TenantId)
            .NotEmpty().WithMessage("TenantId is required.");

        RuleFor(x => x.ProjectionName)
            .NotEmpty().WithMessage("ProjectionName is required.");
    }
}
