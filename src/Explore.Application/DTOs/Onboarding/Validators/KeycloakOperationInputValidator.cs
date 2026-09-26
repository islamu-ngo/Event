using FluentValidation;

namespace Explore.Application.DTOs.Onboarding.Validators;

public sealed class KeycloakInspectionCredentialsValidator
    : AbstractValidator<KeycloakInspectionCredentialsDto>
{
    public KeycloakInspectionCredentialsValidator()
    {
        RuleFor(input => input.AdministratorUsername).MaximumLength(256);
        RuleFor(input => input.AdministratorPassword).MaximumLength(4096);
        RuleFor(input => input)
            .Must(input =>
                string.IsNullOrWhiteSpace(input.AdministratorUsername)
                == string.IsNullOrWhiteSpace(input.AdministratorPassword))
            .WithMessage(
                "Administrator username and password must be supplied together.");
    }
}

public sealed class KeycloakOperationCredentialsValidator
    : AbstractValidator<KeycloakOperationCredentialsDto>
{
    public KeycloakOperationCredentialsValidator()
    {
        RuleFor(input => input.AdministratorUsername)
            .NotEmpty()
            .MaximumLength(256);
        RuleFor(input => input.AdministratorPassword)
            .NotEmpty()
            .MaximumLength(4096);
    }
}

public sealed class KeycloakOperationPlanInputValidator
    : AbstractValidator<KeycloakOperationPlanInputDto>
{
    public KeycloakOperationPlanInputValidator()
    {
        RuleFor(input => input.AdministratorUsername)
            .NotEmpty()
            .MaximumLength(256);
        RuleFor(input => input.AdministratorPassword)
            .NotEmpty()
            .MaximumLength(4096);
        RuleFor(input => input.Intent).NotNull().IsInEnum();
    }
}
