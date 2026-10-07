using FluentValidation;
using Explore.Domain.Enums;

namespace Explore.Application.DTOs.Onboarding.Validators;

public class AuthProviderConfigurationDtoValidator : AbstractValidator<AuthProviderConfigurationDto>
{
    public AuthProviderConfigurationDtoValidator(
        AuthProviderConfigurationDto? authoritativeCurrentConfiguration = null)
    {
        RuleFor(x => x.PrimaryProviderId)
            .Must(BeSupportedPrimaryProvider)
            .WithMessage("Primary authentication provider must be Local Identity, Keycloak, or AT Protocol.");

        When(x => x.PrimaryProviderId == (int)AuthenticationProviderKind.Atproto, () =>
        {
            RuleFor(x => x.AtprotoLoginEnabled)
                .Equal(true)
                .WithMessage("AT Protocol login must be enabled when AT Protocol is the primary provider.");

            RuleFor(x => x.GoogleSsoEnabled)
                .Equal(false)
                .WithMessage("Google SSO must be disabled when AT Protocol is the sole primary provider.");
        });

        When(x => x.PrimaryProviderId == (int)AuthenticationProviderKind.Keycloak, () =>
        {
            RuleFor(x => x.KeycloakAuthority)
                .NotEmpty()
                .WithMessage("Keycloak authority URL is required when Keycloak is enabled.");

            RuleFor(x => x.KeycloakClientId)
                .NotEmpty()
                .WithMessage("Keycloak client ID is required when Keycloak is enabled.");

            RuleFor(x => x.KeycloakClientSecretOwnership)
                .Must((configuration, _) => CanReuseConfiguredKeycloakSecret(
                    authoritativeCurrentConfiguration, configuration))
                .WithMessage("The selected deployment authority must have a configured Keycloak credential for the requested client.");
        });

        When(x => x.AtprotoLoginEnabled, () =>
        {
            RuleFor(x => x.AtprotoPublicUrl)
                .NotEmpty()
                .WithMessage("A publicly accessible URL is required when ATProto login is enabled.");

            RuleFor(x => x.AtprotoPublicUrl)
                .Must(BeAValidUrl)
                .When(x => !string.IsNullOrWhiteSpace(x.AtprotoPublicUrl))
                .WithMessage("ATProto public URL must be a valid URL.");
        });

        When(x => x.GoogleSsoEnabled, () =>
        {
            RuleFor(x => x.GoogleClientId)
                .NotEmpty()
                .WithMessage("Google client ID is required when Google SSO is enabled.");

            RuleFor(x => x.GoogleClientSecretOwnership)
                .Must(_ => authoritativeCurrentConfiguration?.GoogleClientSecretOwnership.Configured == true)
                .WithMessage("The selected deployment authority must have a configured Google credential.");
        });
    }

    private static bool BeSupportedPrimaryProvider(int providerId) =>
        providerId is (int)AuthenticationProviderKind.Local
            or (int)AuthenticationProviderKind.Keycloak
            or (int)AuthenticationProviderKind.Atproto;

    private static bool CanReuseConfiguredKeycloakSecret(
        AuthProviderConfigurationDto? current,
        AuthProviderConfigurationDto requested)
    {
        return current?.KeycloakClientSecretOwnership.Configured == true
               && string.Equals(
                   current.KeycloakAuthority,
                   requested.KeycloakAuthority,
                   StringComparison.Ordinal)
               && string.Equals(
                   current.KeycloakClientId,
                   requested.KeycloakClientId,
                   StringComparison.Ordinal);
    }

    private static bool BeAValidUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
               && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }
}
