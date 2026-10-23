using Explore.Domain.Ai;
using Explore.Infrastructure.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Tests.Infrastructure;

public sealed class AiProviderHealthCheckTests : AiProviderTestBase
{
    [Test]
    public async Task CheckHealthAsync_WhenDisabled_ReturnsHealthy()
    {
        var healthCheck = CreateHealthCheck(new AiProviderSettings());

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Healthy);
        await Assert.That(result.Description).Contains("intentionally disabled");
        await Assert.That(result.Data["enabled"]).IsEqualTo(false);
    }

    [Test]
    public async Task CheckHealthAsync_WhenSettingsInvalid_ReturnsUnhealthyWithoutSecrets()
    {
        var settings = new AiProviderSettings
        {
            Enabled = true,
            Provider = AiProviderSettings.ProviderOpenAiCompatible,
            EndpointUrl = $"https://user:{Guid.CreateVersion7():N}@ai.example.test/v1",
            CredentialScope = AiCredentialScope.Instance,
            ModelId = "gpt-test"
        };
        var healthCheck = CreateHealthCheck(settings);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Unhealthy);
        await Assert.That(result.Data.Keys).DoesNotContain("endpointUrl");
        await Assert.That(result.Data.Keys).DoesNotContain("apiKey");
        await Assert.That(result.Data.Keys).DoesNotContain("modelId");
        await Assert.That(result.Data["credentialConfigured"]).IsEqualTo(false);
        await Assert.That(string.Join('|', result.Data.Values)).DoesNotContain(Fixture.Credential);
        await Assert.That(string.Join('|', result.Data.Values)).DoesNotContain(settings.EndpointUrl);
    }

    private AiProviderHealthCheck CreateHealthCheck(AiProviderSettings settings) =>
        new(Options.Create(settings), Fixture.HealthReporter(settings), Fixture.Metrics);
}
