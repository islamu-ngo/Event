using FluentValidation;

namespace Explore.Application.DTOs.Onboarding.Validators;

public sealed class KeycloakOperationInputValidator
    : AbstractValidator<KeycloakOperationInput>
{
    public KeycloakOperationInputValidator()
    {
        RuleFor(input => input.AdministratorUsername)
            .NotEmpty()
            .MaximumLength(256);
        RuleFor(input => input.AdministratorPassword)
            .NotEmpty()
            .MaximumLength(4096);
    }
}
