using Explore.Domain.Ai;
using Explore.Infrastructure.Ai;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure;

public sealed class AiProviderSettingsValidatorTests : AiProviderTestBase
{
    private readonly AiProviderSettingsValidator _validator = CreateValidator();

    [Test]
    public async Task Validate_DefaultSettingsReturnsSuccess()
    {
        var result = _validator.Validate(null, new AiProviderSettings());

        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task Validate_FakeProviderDoesNotRequireEndpointModelOrKey()
    {
        var result = _validator.Validate(null, new AiProviderSettings
        {
            Enabled = true,
            Provider = AiProviderSettings.ProviderFake
        });

        await Assert.That(result.Succeeded).IsTrue();
    }

    private static AiProviderSettingsValidator CreateValidator()
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns("Testing");
        return new AiProviderSettingsValidator(environment);
    }

    [Test]
    public async Task Validate_OpenAiCompatibleRequiresEndpointAndModel()
    {
        var result = _validator.Validate(null, new AiProviderSettings
        {
            Enabled = true,
            Provider = AiProviderSettings.ProviderOpenAiCompatible
        });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureMessage).Contains("EndpointUrl");
        await Assert.That(result.FailureMessage).Contains("ModelId");

        var configured = _validator.Validate(null, new AiProviderSettings
        {
            Enabled = true,
            Provider = AiProviderSettings.ProviderOpenAiCompatible,
            EndpointUrl = "https://ai.example.test/v1",
            ModelId = "gpt-test"
        });

