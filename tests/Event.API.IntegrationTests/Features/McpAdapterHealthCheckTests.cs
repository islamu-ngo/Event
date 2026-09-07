using Explore.API.Configuration;
using Explore.API.HealthChecks;
using Explore.API.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using NSubstitute;
using TUnit.Core;

namespace ApiIntegrationTests.Features;

public sealed class McpAdapterHealthCheckTests
{
    [Test]
    public async Task CheckHealthAsyncWhenMcpStartupDisabledReturnsDegradedWithSafeData()
    {
        var healthCheck = CreateHealthCheck(new McpRuntimeState(
            StartupEnabled: false,
            RuntimeEnabled: true,
            EffectiveEnabled: false,
            StartupLegacySseCeiling: false,
            RuntimeLegacySseRequested: false,
            LegacySseRuntimeEnabled: false,
            TenantOverrideAllowed: false,
            TenantLegacySseOverrideAllowed: false,
            TenantId: null));

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Degraded);
        await Assert.That(result.Description).Contains("startup");
        await Assert.That(result.Data).ContainsKey("enabled").And.Value.IsEqualTo(false);
        await Assert.That(result.Data).ContainsKey("startupEnabled").And.Value.IsEqualTo(false);
        await Assert.That(result.Data).ContainsKey("runtimeEnabled").And.Value.IsEqualTo(true);
        await Assert.That(result.Data).ContainsKey("endpointPath").And.Value.IsEqualTo("/mcp");
        await Assert.That(result.Data).ContainsKey("stateless").And.Value.IsEqualTo(true);
        await Assert.That(result.Data).ContainsKey("legacySseStartupCeiling").And.Value.IsEqualTo(false);
        await Assert.That(result.Data).ContainsKey("legacySseRuntimeRequested").And.Value.IsEqualTo(false);
        await Assert.That(result.Data).ContainsKey("legacySseRuntimeEnabled").And.Value.IsEqualTo(false);
        await AssertSafeKeys(result);
    }

    [Test]
    public async Task CheckHealthAsyncWhenMcpRuntimeDisabledReturnsDegraded()
    {
        var healthCheck = CreateHealthCheck(new McpRuntimeState(
            StartupEnabled: true,
            RuntimeEnabled: false,
            EffectiveEnabled: false,
            StartupLegacySseCeiling: false,
            RuntimeLegacySseRequested: false,
            LegacySseRuntimeEnabled: false,
            TenantOverrideAllowed: false,
            TenantLegacySseOverrideAllowed: false,
            TenantId: null));

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Degraded);
        await Assert.That(result.Description).Contains("runtime");
        await Assert.That(result.Data).ContainsKey("enabled").And.Value.IsEqualTo(false);
        await Assert.That(result.Data).ContainsKey("startupEnabled").And.Value.IsEqualTo(true);
        await Assert.That(result.Data).ContainsKey("runtimeEnabled").And.Value.IsEqualTo(false);
        await AssertSafeKeys(result);
    }

    [Test]
    public async Task CheckHealthAsyncWhenMcpEnabledReturnsHealthy()
    {
        var healthCheck = CreateHealthCheck(new McpRuntimeState(
            StartupEnabled: true,
            RuntimeEnabled: true,
            EffectiveEnabled: true,
            StartupLegacySseCeiling: true,
            RuntimeLegacySseRequested: true,
            LegacySseRuntimeEnabled: false,
            TenantOverrideAllowed: false,
            TenantLegacySseOverrideAllowed: false,
            TenantId: null));

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Healthy);
        await Assert.That(result.Description).Contains("stateless Streamable HTTP");
        await Assert.That(result.Data).ContainsKey("enabled").And.Value.IsEqualTo(true);
        await Assert.That(result.Data).ContainsKey("legacySseRuntimeRequested").And.Value.IsEqualTo(true);
        await Assert.That(result.Data).ContainsKey("legacySseRuntimeEnabled").And.Value.IsEqualTo(false);
        await AssertSafeKeys(result);
    }

    private static McpAdapterHealthCheck CreateHealthCheck(McpRuntimeState state)
    {
        var runtimeStateService = Substitute.For<IMcpRuntimeStateService>();
        runtimeStateService.GetAsync(null, Arg.Any<CancellationToken>()).Returns(state);

        var services = new ServiceCollection();
        services.AddScoped(_ => runtimeStateService);
        var provider = services.BuildServiceProvider();

        return new McpAdapterHealthCheck(
            Options.Create(new McpAdapterSettings
            {
                Enabled = state.StartupEnabled,
                EndpointPath = "/mcp",
                Stateless = true,
                EnableLegacySse = state.StartupLegacySseCeiling
            }),
            provider.GetRequiredService<IServiceScopeFactory>());
    }

    private static async Task AssertSafeKeys(HealthCheckResult result)
    {
        await Assert.That(result.Data.Keys).DoesNotContain(key => key.Contains("tenant", StringComparison.OrdinalIgnoreCase));
        await Assert.That(result.Data.Keys).DoesNotContain(key => key.Contains("prompt", StringComparison.OrdinalIgnoreCase));
        await Assert.That(result.Data.Keys).DoesNotContain(key => key.Contains("payload", StringComparison.OrdinalIgnoreCase));
        await Assert.That(result.Data.Keys).DoesNotContain(key => key.Contains("apiKey", StringComparison.OrdinalIgnoreCase));
    }
}
