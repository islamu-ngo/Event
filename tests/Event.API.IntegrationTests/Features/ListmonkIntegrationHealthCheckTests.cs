using Event.Api.IntegrationTests.Authentication;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Settings;
using Explore.Infrastructure.HealthChecks;
using Explore.Persistence;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using TUnit.Core;

namespace ApiIntegrationTests.Features;

[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class ListmonkIntegrationHealthCheckTests(PostgreSqlContainerFixture postgres)
{
    [Test]
    public async Task CheckHealthWithoutTenantUsesInstanceReadAuthority()
    {
        await using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();
        var healthCheck = ActivatorUtilities.CreateInstance<ListmonkIntegrationHealthCheck>(
            scope.ServiceProvider);

        var result = await healthCheck.CheckHealthAsync(
            new HealthCheckContext(), TestContext.Current!.Execution.CancellationToken);

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Healthy);
        await Assert.That(result.Description).Contains("disabled");
        await Assert.That(result.Data).ContainsKey("enabled").And.Value.IsEqualTo(false);
        await Assert.That(result.Data).ContainsKey("syncOnRegistration").And.Value.IsEqualTo(false);
        await Assert.That(result.Data).ContainsKey("preconfirmSubscriptions").And.Value.IsEqualTo(true);
        await Assert.That(result.Data.Keys).IsEquivalentTo(new[]
        {
            "enabled", "syncOnRegistration", "preconfirmSubscriptions",
            "instanceUrlConfigured", "defaultListIdConfigured", "apiUsernameResolved",
            "apiKeyResolved", "apiUsernameState", "apiKeyState"
        });
    }

    [Test]
    public async Task EnabledInstanceHealthUsesStoredValuesAndReportsIncompleteConfigurationSafely()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var options = new DbContextOptionsBuilder<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options,
            postgres.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
        await using var seed = new ExploreDbContext(options.Options);
        seed.BindTenantLease();
        var settings = new (string Key, string Value, SettingValueType Type)[]
        {
            (GovernanceSettingKeys.Integrations.Listmonk.Enabled, "true", SettingValueType.Boolean),
            (GovernanceSettingKeys.Integrations.Listmonk.SyncOnRegistration, "true", SettingValueType.Boolean),
            (GovernanceSettingKeys.Integrations.Listmonk.PreconfirmSubscriptions, "false", SettingValueType.Boolean),
            (GovernanceSettingKeys.Integrations.Listmonk.InstanceUrl, "\"\"", SettingValueType.String),
            (GovernanceSettingKeys.Integrations.Listmonk.DefaultListId, "17", SettingValueType.Integer)
        };
        var keys = settings.Select(setting => setting.Key).ToArray();
        var before = await seed.SystemSettings.AsNoTracking()
            .Where(row => keys.Contains(row.SettingKey)).ToArrayAsync(token);
        try
        {
            var tracked = await seed.SystemSettings.Where(row => keys.Contains(row.SettingKey))
                .ToDictionaryAsync(row => row.SettingKey, token);
            foreach (var setting in settings)
            {
                if (!tracked.TryGetValue(setting.Key, out var row))
                {
                    row = new SystemSetting
                    {
                        SettingKey = setting.Key,
                        Value = setting.Value,
                        ValueType = setting.Type
                    };
                    seed.SystemSettings.Add(row);
                }
                row.Value = setting.Value;
            }
            await seed.SaveChangesAsync(token);
            await using var host = CreateHost();
            await using var scope = host.Services.CreateAsyncScope();
            var healthCheck = ActivatorUtilities.CreateInstance<ListmonkIntegrationHealthCheck>(
                scope.ServiceProvider);

            var result = await healthCheck.CheckHealthAsync(new HealthCheckContext(), token);

            await Assert.That(result.Status).IsEqualTo(HealthStatus.Degraded);
            await Assert.That(result.Data).ContainsKey("enabled").And.Value.IsEqualTo(true);
            await Assert.That(result.Data).ContainsKey("syncOnRegistration").And.Value.IsEqualTo(true);
            await Assert.That(result.Data).ContainsKey("preconfirmSubscriptions").And.Value.IsEqualTo(false);
            await Assert.That(result.Data).ContainsKey("instanceUrlConfigured").And.Value.IsEqualTo(false);
            await Assert.That(result.Data).ContainsKey("defaultListIdConfigured").And.Value.IsEqualTo(true);
            await Assert.That(result.Data.Keys).IsEquivalentTo(new[]
            {
                "enabled", "syncOnRegistration", "preconfirmSubscriptions",
                "instanceUrlConfigured", "defaultListIdConfigured", "apiUsernameResolved",
                "apiKeyResolved", "apiUsernameState", "apiKeyState"
            });
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

    private TenantBrandingNativeAuthenticationHost CreateHost() =>
        new(postgres, Guid.CreateVersion7(), [], configureServices: services =>
        {
            services.RemoveAll<ExploreDbContext>();
            services.AddScoped(_ =>
            {
                var context = postgres.CreateEnrolledTenantlessDbContext();
                context.BindTenantLease();
                return context;
            });
        });
}
