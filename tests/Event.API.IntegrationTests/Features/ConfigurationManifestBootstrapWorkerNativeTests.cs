using System.Text.Json;
using System.Text.Json.Nodes;
using Event.Persistence.IntegrationTests;
using Event.Persistence.IntegrationTests.ConfigurationManifest;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.API.BackgroundServices;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Application.Features.ConfigurationManifest.Ingestion;
using Explore.Application.Features.ConfigurationManifest.Requests.Commands;
using Explore.Application.Notifications;
using Explore.Application.Notifications.Handlers;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Settings.Definitions;
using Explore.Infrastructure;
using Explore.Infrastructure.ConfigurationManifest;
using Explore.Infrastructure.Messaging;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Event.Api.IntegrationTests.Features;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel]
public sealed class ConfigurationManifestBootstrapWorkerNativeTests(PostgreSqlContainerFixture fixture)
{
    private static string ThemeKey => AppearanceSettingDefinitions.DefaultThemeMode.Key;
    private static CancellationToken Token => TestContext.Current!.Execution.CancellationToken;

    [Test]
    public async Task OriginalBootstrapEffectCompletesThroughTheEnrolledRuntimeWorker()
    {
        await using var scenario = await CreateScenarioAsync();
        GenericOutboxClaim claim = await scenario.ClaimAsync();
        await Assert.That(claim.Ownership).IsEqualTo(GenericOutboxOwnership.Instance);
        await Assert.That(claim.TenantId).IsNull();
        await Assert.That(claim.SourceId).IsEqualTo(scenario.Receipt.OperationId);
        await Assert.That(claim.AggregateId).IsEqualTo(scenario.Receipt.OperationId);

        // Observe the genuine selector before processing; the worker independently uses this same registration.
        await using (var preparation = scenario.Services.CreateAsyncScope())
        await using (var execution = await preparation.ServiceProvider
            .GetRequiredService<IGenericOutboxExecutionScopeFactory>().CreateAsync(claim, Token))
        {
            var context = execution.Services.GetRequiredService<ExploreDbContext>();
            await Assert.That(ReferenceEquals(context,
                GenericOutboxExecutionScopeFactory.GetSelectedRuntimeContext(execution.Services))).IsTrue();
            await Assert.That(context.TenantFilterTenantId).IsNull();
            await Assert.That(context.IsTenantFilterBypassed).IsFalse();
            await context.Database.OpenConnectionAsync(Token);
            await using var loginProbe = context.Database.GetDbConnection().CreateCommand();
            loginProbe.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
            loginProbe.CommandText = "SELECT current_user::text";
            string login = (string)(await loginProbe.ExecuteScalarAsync(Token))!;
            await Assert.That(login).IsEqualTo(fixture.NativeQueueEnrollment.RuntimeLogin);
            await Assert.That(execution.Services.GetRequiredService<IConfigurationManifestEffectDeliveryStrategy>()
                is DeferredConfigurationManifestEffectDelivery).IsTrue();
            await Assert.That(execution.Services.GetRequiredService<IConfigurationManifestEffectDispatcher>()
                is ConfigurationManifestEffectDispatcher).IsTrue();
            await Assert.That(execution.Services.GetRequiredService<IOutboxMessageDispatcher>()
                is CompositeOutboxMessageDispatcher).IsTrue();
            await Assert.That(execution.Services.GetRequiredService<IHierarchicalSettingsReader>()
                is HierarchicalSettingsReader).IsTrue();

            // These are the dispatcher's real read prerequisites, under the same admitted Runtime owner.
            OutboxMessage? effect = await execution.Services
                .GetRequiredService<IConfigurationManifestEffectOutboxRepository>().GetByIdAsync(claim.MessageId, Token);
            await Assert.That(effect).IsNotNull();
            await Assert.That(effect!.Id).IsEqualTo(claim.MessageId);
            await Assert.That(effect.AggregateId).IsEqualTo(scenario.Receipt.OperationId);
            await Assert.That(effect.EventType).IsEqualTo(ConfigurationManifestEffectOutbox.EventType);
            var instructions = ConfigurationManifestEffectPayload.Read(effect.Payload, effect.CreatedAt);
            await Assert.That(instructions.NotificationTimeUtc).IsEqualTo(scenario.Receipt.StartedAt);
            await Assert.That(instructions.InstanceSettingKeys).IsEquivalentTo([ThemeKey]);
            await Assert.That(instructions.Tenants.Length).IsEqualTo(1);
            await Assert.That(instructions.Tenants[0].TenantId).IsEqualTo(scenario.Receipt.TenantId);
            await Assert.That(instructions.Tenants[0].SettingKeys)
                .IsEquivalentTo([PublicExperienceSettingDefinitions.EventCatalogLabel.Key]);
            await Assert.That(effect.Status).IsEqualTo(claim.Status);
            await Assert.That(effect.NextRetryAt).IsEqualTo(claim.LeaseExpiresAt);
            await Assert.That(effect.CreatedAt).IsEqualTo(scenario.Receipt.CompletedAt);
            var denied = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => context.ConfigurationManifestOperations
                .AsNoTracking().SingleAsync(operation => operation.Id == scenario.Receipt.OperationId, Token));
            await Assert.That(denied!.SqlState).IsEqualTo("42501");
        }

