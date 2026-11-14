using Explore.API.Configuration;
using Explore.API.HealthChecks;
using Explore.API.Mcp;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Settings.Groups;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Settings;
using Event.Api.IntegrationTests.Authentication;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using TUnit.Core;

namespace ApiIntegrationTests.Features;

[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class McpAdapterHealthCheckTests(PostgreSqlContainerFixture postgres)
{
    [Test]
    public async Task CheckHealthWithoutTenantUsesInstanceReadAuthority()
    {
        await using var host = new TenantBrandingNativeAuthenticationHost(
            postgres, Guid.CreateVersion7(), [], configureServices: services =>
            {
                services.RemoveAll<ExploreDbContext>();
                services.AddScoped(_ =>
                {
                    var context = postgres.CreateEnrolledTenantlessDbContext();
                    context.BindTenantLease();
                    return context;
                });
            });
        var healthCheck = new McpAdapterHealthCheck(
            Options.Create(new McpAdapterSettings
            {
                Enabled = false,
                EndpointPath = "/mcp",
                Stateless = true,
                EnableLegacySse = false
            }),
            host.Services.GetRequiredService<IServiceScopeFactory>());

        var result = await healthCheck.CheckHealthAsync(
            new HealthCheckContext(), TestContext.Current!.Execution.CancellationToken);

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Degraded);
        await Assert.That(result.Description).Contains("startup");
        await Assert.That(result.Data).ContainsKey("enabled").And.Value.IsEqualTo(false);
        await Assert.That(result.Data).ContainsKey("startupEnabled").And.Value.IsEqualTo(false);
        await Assert.That(result.Data).ContainsKey("endpointPath").And.Value.IsEqualTo("/mcp");
        await Assert.That(result.Data).ContainsKey("stateless").And.Value.IsEqualTo(true);
        await AssertSafeKeys(result);
    }

    [Test]
    public async Task CheckHealthAsyncWhenMcpStartupDisabledReturnsDegradedWithSafeData()
    {
        await WithHealthCheckAsync(new McpRuntimeState(
            StartupEnabled: false,
            RuntimeEnabled: true,
            EffectiveEnabled: false,
            StartupLegacySseCeiling: false,
            RuntimeLegacySseRequested: false,
            LegacySseRuntimeEnabled: false,
            TenantOverrideAllowed: false,
            TenantLegacySseOverrideAllowed: false,
            TenantId: null), async healthCheck =>
        {

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
        });
    }

    [Test]
    public async Task CheckHealthAsyncWhenMcpRuntimeDisabledReturnsDegraded()
    {
        await WithHealthCheckAsync(new McpRuntimeState(
            StartupEnabled: true,
            RuntimeEnabled: false,
            EffectiveEnabled: false,
            StartupLegacySseCeiling: false,
            RuntimeLegacySseRequested: false,
            LegacySseRuntimeEnabled: false,
            TenantOverrideAllowed: false,
            TenantLegacySseOverrideAllowed: false,
            TenantId: null), async healthCheck =>
        {

            var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

            await Assert.That(result.Status).IsEqualTo(HealthStatus.Degraded);
            await Assert.That(result.Description).Contains("runtime");
            await Assert.That(result.Data).ContainsKey("enabled").And.Value.IsEqualTo(false);
            await Assert.That(result.Data).ContainsKey("startupEnabled").And.Value.IsEqualTo(true);
            await Assert.That(result.Data).ContainsKey("runtimeEnabled").And.Value.IsEqualTo(false);
            await AssertSafeKeys(result);
        });
    }

    [Test]
    public async Task CheckHealthAsyncWhenMcpEnabledReturnsHealthy()
    {
        await WithHealthCheckAsync(new McpRuntimeState(
            StartupEnabled: true,
            RuntimeEnabled: true,
            EffectiveEnabled: true,
            StartupLegacySseCeiling: true,
            RuntimeLegacySseRequested: true,
            LegacySseRuntimeEnabled: false,
            TenantOverrideAllowed: false,
            TenantLegacySseOverrideAllowed: false,
            TenantId: null), async healthCheck =>
        {

            var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

            await Assert.That(result.Status).IsEqualTo(HealthStatus.Healthy);
            await Assert.That(result.Description).Contains("stateless Streamable HTTP");
            await Assert.That(result.Data).ContainsKey("enabled").And.Value.IsEqualTo(true);
            await Assert.That(result.Data).ContainsKey("legacySseRuntimeRequested").And.Value.IsEqualTo(true);
            await Assert.That(result.Data).ContainsKey("legacySseRuntimeEnabled").And.Value.IsEqualTo(false);
            await AssertSafeKeys(result);
        });
    }

    private async Task WithHealthCheckAsync(McpRuntimeState state, Func<McpAdapterHealthCheck, Task> verify)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var options = new DbContextOptionsBuilder<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options,
            postgres.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
        await using var seed = new ExploreDbContext(options.Options);
        seed.BindTenantLease();
        var keys = McpSettingGroup.SettingKeys.ToArray();
        var before = await seed.SystemSettings.AsNoTracking()
            .Where(row => keys.Contains(row.SettingKey)).ToArrayAsync(token);
        try
        {
            var tracked = await seed.SystemSettings.Where(row => keys.Contains(row.SettingKey))
                .ToDictionaryAsync(row => row.SettingKey, token);
            foreach (string key in keys)
            {
                string value = (key == GovernanceSettingKeys.Mcp.Enabled
                    ? state.RuntimeEnabled : state.RuntimeLegacySseRequested) ? "true" : "false";
                if (!tracked.TryGetValue(key, out var row))
                {
                    row = new SystemSetting
                    {
                        SettingKey = key, Value = value, ValueType = SettingValueType.Boolean
                    };
                    seed.SystemSettings.Add(row);
                }
                row.Value = value;
            }
            await seed.SaveChangesAsync(token);
            var services = new ServiceCollection();
            services.AddSingleton<IGlobalExploreDbContextFactory>(
                new GlobalExploreDbContextFactory(postgres.GetNativeQueueOptions(PrimaryDatabaseRole.Global)));
            services.AddSingleton(postgres.NativeQueueEnrollment);
            services.AddScoped<IInstanceSystemSettingsReader, InstanceSystemSettingsReader>();
            await using var provider = services.BuildServiceProvider();
            await verify(new McpAdapterHealthCheck(Options.Create(new McpAdapterSettings
            {
                Enabled = state.StartupEnabled,
                EndpointPath = "/mcp",
                Stateless = true,
                EnableLegacySse = state.StartupLegacySseCeiling
            }),
                provider.GetRequiredService<IServiceScopeFactory>()));
        }
        finally
        {
            seed.ChangeTracker.Clear();
            var current = await seed.SystemSettings.Where(row => keys.Contains(row.SettingKey))
                .ToArrayAsync(CancellationToken.None);
            foreach (var row in current)
            {
                var previous = before.SingleOrDefault(value => value.SettingKey == row.SettingKey);
                if (previous is null)
                    seed.SystemSettings.Remove(row);
                else
                    seed.Entry(row).CurrentValues.SetValues(previous);
            }
            await seed.SaveChangesAsync(CancellationToken.None);
        }
    }

    private static async Task AssertSafeKeys(HealthCheckResult result)
    {
        await Assert.That(result.Data.Keys).DoesNotContain(key => key.Contains("tenant", StringComparison.OrdinalIgnoreCase));
        await Assert.That(result.Data.Keys).DoesNotContain(key => key.Contains("prompt", StringComparison.OrdinalIgnoreCase));
        await Assert.That(result.Data.Keys).DoesNotContain(key => key.Contains("payload", StringComparison.OrdinalIgnoreCase));
        await Assert.That(result.Data.Keys).DoesNotContain(key => key.Contains("apiKey", StringComparison.OrdinalIgnoreCase));
    }
}
