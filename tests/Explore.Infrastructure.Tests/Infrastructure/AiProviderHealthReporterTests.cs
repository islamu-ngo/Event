using Explore.Application.Contracts.Secrets;
using Explore.Domain.Ai;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;

namespace Explore.Infrastructure.Tests.Infrastructure;

public sealed class AiProviderHealthReporterTests : AiProviderTestBase
{
    [Test]
    public async Task Check_WhenDisabled_ReturnsHealthyDisabled()
    {
        var settings = new AiProviderSettings();
        Fixture.RejectSecretReads();
        var health = await Fixture.HealthReporter(settings).CheckAsync(settings, CancellationToken.None);

        await Assert.That(health.Healthy).IsTrue();
        await Assert.That(health.Status).IsEqualTo("healthy_disabled");
        await Assert.That(health.Data["enabled"]).IsEqualTo(false);
    }

    [Test]
    public async Task Check_WhenEnabledWithoutProvider_ReturnsUnhealthy()
    {
        var settings = new AiProviderSettings { Enabled = true };
        var health = await Fixture.HealthReporter(settings).CheckAsync(settings, CancellationToken.None);

        await Assert.That(health.Healthy).IsFalse();
        await Assert.That(health.Status).IsEqualTo("provider_not_configured");
    }

    [Test]
    public async Task Check_WhenFakeProviderEnabled_ReturnsHealthyFake()
    {
        var settings = new AiProviderSettings { Enabled = true, Provider = AiProviderSettings.ProviderFake };
        Fixture.RejectSecretReads();
        var health = await Fixture.HealthReporter(settings).CheckAsync(settings, CancellationToken.None);

        await Assert.That(health.Healthy).IsTrue();
        await Assert.That(health.Status).IsEqualTo("healthy_fake");
    }

    [Test]
    public async Task Check_WhenOpenAiCompatibleConfigured_ReturnsConfiguredNoProbe()
    {
        var settings = Settings(AiProviderSettings.ProviderOpenAiCompatible, "https://ai.example.test/v1", "gpt-test");
        var health = await Fixture.HealthReporter(settings).CheckAsync(settings, CancellationToken.None);

        await Assert.That(health.Healthy).IsTrue();
        await Assert.That(health.Status).IsEqualTo("configured_no_probe");
        await Assert.That(health.Data["credentialConfigured"]).IsEqualTo(true);
    }

    [Test]
    public async Task Check_WhenOpenAiConfigured_ReturnsConfiguredNoProbe()
    {
        var settings = Settings(AiProviderSettings.ProviderOpenAi, "", "gpt-test");
        var health = await Fixture.HealthReporter(settings).CheckAsync(settings, CancellationToken.None);

        await Assert.That(health.Healthy).IsTrue();
        await Assert.That(health.Status).IsEqualTo("configured_no_probe");
    }

    [Test]
    public async Task Check_WhenAnthropicConfigured_ReturnsConfiguredNoProbe()
    {
        var settings = Settings(AiProviderSettings.ProviderAnthropic, "", "claude-test");
        var health = await Fixture.HealthReporter(settings).CheckAsync(settings, CancellationToken.None);

        await Assert.That(health.Healthy).IsTrue();
        await Assert.That(health.Status).IsEqualTo("configured_no_probe");
    }

    [Test]
    public async Task Check_WhenAzureOpenAiConfigured_ReturnsConfiguredNoProbeWithoutSecrets()
    {
        var settings = Settings(AiProviderSettings.ProviderAzureOpenAi, "https://ai.example.openai.azure.com/", "deployment-test");
        var health = await Fixture.HealthReporter(settings).CheckAsync(settings, CancellationToken.None);

        await Assert.That(health.Healthy).IsTrue();
        await Assert.That(health.Status).IsEqualTo("configured_no_probe");
        await Assert.That(health.Data.Keys).DoesNotContain("endpointUrl");
        await Assert.That(health.Data.Keys).DoesNotContain("apiKey");
        await Assert.That(health.Data.Keys).DoesNotContain("modelId");
        await Assert.That(health.Data["endpointConfigured"]).IsEqualTo(true);
        await Assert.That(health.Data["credentialConfigured"]).IsEqualTo(true);
        await Assert.That(health.Data["modelConfigured"]).IsEqualTo(true);
        await Assert.That(string.Join('|', health.Data.Values)).DoesNotContain(Fixture.Credential);
    }

