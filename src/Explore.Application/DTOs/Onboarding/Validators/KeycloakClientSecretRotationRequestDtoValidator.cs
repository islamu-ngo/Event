using FluentValidation;

namespace Explore.Application.DTOs.Onboarding.Validators;

public class KeycloakClientSecretRotationRequestDtoValidator : AbstractValidator<KeycloakClientSecretRotationRequestDto>
{
    private const int IdentifierMaxLength = 128;
    private const int SecretMaxLength = 4096;

    public KeycloakClientSecretRotationRequestDtoValidator()
    {
        RuleFor(x => x.SecretOwnershipMode)
            .NotEmpty()
            .WithMessage("Secret ownership mode is required.")
            .Must(BeSupportedOwnershipMode)
            .WithMessage("Secret ownership mode must be application-managed or deployment-managed.");

        RuleFor(x => x.ClientId)
            .MaximumLength(IdentifierMaxLength)
            .WithMessage("Keycloak client ID is too long.")
            .Must(NotContainControlCharacters)
            .WithMessage("Keycloak client ID must not contain control characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.ClientId));

        When(IsApplicationManaged, () =>
        {
            RuleFor(x => x.ConfirmApplicationManagedSecret)
                .Equal(true)
                .WithMessage("Confirm that the platform should manage the new Keycloak client secret.");

            RuleFor(x => x.NewClientSecret)
                .NotEmpty()
                .WithMessage("New Keycloak client secret is required for application-managed rotation.")
                .MaximumLength(SecretMaxLength)
                .WithMessage("New Keycloak client secret is too long.")
                .Must(NotContainControlCharacters)
                .WithMessage("New Keycloak client secret must not contain control characters.");

            RuleFor(x => x.BootstrapAdminUsername)
                .NotEmpty()
                .WithMessage("Temporary Keycloak admin username is required for application-managed rotation.")
                .MaximumLength(IdentifierMaxLength)
                .WithMessage("Temporary Keycloak admin username is too long.")
                .Must(NotContainControlCharacters)
                .WithMessage("Temporary Keycloak admin username must not contain control characters.");

            RuleFor(x => x.BootstrapAdminPassword)
                .NotEmpty()
                .WithMessage("Temporary Keycloak admin password is required for application-managed rotation.")
                .MaximumLength(SecretMaxLength)
                .WithMessage("Temporary Keycloak admin password is too long.")
                .Must(NotContainControlCharacters)
                .WithMessage("Temporary Keycloak admin password must not contain control characters.");
        });
    }

    private static bool IsApplicationManaged(KeycloakClientSecretRotationRequestDto request)
    {
        return request.SecretOwnershipMode.Equals("application-managed", StringComparison.OrdinalIgnoreCase);
    }

    private static bool BeSupportedOwnershipMode(string? value)
    {
        return value is not null
               && (value.Equals("application-managed", StringComparison.OrdinalIgnoreCase)
                   || value.Equals("deployment-managed", StringComparison.OrdinalIgnoreCase));
    }

    private static bool NotContainControlCharacters(string? value)
    {
        return value is null || !value.Any(char.IsControl);
    }
}
