using System.Text.Json;
using Explore.Application.DTOs.Instance;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.DTOs.Onboarding.Validators;
using Explore.Domain.Enums;

namespace Event.Application.UnitTests.DTOs.Onboarding;

public class AuthProviderConfigurationDtoValidatorTests
{
    [Test]
    [Arguments("googleSsoEnabled", "true")]
    [Arguments("googleClientId", "\"retired-client\"")]
    [Arguments("lockGoogleSsoEnabled", "true")]
    [Arguments("googlePublicOnboardingPolicy", "1")]
    [Arguments("googlePublicSignupUrl", "\"https://identity.example.test/signup\"")]
    public async Task RetiredGoogleFieldsCannotEnterTheProviderWriteContract(string field, string value)
    {
        string payload = $"{{\"{field}\":{value}}}";

        await Assert.ThrowsAsync<JsonException>(() => Task.FromResult(
            JsonSerializer.Deserialize<AuthProviderConfigurationWriteDto>(payload, JsonSerializerOptions.Web)));
    }

    [Test]
    public async Task PublicProviderMetadataDoesNotAdvertiseRetiredGoogleConfiguration()
    {
        using var metadata = JsonDocument.Parse(
            JsonSerializer.Serialize(new AuthProviderConfigurationDto(), JsonSerializerOptions.Web));
        string[] retiredFields = ["googleSsoEnabled", "googleClientId", "googleClientSecretOwnership",
            "lockGoogleSsoEnabled", "googlePublicOnboardingPolicy", "googlePublicSignupUrl"];

        foreach (string field in retiredFields)
            await Assert.That(metadata.RootElement.TryGetProperty(field, out _)).IsFalse();
    }

    [Test]
    [Arguments(AuthenticationProviderKind.Local)]
    [Arguments(AuthenticationProviderKind.Keycloak)]
    [Arguments(AuthenticationProviderKind.Atproto)]
    public async Task RetiredGoogleInputCannotBeRevivedByCredentialOwnershipMetadata(
        AuthenticationProviderKind primary)
    {
        string payload = JsonSerializer.Serialize(new
        {
            PrimaryProviderId = (int)primary,
            GoogleSsoEnabled = true,
            GoogleClientSecretOwnership = new { Configured = true }
        }, JsonSerializerOptions.Web);

        await Assert.ThrowsAsync<JsonException>(() => Task.FromResult(
            JsonSerializer.Deserialize<AuthProviderConfigurationWriteDto>(payload, JsonSerializerOptions.Web)));
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
    public async Task Validate_UnsupportedDirectGooglePrimary_IsInvalid()
    {
        var configuration = new AuthProviderConfigurationDto
        {
            PrimaryProviderId = (int)AuthenticationProviderKind.Google,
            PrimaryProviderCode = "google",
            PrimaryProviderName = "Google"
        };

        var result = await new AuthProviderConfigurationDtoValidator()
            .ValidateAsync(configuration);

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Errors.Any(error =>
            error.PropertyName == nameof(AuthProviderConfigurationDto.PrimaryProviderId))).IsTrue();
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
