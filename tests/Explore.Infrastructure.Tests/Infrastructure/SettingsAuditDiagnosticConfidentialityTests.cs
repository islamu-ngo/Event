using Explore.Persistence.Services;
using System.Text.Json;
using System.Net;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Notifications;
using Explore.Application.Notifications.Handlers;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Infrastructure.Analytics;
using Explore.Infrastructure.Localization;
using Explore.Infrastructure.Integrations.Listmonk;
using Explore.Infrastructure.Storage;
using Explore.Infrastructure.Services;
using Explore.Persistence.Repositories;
using Explore.Persistence.Database;
using Explore.Tests.Shared.Settings;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure;

public sealed class SettingsAuditDiagnosticConfidentialityTests
{
    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    public async Task NativeStorageResolutionPreservesConfigurationWithoutTenantOrCredentialDiagnostics(int resolvedStage)
    {
        await using var database = await SmtpSettingsDatabase.CreateAsync();
        string canary = Guid.NewGuid().ToString("N");
        string endpoint = "https://storage.example.test/" + canary;
        await database.SetInstanceAsync(GovernanceSettingKeys.Storage.Endpoint,
            resolvedStage >= 1 ? endpoint : "");
        await database.SetInstanceAsync(GovernanceSettingKeys.Storage.BucketName,
            resolvedStage >= 2 ? canary : "");
        if (resolvedStage >= 3)
            database.Secrets.ResolveAsync(SecretDefinitionRegistry.Keys.Storage.AccessKeyId,
                    database.TenantId, Arg.Any<CancellationToken>())
                .Returns(SecretResolutionResult.Resolved(new ResolvedSecret(
                    SecretDefinitionRegistry.Keys.Storage.AccessKeyId, canary,
                    SecretSourceType.EnvironmentVariable, SecretScope.Tenant, database.TenantId, DateTimeOffset.UtcNow)));
        if (resolvedStage >= 4)
            database.Secrets.ResolveAsync(SecretDefinitionRegistry.Keys.Storage.SecretAccessKey,
                    database.TenantId, Arg.Any<CancellationToken>())
                .Returns(SecretResolutionResult.Resolved(new ResolvedSecret(
                    SecretDefinitionRegistry.Keys.Storage.SecretAccessKey, canary,
                    SecretSourceType.EnvironmentVariable, SecretScope.Tenant, database.TenantId, DateTimeOffset.UtcNow)));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var logger = new TestListLogger<S3ConfigResolver>();
        var resolver = new S3ConfigResolver(database.Settings, database, cache, new S3ConfigCacheRegistry(), database.Secrets, logger);

        var configuration = await resolver.ResolveAsync();

        if (resolvedStage == 4)
        {
            await Assert.That(configuration).IsNotNull();
            await Assert.That(configuration!.Endpoint).IsEqualTo(endpoint);
            await Assert.That(configuration.BucketName).IsEqualTo(canary);
            await Assert.That(configuration.AccessKeyId).IsEqualTo(canary);
            await Assert.That(configuration.SecretAccessKey).IsEqualTo(canary);
            await Assert.That(logger.Entries).IsEmpty();
        }
        else
        {
            await Assert.That(configuration).IsNull();
            await Assert.That(logger.Entries.Count).IsEqualTo(1);
            await Assert.That(logger.Entries[0].Level).IsEqualTo(LogLevel.Debug);
        }
        await AssertPrivateValuesAbsent(logger.Entries, canary, database.TenantId.ToString());
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task NativeListmonkConnectionFailureOmitsProviderBodyCredentialsAndTenant(int failureKind)
    {
        await using var database = await SmtpSettingsDatabase.CreateAsync();
        string canary = Guid.NewGuid().ToString("N");
        await database.SetInstanceAsync(GovernanceSettingKeys.Integrations.Listmonk.InstanceUrl,
            "https://listmonk.example.test");
        foreach (string key in new[]
        {
            SecretDefinitionRegistry.Keys.Integrations.Listmonk.ApiUsername,
            SecretDefinitionRegistry.Keys.Integrations.Listmonk.ApiKey
        })
            database.Secrets.ResolveAsync(key, database.TenantId, Arg.Any<CancellationToken>())
                .Returns(SecretResolutionResult.Resolved(new ResolvedSecret(key, canary,
                    SecretSourceType.EnvironmentVariable, SecretScope.Tenant,
                    database.TenantId, DateTimeOffset.UtcNow)));
        using var transport = new ListmonkFailureTransport(canary, failureKind);
        var logger = new TestListLogger<ListmonkConnectionTester>();
        var tester = new ListmonkConnectionTester(database.Settings, database.Secrets,
            database, transport, logger);

        await Assert.That(await tester.TestConnectionAsync()).IsFalse();

        await Assert.That(transport.RequestCount).IsEqualTo(1);
        await Assert.That(logger.Entries.Count).IsEqualTo(1);
        await Assert.That(logger.Entries[0].Level).IsEqualTo(LogLevel.Warning);
        if (failureKind == 0)
            await Assert.That(logger.Entries[0].Arguments).Contains(503);
        await AssertPrivateValuesAbsent(logger.Entries, canary, database.TenantId.ToString());
    }

    [Test]
    public async Task DistributedCacheFailurePreservesNativeBootstrapFallbackWithoutExceptionExports()
    {
        await using var database = await SmtpSettingsDatabase.CreateAsync();
        string canary = Guid.NewGuid().ToString("N") + "\r\nprivate-cache-connection";
        var cache = Substitute.For<IDistributedCache>();
        cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<byte[]?>(new InvalidOperationException(canary)));
        cache.SetAsync(Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<DistributedCacheEntryOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException(canary)));
        cache.RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException(canary)));
        var services = new ServiceCollection();
        services.AddOptions<DeploymentSettings>();
        services.AddSingleton<IGlobalExploreDbContextFactory>(new GlobalExploreDbContextFactory(
            new PrimaryDatabaseConnectionOptions
            {
                Role = PrimaryDatabaseRole.Global,
                Provider = PrimaryDatabaseProvider.Sqlite,
                Database = database.Context.Database.GetDbConnection().DataSource
            }));
        services.AddSingleton<IInstanceBootstrapStateReader, InstanceBootstrapStateReader>();
        using var provider = services.BuildServiceProvider();
        var logger = new TestListLogger<DeploymentModeProvider>();
        var mode = new DeploymentModeProvider(provider.GetRequiredService<IOptionsMonitor<DeploymentSettings>>(),
            new ConfigurationBuilder().Build(), cache,
            provider.GetRequiredService<IServiceScopeFactory>(), logger);

        await Assert.That(await mode.GetCurrentModeAsync()).IsEqualTo(DeploymentMode.SingleTenant);
        await mode.InvalidateCacheAsync();

        await Assert.That(logger.Entries.Count(entry => entry.Level == LogLevel.Warning)).IsEqualTo(3);
        await AssertPrivateValuesAbsent(logger.Entries, canary[..32]);
    }

    [Test]
    public async Task SettingNotificationDiagnosticsOmitEvenUnregisteredPrivateValuesAndIdentities()
    {
        string canary = Guid.NewGuid().ToString("N") + "\r\nprivate-setting";
        Guid tenant = Guid.NewGuid();
        Guid actor = Guid.NewGuid();
        var logger = new TestListLogger<SettingAuditLogHandler>();
        var handler = new SettingAuditLogHandler(logger);

        await handler.HandleAsync(new SettingChangedNotification(
            canary, canary, canary, SettingSource.TenantOverride, tenant, actor, DateTime.UtcNow), default);
        await handler.HandleAsync(new SettingChangedNotification(
            canary, canary, canary, (SettingSource)999, tenant, actor, DateTime.UtcNow), default);

        await Assert.That(logger.Entries.Count).IsEqualTo(2);
        await Assert.That(logger.Entries[0].Arguments).Contains(nameof(SettingSource.TenantOverride));
        await Assert.That(logger.Entries[1].Arguments).Contains("unknown");
        await AssertPrivateValuesAbsent(logger.Entries, canary, tenant.ToString(), actor.ToString());
    }

    [Test]
    public async Task DurableConfigurationAuditRetainsBusinessDataWithoutDuplicatingItIntoDiagnostics()
    {
        await using var database = await SmtpSettingsDatabase.CreateAsync();
        var repository = new ConfigurationChangeLogRepository(database.Context);
        var logger = new TestListLogger<ConfigurationChangeLogService>();
        var service = new ConfigurationChangeLogService(repository, logger);
        string canary = Guid.NewGuid().ToString("N");
        string key = "private." + canary;
        string before = canary + "-before";
        string after = canary + "-after";
        string action = canary + "-action";

        await service.LogChangeAsync(database.ActorId, key, before, after,
            ConfigurationScopeEnum.Tenant, database.TenantId, action);

        var records = await repository.GetBySettingKey(key);
        await Assert.That(records.Count).IsEqualTo(1);
        await Assert.That(records[0].OldValue).IsEqualTo(before);
        await Assert.That(records[0].NewValue).IsEqualTo(after);
        await Assert.That(records[0].ActionType).IsEqualTo(action);
        await Assert.That(records[0].UserId).IsEqualTo(database.ActorId);
        await Assert.That(records[0].ScopeId).IsEqualTo(database.TenantId);
        await Assert.That(logger.Entries.Count).IsEqualTo(1);
        await Assert.That(logger.Entries[0].Arguments).Contains(nameof(ConfigurationScopeEnum.Tenant));
        await AssertPrivateValuesAbsent(logger.Entries, canary,
            database.ActorId.ToString(), database.TenantId.ToString());
    }

    [Test]
    public async Task RealHierarchyResolversPreserveSelectedConfigurationAndFallbackWithoutPrivateDiagnosticCopies()
    {
        await using var database = await SmtpSettingsDatabase.CreateAsync();
        string canary = Guid.NewGuid().ToString("N");
        await database.SetInstanceAsync(GovernanceSettingKeys.Analytics.Provider, "posthog");
        await database.SetInstanceAsync(GovernanceSettingKeys.Analytics.Enabled, true);
        await database.SetInstanceAsync(GovernanceSettingKeys.Analytics.ApiKey, canary);
        await database.SetInstanceAsync(GovernanceSettingKeys.Localization.TmsProvider, "tolgee");
        await database.SetInstanceAsync(GovernanceSettingKeys.Localization.DefaultLanguage, canary);
        await database.SetInstanceAsync(GovernanceSettingKeys.Localization.EnabledLanguages, "en," + canary + ",fr");
        await database.SetInstanceAsync(GovernanceSettingKeys.Localization.FallbackLanguage, canary);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var analyticsLog = new TestListLogger<AnalyticsConfigResolver>();
        var translationLog = new TestListLogger<TranslationConfigResolver>();
        var analytics = new AnalyticsConfigResolver(database.Settings, database, cache,
            database.Secrets, analyticsLog);
        var translation = new TranslationConfigResolver(database.Settings, database, cache, translationLog);

        var analyticsConfiguration = await analytics.ResolveAsync();
        var translationConfiguration = await translation.ResolveAsync();

        await Assert.That(analyticsConfiguration.Provider).IsEqualTo(AnalyticsProviderEnum.Posthog);
        await Assert.That(analyticsConfiguration.IsEnabled).IsTrue();
        await Assert.That(analyticsConfiguration.ApiKey).IsEqualTo(canary);
        await Assert.That(translationConfiguration.Provider).IsEqualTo(TranslationManagementProviderEnum.Tolgee);
        await Assert.That(translationConfiguration.DefaultLanguage).IsEqualTo("en");
        await Assert.That(translationConfiguration.FallbackLanguage).IsEqualTo("en");
        await Assert.That(translationConfiguration.EnabledLanguages).IsEquivalentTo(["en", "fr"]);
        await Assert.That(analyticsLog.Entries.Any(entry => entry.Level == LogLevel.Debug)).IsTrue();
        await Assert.That(translationLog.Entries.Any(entry => entry.Level == LogLevel.Warning)).IsTrue();
        await AssertPrivateValuesAbsent(analyticsLog.Entries.Concat(translationLog.Entries),
            canary, database.TenantId.ToString());
    }

    private static async Task AssertPrivateValuesAbsent(IEnumerable<TestLogEntry> entries, params string[] privateValues)
    {
        foreach (var entry in entries)
        {
            await Assert.That(entry.Exception).IsNull();
            string exported = entry.Message + JsonSerializer.Serialize(entry.State);
            foreach (string value in privateValues)
                await Assert.That(exported.Contains(value, StringComparison.Ordinal)).IsFalse();
        }
    }

    private sealed class ListmonkFailureTransport(string canary, int failureKind)
        : HttpMessageHandler, IHttpClientFactory
    {
        public int RequestCount { get; private set; }
        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return failureKind switch
            {
                0 => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                { Content = new StringContent(canary) }),
                1 => Task.FromException<HttpResponseMessage>(new HttpRequestException(canary)),
                2 => Task.FromException<HttpResponseMessage>(new TaskCanceledException(canary)),
                _ => throw new ArgumentOutOfRangeException(nameof(failureKind))
            };
        }
    }
}