        await Assert.That(configured.Succeeded).IsTrue();
    }

    [Test]
    public async Task Validate_OpenAiRequiresModelAndCredentialReferenceButNotEndpoint()
    {
        var missing = _validator.Validate(null, new AiProviderSettings
        {
            Enabled = true,
            Provider = AiProviderSettings.ProviderOpenAi
        });

        await Assert.That(missing.Succeeded).IsFalse();
        await Assert.That(missing.FailureMessage).Contains("ModelId");
        var settings = new AiProviderSettings
        {
            Enabled = true,
            Provider = AiProviderSettings.ProviderOpenAi,
            ModelId = "gpt-test",
            CredentialScope = AiCredentialScope.None
        };
        var absentReference = await Fixture.Configuration(settings).ResolveInstanceAsync(settings, CancellationToken.None);
        await Assert.That(absentReference.FailureCode).IsEqualTo("credential_scope_invalid");

        var configured = _validator.Validate(null, new AiProviderSettings
        {
            Enabled = true,
            Provider = AiProviderSettings.ProviderOpenAi,
            ModelId = "gpt-test",
            CredentialScope = AiCredentialScope.Instance
        });

        await Assert.That(configured.Succeeded).IsTrue();
    }

    [Test]
    public async Task Validate_AnthropicRequiresModelAndCredentialReferenceButNotEndpoint()
    {
        var missing = _validator.Validate(null, new AiProviderSettings
        {
            Enabled = true,
            Provider = AiProviderSettings.ProviderAnthropic
        });

        await Assert.That(missing.Succeeded).IsFalse();
        await Assert.That(missing.FailureMessage).Contains("ModelId");
        var settings = new AiProviderSettings
        {
            Enabled = true,
            Provider = AiProviderSettings.ProviderAnthropic,
            ModelId = "claude-test",
            CredentialScope = AiCredentialScope.None
        };
        var absentReference = await Fixture.Configuration(settings).ResolveInstanceAsync(settings, CancellationToken.None);
        await Assert.That(absentReference.FailureCode).IsEqualTo("credential_scope_invalid");

        var configured = _validator.Validate(null, new AiProviderSettings
        {
            Enabled = true,
            Provider = AiProviderSettings.ProviderAnthropic,
            ModelId = "claude-test",
            CredentialScope = AiCredentialScope.Instance
        });

        await Assert.That(configured.Succeeded).IsTrue();
    }

    [Test]
    public async Task Validate_AzureOpenAiSupportsDefaultAzureCredentialWithoutApiKey()
    {
        var result = _validator.Validate(null, new AiProviderSettings
        {
            Enabled = true,
            Provider = AiProviderSettings.ProviderAzureOpenAi,
            EndpointUrl = "https://ai.example.openai.azure.com/",
            ModelId = "deployment-test",
            CredentialScope = AiCredentialScope.None,
            AzureCredentialMode = AiProviderSettings.AzureCredentialModeDefaultAzureCredential
        });

        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task Validate_AzureOpenAiApiKeyModeRequiresInstanceReference()
    {
        var settings = new AiProviderSettings
        {
            Enabled = true,
            Provider = AiProviderSettings.ProviderAzureOpenAi,
            EndpointUrl = "https://ai.example.openai.azure.com/",
            ModelId = "deployment-test",
            CredentialScope = AiCredentialScope.None,
            AzureCredentialMode = AiProviderSettings.AzureCredentialModeApiKey
        };
        var result = await Fixture.Configuration(settings).ResolveInstanceAsync(settings, CancellationToken.None);

        await Assert.That(result.Configuration).IsNull();
        await Assert.That(result.FailureCode).IsEqualTo("credential_scope_invalid");
    }

    [Test]
    public async Task Validate_AzureOpenAiRequiresHttpsEndpoint()
    {
        var result = _validator.Validate(null, new AiProviderSettings
        {
            Enabled = true,
            Provider = AiProviderSettings.ProviderAzureOpenAi,
            EndpointUrl = "http://ai.example.openai.azure.com/",
            ModelId = "deployment-test",
            CredentialScope = AiCredentialScope.Instance
        });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureMessage).Contains("HTTPS");
    }

    [Test]
    public async Task Validate_OpenAiCompatibleAcceptsSafeHttpEndpoint()
    {
        var result = _validator.Validate(null, new AiProviderSettings
        {
            Enabled = true,
            Provider = AiProviderSettings.ProviderOpenAiCompatible,
            EndpointUrl = "https://ai.example.test/v1",
            ModelId = "gpt-test",
            CredentialScope = AiCredentialScope.Instance
        });

        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task Validate_EndpointWithEmbeddedCredentialsReturnsFailure()
    {
        var result = _validator.Validate(null, new AiProviderSettings
        {
            Enabled = true,
            Provider = AiProviderSettings.ProviderOpenAiCompatible,
            EndpointUrl = $"https://user:{Guid.CreateVersion7():N}@ai.example.test/v1",
            ModelId = "gpt-test",
            CredentialScope = AiCredentialScope.Instance
        });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureMessage).Contains("embedded credentials");
    }

    [Test]
    public async Task Validate_DisabledOpenAiCompatibleDoesNotRequireEndpointModelOrKey()
    {
        var result = _validator.Validate(null, new AiProviderSettings
        {
            Enabled = false,
            Provider = AiProviderSettings.ProviderOpenAiCompatible
        });

        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task Validate_LocalEndpointRequiresExplicitOptIn()
    {
        var result = _validator.Validate(null, new AiProviderSettings
        {
            Enabled = true,
            Provider = AiProviderSettings.ProviderOpenAiCompatible,
            EndpointUrl = "http://localhost:11434/v1",
            ModelId = "local-model",
            CredentialScope = AiCredentialScope.Instance
        });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureMessage).Contains("local, loopback, link-local, or private network hosts");
    }

    [Test]
    public async Task Validate_LocalEndpointCanBeExplicitlyAllowed()
    {
        var result = _validator.Validate(null, new AiProviderSettings
        {
            Enabled = true,
            Provider = AiProviderSettings.ProviderOpenAiCompatible,
            EndpointUrl = "http://localhost:11434/v1",
            ModelId = "local-model",
            CredentialScope = AiCredentialScope.Instance,
            AllowLocalProviderEndpoints = true
        });

        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task Validate_QueryStringEndpointReturnsFailure()
    {
        var result = _validator.Validate(null, new AiProviderSettings
        {
            Enabled = true,
            Provider = AiProviderSettings.ProviderOpenAiCompatible,
            EndpointUrl = "https://ai.example.test/v1?tenant=browser-controlled",
            ModelId = "gpt-test",
            CredentialScope = AiCredentialScope.Instance
        });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureMessage).Contains("query strings or fragments");
    }

    [Test]
    public async Task Validate_InvalidLimitsReturnFailure()
    {
        var result = _validator.Validate(null, new AiProviderSettings
        {
            MaxInputTokens = 0,
            MaxOutputTokens = 0,
            Temperature = 3m,
            TimeoutSeconds = 0,
            RetentionDays = -1,
            DailyMessageLimit = 0
        });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureMessage).Contains("MaxInputTokens");
        await Assert.That(result.FailureMessage).Contains("MaxOutputTokens");
        await Assert.That(result.FailureMessage).Contains("Temperature");
        await Assert.That(result.FailureMessage).Contains("TimeoutSeconds");
        await Assert.That(result.FailureMessage).Contains("RetentionDays");
        await Assert.That(result.FailureMessage).Contains("DailyMessageLimit");
    }
}
