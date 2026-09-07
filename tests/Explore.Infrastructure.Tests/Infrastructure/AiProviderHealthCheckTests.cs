using System.Diagnostics.Metrics;
using Explore.Application.Contracts.Infrastructure.Ai;
using Explore.Application.Telemetry;
using Explore.Infrastructure.Ai;
using Explore.Infrastructure.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure;

public sealed class AiProviderHealthCheckTests
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
        var healthCheck = CreateHealthCheck(new AiProviderSettings
        {
            Enabled = true,
            Provider = AiProviderSettings.ProviderOpenAiCompatible,
            EndpointUrl = "https://user:password@ai.example.test/v1",
            ApiKey = "secret-key",
            ModelId = "gpt-test"
        });

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Unhealthy);
        await Assert.That(result.Data.Keys).DoesNotContain("endpointUrl");
        await Assert.That(result.Data.Keys).DoesNotContain("apiKey");
        await Assert.That(result.Data.Keys).DoesNotContain("modelId");
        await Assert.That(result.Data["apiKeyConfigured"]).IsEqualTo(true);
    }

    private static AiProviderHealthCheck CreateHealthCheck(AiProviderSettings settings)
    {
        var meterFactory = Substitute.For<IMeterFactory>();
        meterFactory.Create(Arg.Any<MeterOptions>()).Returns(new Meter(BusinessMetrics.MeterName));

        var validator = new AiProviderSettingsValidator();
        var strategies = new IAiProviderStrategy[]
        {
            new FakeAiProviderStrategy(new FakeAiChatProvider()),
            new StubProviderStrategy(AiProviderSettings.ProviderOpenAiCompatible, "configured_no_probe",
                "OpenAI-compatible AI provider settings are valid; network probing is deferred to the adapter."),
        };
        var resolver = new AiProviderStrategyResolver(
            strategies, Substitute.For<ILogger<AiProviderStrategyResolver>>());

        return new AiProviderHealthCheck(
            Options.Create(settings),
            new AiProviderHealthReporter(validator, resolver),
            new BusinessMetrics(meterFactory));
    }

    private sealed class StubProviderStrategy : IAiProviderStrategy
    {
        private readonly int _providerId;
        private readonly string _healthStatus;
        private readonly string _healthDescription;

        public StubProviderStrategy(int providerId, string healthStatus, string healthDescription)
        {
            _providerId = providerId;
            _healthStatus = healthStatus;
            _healthDescription = healthDescription;
        }

        public int ProviderId => _providerId;
        public bool SupportsProvider(int providerId) => providerId == _providerId;
        public Task<AiChatProviderResult> SendAsync(AiChatPayload request, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<AiModelDescriptor>> ListAvailableModelsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AiModelDescriptor>>(Array.Empty<AiModelDescriptor>());
        public AiProviderHealth CheckHealth(IReadOnlyDictionary<string, object> data) =>
            new(true, true, _healthStatus, _healthDescription, data);
    }
}