    [Test]
    public async Task Check_WhenEndpointUnsafe_ReturnsInvalidSettingsWithoutLeakingEndpointOrKey()
    {
        var settings = Settings(AiProviderSettings.ProviderOpenAiCompatible,
            $"https://user:{Guid.CreateVersion7():N}@ai.example.test/v1?prompt=leak", "gpt-test");
        var health = await Fixture.HealthReporter(settings).CheckAsync(settings, CancellationToken.None);

        await Assert.That(health.Healthy).IsFalse();
        await Assert.That(health.Status).IsEqualTo("invalid_settings");
        await Assert.That(health.Data.Keys).DoesNotContain("endpointUrl");
        await Assert.That(health.Data.Keys).DoesNotContain("apiKey");
        await Assert.That(health.Data.Keys).DoesNotContain("modelId");
        await Assert.That(health.Data.Keys).DoesNotContain("prompt");
        await Assert.That(health.Data["endpointConfigured"]).IsEqualTo(true);
        // A posted reference is not evidence of a resolved credential.
        await Assert.That(health.Data["credentialConfigured"]).IsEqualTo(false);
        await Assert.That(health.Data["modelConfigured"]).IsEqualTo(true);
        await Assert.That(string.Join('|', health.Data.Values)).DoesNotContain(settings.EndpointUrl);
        await Assert.That(string.Join('|', health.Data.Values)).DoesNotContain(Fixture.Credential);
    }

    [Test]
    [Arguments(SecretResolutionStatus.Unconfigured, "credential_unconfigured")]
    [Arguments(SecretResolutionStatus.Unavailable, "credential_unavailable")]
    [Arguments(SecretResolutionStatus.Unauthorized, "credential_unauthorized")]
    [Arguments(SecretResolutionStatus.Invalid, "credential_invalid")]
    public async Task Check_SelectedCredentialFailureCannotReportConfigured(SecretResolutionStatus status, string failure)
    {
        var settings = Settings(AiProviderSettings.ProviderOpenAi, "", "gpt-test");
        Fixture.SetSecret(SecretDefinitionRegistry.Keys.Ai.OpenAiApiKey, SecretScope.Instance, null, "",
            status switch
            {
                SecretResolutionStatus.Unconfigured => SecretResolutionResult.Unconfigured,
                SecretResolutionStatus.Unavailable => SecretResolutionResult.Unavailable,
                SecretResolutionStatus.Unauthorized => SecretResolutionResult.Unauthorized,
                _ => SecretResolutionResult.Invalid
            });

        var health = await Fixture.HealthReporter(settings, bindCredential: false).CheckAsync(settings, CancellationToken.None);

        await Assert.That(health.Healthy).IsFalse();
        await Assert.That(health.Status).IsEqualTo(failure);
        await Assert.That(health.Data["reason"]).IsEqualTo(failure);
        await Assert.That(health.Data["credentialConfigured"]).IsEqualTo(false);
    }

    [Test]
    public async Task Check_AzureDefaultCredentialWithExplicitNoneRemainsConfiguredWithoutApiKey()
    {
        var settings = Settings(AiProviderSettings.ProviderAzureOpenAi, "https://ai.example.openai.azure.com/", "deployment-test");
        settings.AzureCredentialMode = AiProviderSettings.AzureCredentialModeDefaultAzureCredential;
        settings.CredentialScope = AiCredentialScope.None;
        Fixture.RejectSecretReads();

        var health = await Fixture.HealthReporter(settings).CheckAsync(settings, CancellationToken.None);

        await Assert.That(health.Healthy).IsTrue();
        await Assert.That(health.Status).IsEqualTo("configured_no_probe");
        await Assert.That(health.Data["credentialConfigured"]).IsEqualTo(false);
    }

    private static AiProviderSettings Settings(int provider, string endpoint, string model) => new()
    {
        Enabled = true,
        Provider = provider,
        EndpointUrl = endpoint,
        CredentialScope = AiCredentialScope.Instance,
        ModelId = model
    };
}
