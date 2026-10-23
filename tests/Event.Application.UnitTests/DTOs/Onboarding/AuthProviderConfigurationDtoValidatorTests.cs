using Explore.Application.DTOs.Onboarding;
using Explore.Application.DTOs.Onboarding.Validators;
using Explore.Domain.Enums;

namespace Event.Application.UnitTests.DTOs.Onboarding;

public class AuthProviderConfigurationDtoValidatorTests
{
    [Test]
    [Arguments(false, true)]
    [Arguments(false, false)]
    [Arguments(true, false)]
    public async Task GoogleReadinessUsesAuthoritativeOwnershipNotSubmittedMetadata(
        bool authoritativeConfigured, bool submittedConfigured)
    {
        var current = new AuthProviderConfigurationDto
        {
            GoogleClientSecretOwnership = new() { Configured = authoritativeConfigured }
        };
        var requested = new AuthProviderConfigurationDto
        {
            GoogleSsoEnabled = true,
            GoogleClientId = "client.apps.googleusercontent.com",
            GoogleClientSecretOwnership = new() { Configured = submittedConfigured }
        };

        var result = await new AuthProviderConfigurationDtoValidator(current).ValidateAsync(requested);

        await Assert.That(result.IsValid).IsEqualTo(authoritativeConfigured);
        if (!authoritativeConfigured)
            await Assert.That(result.Errors.Any(error =>
                error.PropertyName == nameof(AuthProviderConfigurationDto.GoogleClientSecretOwnership))).IsTrue();
    }

    [Test]
    public async Task Validate_KeycloakWithoutSecretAndNoConfiguredSecret_IsInvalid()
    {
        var validator = new AuthProviderConfigurationDtoValidator();

        var result = await validator.ValidateAsync(CreateKeycloakConfiguration());

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Errors.Any(error =>
            error.PropertyName == nameof(AuthProviderConfigurationDto.KeycloakClientSecretOwnership))).IsTrue();
    }

    [Test]
    public async Task Validate_KeycloakWithoutSecretAndConfiguredSecret_IsValid()
    {
        var current = CreateKeycloakConfiguration();
        current = current with
        {
            KeycloakClientSecretOwnership = current.KeycloakClientSecretOwnership with { Configured = true }
        };
        var validator = new AuthProviderConfigurationDtoValidator(current);

        var result = await validator.ValidateAsync(CreateKeycloakConfiguration());

        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async Task Validate_KeycloakWithoutSecretForDifferentClient_IsInvalid()
    {
        var current = CreateKeycloakConfiguration();
        current = current with
        {
            KeycloakClientSecretOwnership = current.KeycloakClientSecretOwnership with { Configured = true }
        };
        var validator = new AuthProviderConfigurationDtoValidator(current);
        var requested = CreateKeycloakConfiguration();
        requested = requested with { KeycloakClientId = "replacement-client" };

        var result = await validator.ValidateAsync(requested);

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Errors.Any(error =>
            error.PropertyName == nameof(AuthProviderConfigurationDto.KeycloakClientSecretOwnership))).IsTrue();
    }

    [Test]
    public async Task Validate_LocalPrimaryProviderWithoutKeycloakMetadata_IsValid()
    {
        var configuration = new AuthProviderConfigurationDto
        {
            PrimaryProviderId = (int)AuthenticationProviderKind.Local,
            PrimaryProviderCode = "local",
            PrimaryProviderName = "Local Identity"
        };

        var result = await new AuthProviderConfigurationDtoValidator()
            .ValidateAsync(configuration);

        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async Task Validate_AtprotoPrimaryWithEnabledAxisAndPublicUrl_IsValid()
    {
        var configuration = new AuthProviderConfigurationDto
        {
            PrimaryProviderId = (int)AuthenticationProviderKind.Atproto,
            PrimaryProviderCode = "atproto",
            PrimaryProviderName = "AT Protocol",
            AtprotoLoginEnabled = true,
            AtprotoPublicUrl = "https://events.example.test"
        };

        var result = await new AuthProviderConfigurationDtoValidator()
            .ValidateAsync(configuration);

        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async Task Validate_AtprotoPrimaryWithDisabledAxis_IsInvalid()
    {
        var configuration = new AuthProviderConfigurationDto
        {
            PrimaryProviderId = (int)AuthenticationProviderKind.Atproto,
            PrimaryProviderCode = "atproto",
            PrimaryProviderName = "AT Protocol",
            AtprotoLoginEnabled = false,
            AtprotoPublicUrl = "https://events.example.test"
        };

        var result = await new AuthProviderConfigurationDtoValidator()
            .ValidateAsync(configuration);

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Errors.Any(error =>
            error.PropertyName == nameof(AuthProviderConfigurationDto.AtprotoLoginEnabled))).IsTrue();
    }

    [Test]
    public async Task Validate_AtprotoPrimaryWithGoogleEnabled_IsInvalid()
    {
        var configuration = new AuthProviderConfigurationDto
        {
            PrimaryProviderId = (int)AuthenticationProviderKind.Atproto,
            PrimaryProviderCode = "atproto",
            PrimaryProviderName = "AT Protocol",
            AtprotoLoginEnabled = true,
            AtprotoPublicUrl = "https://events.example.test",
            GoogleSsoEnabled = true,
            GoogleClientId = "client.apps.googleusercontent.com"
        };

        var result = await new AuthProviderConfigurationDtoValidator()
            .ValidateAsync(configuration);

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Errors.Any(error =>
            error.PropertyName == nameof(AuthProviderConfigurationDto.GoogleSsoEnabled))).IsTrue();
    }

    [Test]
    public async Task Validate_AtprotoCanCoexistWithLocalPrimaryProvider()
    {
        var configuration = new AuthProviderConfigurationDto
        {
            PrimaryProviderId = (int)AuthenticationProviderKind.Local,
            PrimaryProviderCode = "local",
            PrimaryProviderName = "Local Identity",
            AtprotoLoginEnabled = true,
            AtprotoPublicUrl = "https://events.example.test"
        };

        var result = await new AuthProviderConfigurationDtoValidator()
            .ValidateAsync(configuration);

        await Assert.That(result.IsValid).IsTrue();
    }

    private static AuthProviderConfigurationDto CreateKeycloakConfiguration()
    {
        return new AuthProviderConfigurationDto
        {
            PrimaryProviderId = (int)AuthenticationProviderKind.Keycloak,
            PrimaryProviderCode = "keycloak",
            PrimaryProviderName = "Keycloak",
            KeycloakAuthority = "https://id.example.test/realms/event",
            KeycloakClientId = "islamu-event-blazor"
        };
    }
}