        await scenario.ProcessAsync(claim);
        await scenario.AssertCompletedAsync(expectedRetryCount: 0);
    }

    [Test]
    public async Task ReplacedClaimCannotInvalidateCachesOrCompleteItsSuccessor()
    {
        await using var scenario = await CreateScenarioAsync();
        GenericOutboxClaim original = await scenario.ClaimAsync();
        await using (var adversary = fixture.CreateDbContext())
        {
            // Simulate expiry in the test-owned row; only native admission creates the replacement claim.
            int changed = await adversary.OutboxMessages.Where(row => row.Id == original.MessageId
                    && row.Status == original.Status && row.NextRetryAt == original.LeaseExpiresAt)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.NextRetryAt,
                    DateTime.UtcNow.AddMinutes(-1)), Token);
            await Assert.That(changed).IsEqualTo(1);
        }
        GenericOutboxClaim successor = await scenario.ClaimAsync();
        await Assert.That(successor.LeaseExpiresAt).IsNotEqualTo(original.LeaseExpiresAt);
        var denied = await Assert.ThrowsAsync<InvalidOperationException>(() => scenario.ProcessAsync(original));
        await Assert.That(denied!.Message).IsEqualTo("generic_outbox_execution_not_admitted");
        OutboxMessage retained = await scenario.ReadEffectAsync();
        await Assert.That(retained.Status).IsEqualTo(OutboxMessageStatus.Processing);
        await Assert.That(retained.NextRetryAt).IsEqualTo(successor.LeaseExpiresAt);
        await Assert.That(retained.RetryCount).IsEqualTo(0);
        await Assert.That(retained.ProcessedAt).IsNull();
        await Assert.That(await scenario.ReadCachedThemeAsync()).IsEqualTo("light");
        await scenario.AssertReceiptUnchangedAsync();

        await scenario.ProcessAsync(successor);
        await scenario.AssertCompletedAsync(expectedRetryCount: 0);
    }

    [Test]
    public async Task TransientCacheFailureRetainsTheAppliedReceiptAndRetriesTheSameEffect()
    {
        await using var scenario = await CreateScenarioAsync();
        GenericOutboxClaim first = await scenario.ClaimAsync();
        scenario.Cache.RejectRemovals = true;
        await scenario.ProcessAsync(first);
        await Assert.That(scenario.Cache.FailedRemovalObserved).IsTrue();
        OutboxMessage retry = await scenario.ReadEffectAsync();
        await Assert.That(retry.Status).IsEqualTo(OutboxMessageStatus.Pending);
        await Assert.That(retry.RetryCount).IsEqualTo(1);
        await Assert.That(retry.LastError).IsEqualTo("dispatch_failed");
        await Assert.That(retry.ProcessedAt).IsNull();
        await Assert.That(retry.NextRetryAt).IsNotNull();
        await Assert.That(await scenario.ReadCachedThemeAsync()).IsEqualTo("light");
        await scenario.AssertReceiptUnchangedAsync();

        scenario.Cache.RejectRemovals = false;
        GenericOutboxClaim second = await scenario.ClaimAsync();
        await Assert.That(second.MessageId).IsEqualTo(first.MessageId);
        await Assert.That(second.SourceId).IsEqualTo(first.SourceId);
        await Assert.That(second.CreatedAt).IsEqualTo(first.CreatedAt);
        await Assert.That(second.LeaseExpiresAt).IsNotEqualTo(first.LeaseExpiresAt);
        await scenario.ProcessAsync(second);
        await scenario.AssertCompletedAsync(expectedRetryCount: 1);
    }

    [Test]
    public async Task RuntimeCannotReplaceImmutableManifestInstructionsThroughNativeOrDirectWrites()
    {
        await using var scenario = await CreateScenarioAsync();
        var claim = await scenario.ClaimAsync();
        OutboxMessage original = await scenario.ReadEffectAsync();
        var replacement = JsonNode.Parse(original.Payload!)!.AsObject();
        replacement["notificationTimeUtc"] = scenario.Receipt.StartedAt.AddTicks(1);
        string proposed = replacement.ToJsonString();
        await using (var preparation = scenario.Services.CreateAsyncScope())
        await using (var execution = await preparation.ServiceProvider.GetRequiredService<IGenericOutboxExecutionScopeFactory>()
            .CreateAsync(claim, Token))
        {
            var context = execution.Services.GetRequiredService<ExploreDbContext>();
            await context.Database.OpenConnectionAsync(Token);
            var repository = execution.Services.GetRequiredService<IOutboxRepository>();
            await Assert.That(await repository.TryReplaceProcessingPayloadAsync(claim.MessageId, claim.LeaseExpiresAt,
                original.Payload!, proposed, Token)).IsFalse();
            await Assert.That(await PostgresOutboxExecutionContract.SettleAsync(context, fixture.NativeQueueEnrollment,
                claim.MessageId, claim.LeaseExpiresAt, claim.Status, 4, null, false, 0, null,
                original.Payload, proposed, Token)).IsEqualTo(0);
            await AssertDirectWritesDeniedAsync(context, original.Id, proposed);
        }
        await Assert.That((await scenario.ReadEffectAsync()).Payload).IsEqualTo(original.Payload);
        await scenario.ProcessAsync(claim);
        await scenario.AssertCompletedAsync(expectedRetryCount: 0);
    }

    [Test]
    public async Task UnknownPayloadVersionRetriesWithoutApplyingEffectsOrChangingTheReceipt()
    {
        await using var scenario = await CreateScenarioAsync();
        var original = await scenario.ReadEffectAsync();
        var unsupported = JsonNode.Parse(original.Payload!)!.AsObject();
        unsupported["version"] = 2;
        string changedPayload = unsupported.ToJsonString();
        await using (var adversary = fixture.CreateDbContext())
            await Assert.That(await adversary.OutboxMessages.Where(row => row.Id == original.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Payload, changedPayload), Token)).IsEqualTo(1);
        var claim = await scenario.ClaimAsync();
        await scenario.ProcessAsync(claim);
        var retained = await scenario.ReadEffectAsync();
        await Assert.That(retained.Status).IsEqualTo(OutboxMessageStatus.Pending);
        await Assert.That(retained.RetryCount).IsEqualTo(1);
        await Assert.That(retained.LastError).IsEqualTo("dispatch_failed");
        await Assert.That(retained.Payload).IsEqualTo(changedPayload);
        await Assert.That(retained.ProcessedAt).IsNull();
        await Assert.That(await scenario.ReadCachedThemeAsync()).IsEqualTo("light");
        await scenario.AssertReceiptUnchangedAsync();
    }

    [Test]
    [Arguments("payload")]
    [Arguments("tenant")]
    [Arguments("time")]
    [Arguments("route")]
    public async Task SameClaimCannotAuthorizeSubstitutedInstructions(string mutation)
    {
        await using var scenario = await CreateScenarioAsync();
        var claim = await scenario.ClaimAsync();
        var original = await scenario.ReadEffectAsync();
        var submitted = JsonSerializer.Deserialize<OutboxMessage>(JsonSerializer.Serialize(original))!;
        var payload = JsonNode.Parse(original.Payload!)!.AsObject();
        switch (mutation)
        {
            case "payload": submitted.Payload = "{}"; break;
            case "tenant": payload["tenants"]![0]!["tenantId"] = Guid.CreateVersion7(); submitted.Payload = payload.ToJsonString(); break;
            case "time": payload["notificationTimeUtc"] = scenario.Receipt.StartedAt.AddTicks(1); submitted.Payload = payload.ToJsonString(); break;
            case "route": submitted.EventType = "OperatorIdentityImported"; break;
        }
        await using (var preparation = scenario.Services.CreateAsyncScope())
        await using (var execution = await preparation.ServiceProvider.GetRequiredService<IGenericOutboxExecutionScopeFactory>()
            .CreateAsync(claim, Token))
        {
            var denied = await Assert.ThrowsAsync<InvalidOperationException>(() => execution.Services
                .GetRequiredService<IOutboxMessageDispatcher>().DispatchAsync(submitted, Token));
            await Assert.That(denied!.Message).IsEqualTo("generic_outbox_execution_not_admitted");
        }
        var retained = await scenario.ReadEffectAsync();
        await Assert.That(retained.Payload).IsEqualTo(original.Payload);
        await Assert.That(retained.Status).IsEqualTo(OutboxMessageStatus.Processing);
        await Assert.That(retained.NextRetryAt).IsEqualTo(claim.LeaseExpiresAt);
        await Assert.That(await scenario.ReadCachedThemeAsync()).IsEqualTo("light");
        await scenario.AssertReceiptUnchangedAsync();
        await scenario.ProcessAsync(claim);
        await scenario.AssertCompletedAsync(expectedRetryCount: 0);
    }

    private static async Task AssertDirectWritesDeniedAsync(ExploreDbContext context, Guid messageId, string payload)
    {
        var entity = context.Model.FindEntityType(typeof(OutboxMessage))!;
        var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        var sql = context.GetService<ISqlGenerationHelper>();
        string table = sql.DelimitIdentifier(store.Name, store.Schema);
        string id = sql.DelimitIdentifier(entity.FindProperty(nameof(OutboxMessage.Id))!.GetColumnName(store)!);
        string value = sql.DelimitIdentifier(entity.FindProperty(nameof(OutboxMessage.Payload))!.GetColumnName(store)!);
        foreach (string statement in new[]
        {
            $"UPDATE {table} SET {value} = @payload WHERE {id} = @id",
            $"DELETE FROM {table} WHERE {id} = @id",
            $"INSERT INTO {table} ({id},{value}) VALUES (@id,@payload)"
        })
        {
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = statement;
            command.Parameters.Add(new Npgsql.NpgsqlParameter("id", messageId));
            command.Parameters.Add(new Npgsql.NpgsqlParameter("payload", payload));
            var denied = await Assert.ThrowsAsync<Npgsql.PostgresException>(async () => await command.ExecuteNonQueryAsync(Token));
            await Assert.That(denied!.SqlState).IsEqualTo("42501");
        }
    }

    private async Task<Scenario> CreateScenarioAsync()
    {
        await fixture.ResetAsync();
        await using (var seed = fixture.CreateDbContext())
        {
            var definition = AppearanceSettingDefinitions.DefaultThemeMode;
            SystemSetting? theme = await seed.SystemSettings.SingleOrDefaultAsync(row => row.SettingKey == ThemeKey, Token);
            if (theme is null)
            {
                theme = new SystemSetting
                {
                    Id = Guid.CreateVersion7(),
                    SettingKey = definition.Key,
                    Value = "\"light\"",
                    ValueType = definition.ValueType,
                    Description = definition.Description,
                    Category = definition.Category,
                    CreatedAt = DateTime.UtcNow
                };
                seed.SystemSettings.Add(theme);
            }
            theme.Value = "\"light\"";
            theme.IsLocked = false;
            await seed.SaveChangesAsync(Token);
        }
        var cache = new FaultingMemoryCache();
        ServiceProvider services = CreateWorkerServices(cache);
        try
        {
            await using (var observer = fixture.CreateDbContext())
                await Assert.That(await Reader(observer, cache).ResolveAsync<string>(ThemeKey, new SettingContext(), Token))
                    .IsEqualTo("light");
            await using var bootstrap = ConfigurationManifestBootstrapTestHost.ForPostgres(fixture);
            var manifest = new ConfigurationManifestV1Alpha2
            {
                Schema = ConfigurationManifestContractMetadata.SchemaId,
                ApiVersion = ConfigurationManifestContractMetadata.ApiVersion,
                Kind = ConfigurationManifestContractMetadata.Kind,
                Metadata = new() { Name = "native-worker-handoff" },
                Spec = new()
                {
                    Instance = new()
                    {
                        Settings = new Dictionary<string, JsonElement>
                        {
                            [ThemeKey] = JsonSerializer.SerializeToElement("dark")
                        },
                        Documents = new Dictionary<string, ConfigurationManifestDocumentV1Alpha2>()
                    },
                    Tenants =
                    [
                        new ConfigurationManifestTenantV1Alpha2
                        {
                            Metadata = new() { Name = "native-worker-community" },
                            Spec = new()
                            {
                                DisplayName = "Native worker community",
                                Settings = new Dictionary<string, JsonElement>
                                {
                                    [PublicExperienceSettingDefinitions.EventCatalogLabel.Key] =
                                        JsonSerializer.SerializeToElement("Community events")
                                },
                                Documents = new Dictionary<string, ConfigurationManifestDocumentV1Alpha2>()
                            }
                        }
                    ]
                }
            };
            using var input = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(manifest,
                ConfigurationPortabilityJsonContext.Default.ConfigurationManifestV1Alpha2));
            ConfigurationManifestReadResult source = await new ConfigurationManifestReader()
                .ReadStreamAsync(input, ConfigurationManifestMode.Bootstrap, Token);
            var applied = await bootstrap.ExecuteAsync(new ApplyConfigurationManifestCommand(source), Token);
            await Assert.That(applied.IsSuccess).IsTrue();
            ReceiptSnapshot receipt = await ReadReceiptAsync(fixture, applied.Id);
            await Assert.That(receipt.Status).IsEqualTo(ConfigurationManifestOperationStatus.Applied);
            await Assert.That(receipt.Digest).IsEqualTo(bootstrap.ArtifactDigest);
            await Assert.That(receipt.EffectId).IsNotNull();
            var scenario = new Scenario(fixture, services, cache, receipt);
            OutboxMessage pending = await scenario.ReadEffectAsync();
            await Assert.That(pending.Status).IsEqualTo(OutboxMessageStatus.Pending);
            await Assert.That(pending.RetryCount).IsEqualTo(0);
            await Assert.That(pending.NextRetryAt).IsNull();
            await Assert.That(pending.ProcessedAt).IsNull();
            var instructions = ConfigurationManifestEffectPayload.Read(pending.Payload, pending.CreatedAt);
            await Assert.That(instructions.NotificationTimeUtc).IsEqualTo(receipt.StartedAt);
            await Assert.That(instructions.Tenants.Select(tenant => tenant.TenantId)).IsEquivalentTo([receipt.TenantId]);
            await Assert.That(pending.AggregateType).IsEqualTo(ConfigurationManifestEffectOutbox.AggregateType);
            await Assert.That(pending.EventType).IsEqualTo(ConfigurationManifestEffectOutbox.EventType);
            await Assert.That(pending.AggregateId).IsEqualTo(receipt.OperationId);
            await Assert.That(pending.CreatedAt).IsEqualTo(receipt.CompletedAt);
            await Assert.That(await scenario.ReadCachedThemeAsync()).IsEqualTo("light");
            return scenario;
        }
        catch
        {
            await services.DisposeAsync();
            cache.Dispose();
            throw;
        }
    }

    private ServiceProvider CreateWorkerServices(FaultingMemoryCache cache)
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IMemoryCache>(cache);
        services.AddSingleton(fixture.NativeQueueEnrollment);
        services.AddConfigurationManifestStartup(configuration, ConfigurationManifestEffectDeliveryMode.DeferredToRuntime);
        services.AddConfigurationManifestPersistence();
        services.AddPolicyMutationExecutionScopes();
        services.AddSingleton<IGenericOutboxAdmissionDbContextFactory>(new GlobalExploreDbContextFactory(
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global)));
        var runtimeOptions = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(runtimeOptions,
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime));
        runtimeOptions.AddInterceptors(PostgresTenantSessionInterceptor.RuntimeInstance);
        services.AddSingleton<IDbContextFactory<ExploreDbContext>>(new PooledDbContextFactory<ExploreDbContext>(runtimeOptions.Options));
        services.AddSingleton<ITenantExploreDbContextFactory>(new TenantExploreDbContextFactory(
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime)));
        services.AddScoped<ITenantContextAccessor, TenantContextAccessor>();
        services.AddScoped(provider => GenericOutboxExecutionScopeFactory.GetSelectedRuntimeContext(provider)
            ?? throw new InvalidOperationException("The native worker must select its original Runtime context first."));
        services.AddScoped<IGenericOutboxAdmission, GenericOutboxAdmission>();
        services.AddScoped<IGenericOutboxExecutionAuthority, GenericOutboxExecutionAuthority>();
        services.AddScoped<IGenericOutboxExecutionScopeFactory, GenericOutboxExecutionScopeFactory>();
        services.AddScoped<IOutboxRepository, OutboxRepository>();
        services.AddScoped<IHierarchicalSettingsReader>(provider => Reader(
            provider.GetRequiredService<ExploreDbContext>(), provider.GetRequiredService<IMemoryCache>()));
        services.AddScoped<ITypedSettingsDocumentResolver>(provider => new TypedSettingsDocumentResolver(
            new TenantSettingsDocumentRepository(provider.GetRequiredService<ExploreDbContext>()),
            provider.GetRequiredService<IMemoryCache>()));
        services.AddScoped<INotificationHandler<SettingChangedNotification>, SettingAuditLogHandler>();
        services.AddScoped<IOutboxMessageDispatcher>(provider => new CompositeOutboxMessageDispatcher(
            // Unrelated routes are intentionally uncomposed: this is a native manifest lane, not a full API-host test.
            notificationFanoutOccurrenceHandoffService: null!, notificationFanoutService: null!,
            moderationNotificationFanoutService: null!, reportProviderSyncDispatcher: null!,
            eventDiscoveryIdentityCorrectionDispatcher: null!, locationPrivacyCorrectionDispatcher: null!,
            privacyErasureCacheInvalidationDispatcher: null!, admissionCredentialDeliveryHandler: null!,
            admissionRecoveryRequestHandler: null!, admissionRecoveryDeliveryHandler: null!,
            outboxRepository: provider.GetRequiredService<IOutboxRepository>(), refundCampaignRepository: null!,
            refundCampaignProcessor: null!, refundDispatchService: null!, refundReconciliationService: null!,
            paymentCancellationService: null!, admissionRevocationService: null!, admissionRefundRevocationService: null!,
            admissionEventCancellationService: null!, businessMetrics: null!, timeProvider: TimeProvider.System,
            processManagedTenantProvisioningOperationCommandHandler: null!, reconcileManagedTenantProvisioningDeadLetterCommandHandler: null!,
            logger: NullLogger<CompositeOutboxMessageDispatcher>.Instance,
            configurationManifestEffectDispatcher: provider.GetRequiredService<IConfigurationManifestEffectDispatcher>(),
            configurationImportEffectDelivery: null!, moderationEffectDispatcher: null!, notificationOutboxExecutionAuthority: null!,
            genericOutboxExecutionAuthority: provider.GetRequiredService<IGenericOutboxExecutionAuthority>()));
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static HierarchicalSettingsReader Reader(ExploreDbContext context, IMemoryCache cache)
    {
        var locks = new RelationalSettingMutationLock(context, new EfCoreUnitOfWork(context));
        return new HierarchicalSettingsReader(SystemSettingRepository.CreateReadOnly(context, locks),
            TenantSettingRepository.CreateReadOnly(context, locks), new OrganizationSettingRepository(context),
            new GroupSettingRepository(context), UserPreferenceRepository.CreateReadOnly(context), cache,
            new AtprotoFederationSettingRepository(context, null));
    }

    private static async Task<ReceiptSnapshot> ReadReceiptAsync(PostgreSqlContainerFixture database, Guid operationId)
    {
        await using var observer = database.CreateDbContext();
        var operation = await observer.ConfigurationManifestOperations.AsNoTracking()
            .SingleAsync(row => row.Id == operationId, Token);
        var result = await observer.ConfigurationManifestTenantResults.AsNoTracking()
            .SingleAsync(row => row.OperationId == operationId, Token);
        await Assert.That(result.Status).IsEqualTo(ConfigurationManifestTenantResultStatus.Created);
        return new(operation.Id, operation.Digest, operation.Status, operation.CompletedAt, operation.StartedAt, operation.EffectOutboxId,
            result.Id, result.TenantId, string.Join(",", result.ChangedKeyNames));
    }

    private sealed record ReceiptSnapshot(Guid OperationId, string Digest, ConfigurationManifestOperationStatus Status,
        DateTime CompletedAt, DateTime StartedAt, Guid? EffectId, Guid ResultId, Guid TenantId, string ChangedKeys);

    private sealed class Scenario(PostgreSqlContainerFixture database, ServiceProvider services,
        FaultingMemoryCache cache, ReceiptSnapshot receipt) : IAsyncDisposable
    {
        internal IServiceProvider Services => services;
        internal FaultingMemoryCache Cache => cache;
        internal ReceiptSnapshot Receipt => receipt;

        internal async Task<GenericOutboxClaim> ClaimAsync()
        {
            await using var scope = services.CreateAsyncScope();
            var claims = await scope.ServiceProvider.GetRequiredService<IGenericOutboxAdmission>()
                .ClaimDueAsync(100, DateTime.UtcNow, Token);
            return claims.Single(claim => claim.MessageId == receipt.EffectId);
        }

        internal Task ProcessAsync(GenericOutboxClaim claim) => new OutboxProcessor(services,
            Options.Create(new OutboxProcessorSettings { InitialRetryDelaySeconds = 0, MaxRetryDelaySeconds = 0 }),
            NullLogger<OutboxProcessor>.Instance).ProcessGenericClaimAsync(claim, Token);

        internal async Task<OutboxMessage> ReadEffectAsync()
        {
            await using var observer = database.CreateDbContext();
            return await observer.OutboxMessages.AsNoTracking().SingleAsync(row => row.Id == receipt.EffectId, Token);
        }

        internal async Task<string?> ReadCachedThemeAsync()
        {
            await using var observer = database.CreateDbContext();
            return await Reader(observer, cache).ResolveAsync<string>(ThemeKey, new SettingContext(), Token);
        }

        internal async Task AssertReceiptUnchangedAsync()
        {
            await Assert.That(await ReadReceiptAsync(database, receipt.OperationId)).IsEqualTo(receipt);
            await using var observer = database.CreateDbContext();
            string? persisted = await observer.SystemSettings.AsNoTracking().Where(row => row.SettingKey == ThemeKey)
                .Select(row => row.Value).SingleAsync(Token);
            await Assert.That(JsonSerializer.Deserialize<string>(persisted!)).IsEqualTo("dark");
        }

        internal async Task AssertCompletedAsync(int expectedRetryCount)
        {
            OutboxMessage completed = await ReadEffectAsync();
            await Assert.That(completed.Status).IsEqualTo(OutboxMessageStatus.Completed);
            await Assert.That(completed.RetryCount).IsEqualTo(expectedRetryCount);
            await Assert.That(completed.ProcessedAt).IsNotNull();
            await Assert.That(completed.NextRetryAt).IsNull();
            await Assert.That(await ReadCachedThemeAsync()).IsEqualTo("dark");
            await AssertReceiptUnchangedAsync();
            await using var observer = database.CreateDbContext();
            await Assert.That(await observer.OutboxMessages.CountAsync(row => row.AggregateId == receipt.OperationId, Token))
                .IsEqualTo(1);
        }

        public async ValueTask DisposeAsync()
        {
            await services.DisposeAsync();
            cache.Dispose();
        }
    }

    /// <summary>Faults only cache transport; the real reader, dispatcher, admission and settlement remain unchanged.</summary>
    private sealed class FaultingMemoryCache : IMemoryCache
    {
        private readonly MemoryCache inner = new(new MemoryCacheOptions());
        internal bool RejectRemovals { get; set; }
        internal bool FailedRemovalObserved { get; private set; }
        public bool TryGetValue(object key, out object? value) => inner.TryGetValue(key, out value);
        public ICacheEntry CreateEntry(object key) => inner.CreateEntry(key);
        public void Remove(object key)
        {
            if (RejectRemovals)
            {
                FailedRemovalObserved = true;
                throw new InvalidOperationException("The test cache transport is temporarily unavailable.");
            }
            inner.Remove(key);
        }
        public void Dispose() => inner.Dispose();
    }
}
