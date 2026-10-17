using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Application.Features.ConfigurationManifest.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Settings.Documents;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Seed;
using Explore.Application.Contracts.Operations;
using Explore.Application.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Event.Persistence.IntegrationTests.ConfigurationManifest;

[NotInParallel("ConfigurationManifestAtomicPersistence")]
public sealed class ConfigurationManifestAtomicPersistenceTests
{
    [Test]
    public async Task Bootstrap_RerunCreatesOnceThenAuditsWholesaleSkip()
    {
        string databasePath = TemporaryDatabasePath();
        try
        {
            DbContextOptions<ExploreDbContext> options = CreateOptions(databasePath);
            await InitializeAsync(options);
            var factory = new ConfigurationManifestApplicationTestSupport.TestDbContextFactory(options);
            var source = ConfigurationManifestApplicationTestSupport.Source("primary");

            await using (ExploreDbContext firstContext = factory.CreateDbContext())
            {
                await using var handler = ConfigurationManifestApplicationTestSupport.CreateHandler(factory);
                var first = await handler.ExecuteAsync(
                    new ApplyConfigurationManifestCommand(source),
                    CancellationToken.None);
                await Assert.That(first.IsSuccess).IsTrue();
            }

            await using (ExploreDbContext secondContext = factory.CreateDbContext())
            {
                await using var handler = ConfigurationManifestApplicationTestSupport.CreateHandler(factory);
                var second = await handler.ExecuteAsync(
                    new ApplyConfigurationManifestCommand(source),
                    CancellationToken.None);
                await Assert.That(second.IsSuccess).IsTrue();
            }

            await using ExploreDbContext verification = factory.CreateDbContext();
            ConfigurationManifestOperation[] operations =
                await verification.ConfigurationManifestOperations
                    .AsNoTracking()
                    .OrderBy(operation => operation.CompletedAt)
                    .ToArrayAsync();
            ConfigurationManifestTenantResult[] results =
                await verification.ConfigurationManifestTenantResults
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .OrderBy(result => result.CompletedAt)
                    .ToArrayAsync();

            await Assert.That(await verification.Tenants.CountAsync()).IsEqualTo(1);
            await Assert.That(await verification.TenantSettingOverrides
                .IgnoreQueryFilters()
                .CountAsync()).IsEqualTo(1);
            TenantSettingsDocument[] documents = await verification.TenantSettingsDocuments
                .IgnoreQueryFilters()
                .AsNoTracking()
                .ToArrayAsync();
            await Assert.That(documents.Select(document => document.DocumentKey)).IsEquivalentTo(
            [
                SettingsDocumentKeys.Tenant.Branding,
                SettingsDocumentKeys.Tenant.DirectoryOperatorIdentity
            ]);
            OutboxMessage effectOutbox = await verification.OutboxMessages
                .AsNoTracking()
                .SingleAsync(message =>
                    message.EventType == ConfigurationManifestEffectOutbox.EventType);
            await Assert.That(effectOutbox.Status).IsEqualTo(OutboxMessageStatus.Completed);
            await Assert.That(operations.Length).IsEqualTo(2);
            await Assert.That(operations.Count(operation =>
                operation.CreatedTenantCount == 1)).IsEqualTo(1);
            await Assert.That(operations.Count(operation =>
                operation.SkippedExistingTenantCount == 1)).IsEqualTo(1);
            await Assert.That(results.Select(result => result.Status)).IsEquivalentTo(
            [
                ConfigurationManifestTenantResultStatus.Created,
                ConfigurationManifestTenantResultStatus.SkippedExisting
            ]);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Test]
    public async Task Bootstrap_PartialOverlapNeverFillsExistingTenantConfiguration()
    {
        string databasePath = TemporaryDatabasePath();
        try
        {
            DbContextOptions<ExploreDbContext> options = CreateOptions(databasePath);
            await InitializeAsync(options);
            var factory = new ConfigurationManifestApplicationTestSupport.TestDbContextFactory(options);
            Guid existingTenantId = Guid.CreateVersion7();
            await using (ExploreDbContext seed = factory.CreateDbContext())
            {
                seed.Tenants.Add(new Tenant
                {
                    Id = existingTenantId,
                    FullName = "Existing",
                    Slug = "existing",
                    TenantStatusId = (int)TenantStatusEnum.Provisioning,
                    TenantStatus = null!,
                    CreatedAt = DateTime.UtcNow
                });
                seed.TenantSettingOverrides.Add(new TenantSetting
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = existingTenantId,
                    Tenant = null!,
                    SettingKey = Explore.Domain.Settings.Definitions
                        .PublicExperienceSettingDefinitions.EventCatalogLabel.Key,
                    Value = "\"Original Events\"",
                    IsLocked = false,
                    CreatedAt = DateTime.UtcNow
                });
                await seed.SaveChangesAsync();
            }

            await using (ExploreDbContext apply = factory.CreateDbContext())
            {
                await using var handler = ConfigurationManifestApplicationTestSupport.CreateHandler(factory);
                var response = await handler.ExecuteAsync(
                    new ApplyConfigurationManifestCommand(
                        ConfigurationManifestApplicationTestSupport.Source("existing", "new")),
                    CancellationToken.None);
                await Assert.That(response.IsSuccess).IsTrue();
            }

            await using ExploreDbContext verification = factory.CreateDbContext();
            ConfigurationManifestOperation operation =
                await verification.ConfigurationManifestOperations.SingleAsync();
            ConfigurationManifestTenantResult[] results =
                await verification.ConfigurationManifestTenantResults
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .ToArrayAsync();

            await Assert.That(operation.CreatedTenantCount).IsEqualTo(1);
            await Assert.That(operation.SkippedExistingTenantCount).IsEqualTo(1);
            TenantSetting existingSetting = await verification.TenantSettingOverrides
                .IgnoreQueryFilters()
                .SingleAsync(setting => setting.TenantId == existingTenantId);
            await Assert.That(existingSetting.Value).IsEqualTo("\"Original Events\"");
            await Assert.That(await verification.TenantSettingsDocuments
                .IgnoreQueryFilters()
                .CountAsync(document => document.TenantId == existingTenantId)).IsEqualTo(0);
            Guid newTenantId = await verification.Tenants
                .Where(tenant => tenant.Slug == "new")
                .Select(tenant => tenant.Id)
                .SingleAsync();
            await Assert.That(await verification.TenantSettingOverrides
                .IgnoreQueryFilters()
                .CountAsync(setting => setting.TenantId == newTenantId)).IsEqualTo(1);
            TenantSettingsDocument[] newTenantDocuments = await verification.TenantSettingsDocuments
                .IgnoreQueryFilters()
                .Where(document => document.TenantId == newTenantId)
                .AsNoTracking()
                .ToArrayAsync();
            await Assert.That(newTenantDocuments.Select(document => document.DocumentKey)).IsEquivalentTo(
            [
                SettingsDocumentKeys.Tenant.Branding,
                SettingsDocumentKeys.Tenant.DirectoryOperatorIdentity
            ]);
            await Assert.That(results.Single(result =>
                    result.TenantId == existingTenantId).ChangedKeyNames)
                .IsEmpty();
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Test]
    public async Task Bootstrap_DifferentDigestRerunSkipsWithoutOverwritingExistingConfiguration()
    {
        string databasePath = TemporaryDatabasePath();
        try
        {
            DbContextOptions<ExploreDbContext> options = CreateOptions(databasePath);
            await InitializeAsync(options);
            var factory = new ConfigurationManifestApplicationTestSupport.TestDbContextFactory(options);
            string firstDigest = await ApplyAsync(
                factory,
                ConfigurationManifestApplicationTestSupport.DifferentDigestSource(
                    new string('d', ConfigurationManifestOperation.DigestLength),
                    "Original Catalog",
                    "digest"));
            string secondDigest = await ApplyAsync(
                factory,
                ConfigurationManifestApplicationTestSupport.DifferentDigestSource(
                    new string('e', ConfigurationManifestOperation.DigestLength),
                    "Changed Catalog",
                    "digest"));

            await using ExploreDbContext verification = factory.CreateDbContext();
            TenantSetting persisted = await verification.TenantSettingOverrides
                .IgnoreQueryFilters()
                .AsNoTracking()
                .SingleAsync();
            ConfigurationManifestOperation secondOperation =
                await verification.ConfigurationManifestOperations
                    .AsNoTracking()
                    .SingleAsync(operation => operation.Digest == secondDigest);

            await Assert.That(persisted.Value).IsEqualTo("\"Original Catalog\"");
            await Assert.That(firstDigest).IsNotEqualTo(secondDigest);
            await Assert.That(secondOperation.CreatedTenantCount).IsEqualTo(0);
            await Assert.That(secondOperation.SkippedExistingTenantCount).IsEqualTo(1);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Test]
    public async Task Bootstrap_GuardedAndOrdinarySettingsCommitThroughNormalizedBoundaries()
    {
        string databasePath = TemporaryDatabasePath();
        try
        {
            DbContextOptions<ExploreDbContext> options = CreateOptions(databasePath);
            await InitializeAsync(options);
            var factory = new ConfigurationManifestApplicationTestSupport.TestDbContextFactory(options);
            await using (ExploreDbContext apply = factory.CreateDbContext())
            {
                await using var handler = ConfigurationManifestApplicationTestSupport.CreateHandler(factory);

                var response = await handler.ExecuteAsync(
                    new ApplyConfigurationManifestCommand(
                        ConfigurationManifestApplicationTestSupport.GuardedSource("guarded")),
                    CancellationToken.None);

                await Assert.That(response.IsSuccess).IsTrue();
            }

            await using ExploreDbContext verification = factory.CreateDbContext();
            TenantSetting[] settings = await verification.TenantSettingOverrides
                .IgnoreQueryFilters()
                .AsNoTracking()
                .OrderBy(setting => setting.SettingKey)
                .ToArrayAsync();

            await Assert.That(settings.Length).IsEqualTo(2);
            await Assert.That(settings.Select(setting => setting.SettingKey)).IsEquivalentTo(
            [
                Explore.Domain.Settings.Definitions.EventSettingDefinitions.RequireApproval.Key,
                Explore.Domain.Settings.Definitions.PublicExperienceSettingDefinitions.EventCatalogLabel.Key
            ]);
            await Assert.That(settings.All(setting => setting.CreatedBy == null)).IsTrue();
            await Assert.That(await verification.ConfigurationManifestOperations
                .CountAsync(operation =>
                    operation.Status == ConfigurationManifestOperationStatus.Applied))
                .IsEqualTo(1);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Test]
    public async Task Bootstrap_LaterInvocationDrainsPersistedFailedEffectsBeforeRerun()
    {
        string databasePath = TemporaryDatabasePath();
        try
        {
            DbContextOptions<ExploreDbContext> options = CreateOptions(databasePath);
            await InitializeAsync(options);
            var factory = new ConfigurationManifestApplicationTestSupport.TestDbContextFactory(options);
            var source = ConfigurationManifestApplicationTestSupport.Source("effect-retry");
            await using (ExploreDbContext firstContext = factory.CreateDbContext())
            {
                await using var handler = ConfigurationManifestApplicationTestSupport.CreateHandler(factory,
                    effectConsumer: new ThrowingConsumer());
                await Assert.ThrowsAsync<AggregateException>(async () => await handler.ExecuteAsync(
                        new ApplyConfigurationManifestCommand(source),
                        CancellationToken.None));
            }

            await using (ExploreDbContext secondContext = factory.CreateDbContext())
            {
                await using var handler = ConfigurationManifestApplicationTestSupport.CreateHandler(factory);
                var response = await handler.ExecuteAsync(
                    new ApplyConfigurationManifestCommand(source),
                    CancellationToken.None);
                await Assert.That(response.IsSuccess).IsTrue();
            }

            await using ExploreDbContext verification = factory.CreateDbContext();
            OutboxMessage outbox = await verification.OutboxMessages
                .AsNoTracking()
                .SingleAsync(message =>
                    message.EventType == ConfigurationManifestEffectOutbox.EventType);
            ConfigurationManifestOperation[] operations =
                await verification.ConfigurationManifestOperations
                    .AsNoTracking()
                    .ToArrayAsync();
            await Assert.That(outbox.Status).IsEqualTo(OutboxMessageStatus.Completed);
            await Assert.That(outbox.RetryCount).IsEqualTo(1);
            await Assert.That(await verification.Tenants.CountAsync()).IsEqualTo(1);
            await Assert.That(operations.Length).IsEqualTo(2);
            await Assert.That(operations.Count(operation =>
                operation.CreatedTenantCount == 1)).IsEqualTo(1);
            await Assert.That(operations.Count(operation =>
                operation.SkippedExistingTenantCount == 1)).IsEqualTo(1);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Test]
    public async Task Bootstrap_AuditWriteFailureRollsBackConfigurationThenRecordsFailure()
    {
        string databasePath = TemporaryDatabasePath();
        try
        {
            DbContextOptions<ExploreDbContext> options = CreateOptions(databasePath);
            await InitializeAsync(options);
            var factory = new ConfigurationManifestApplicationTestSupport.TestDbContextFactory(options);
            await using (ExploreDbContext apply = factory.CreateDbContext())
            {
                var entity = apply.Model.FindEntityType(typeof(ConfigurationManifestOperation))!;
                string table = apply.GetService<ISqlGenerationHelper>().DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
                await apply.Database.ExecuteSqlRawAsync($"""
                    CREATE TRIGGER reject_manifest_applied BEFORE INSERT ON {table}
                    WHEN NEW.status = {(int)ConfigurationManifestOperationStatus.Applied}
                    BEGIN SELECT RAISE(ABORT, 'actual manifest receipt persistence rejected'); END;
                    """);
                await using var handler = ConfigurationManifestApplicationTestSupport.CreateHandler(factory);

                var response = await handler.ExecuteAsync(
                    new ApplyConfigurationManifestCommand(
                        ConfigurationManifestApplicationTestSupport.Source("rollback")),
                    CancellationToken.None);

                await Assert.That(response.IsSuccess).IsFalse();
                await Assert.That(response.FailureCode)
                    .IsEqualTo(
                        Explore.Application.Features.ConfigurationManifest.Preflight
                            .ConfigurationManifestApplicationFailureCodes.ApplyFailed);
            }

            await using ExploreDbContext verification = factory.CreateDbContext();
            await Assert.That(await verification.Tenants.CountAsync()).IsEqualTo(0);
            await Assert.That(await verification.TenantSettingOverrides
                .IgnoreQueryFilters()
                .CountAsync()).IsEqualTo(0);
            await Assert.That(await verification.TenantSettingsDocuments
                .IgnoreQueryFilters()
                .CountAsync()).IsEqualTo(0);
            await Assert.That(await verification.OutboxMessages.CountAsync()).IsEqualTo(0);
            ConfigurationManifestOperation[] operations =
                await verification.ConfigurationManifestOperations
                    .AsNoTracking()
                    .ToArrayAsync();
            await Assert.That(operations).HasSingleItem();
            await Assert.That(operations[0].Status)
                .IsEqualTo(ConfigurationManifestOperationStatus.Failed);
            await Assert.That(await verification.ConfigurationManifestTenantResults
                .IgnoreQueryFilters()
                .CountAsync()).IsEqualTo(0);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Test]
    public async Task Bootstrap_SecondTenantFailureRollsBackFirstTenantAndRecordsOnlyFailure()
    {
        string databasePath = TemporaryDatabasePath();
        try
        {
            DbContextOptions<ExploreDbContext> options = CreateOptions(databasePath);
            await InitializeAsync(options);
            var factory = new ConfigurationManifestApplicationTestSupport.TestDbContextFactory(options);
            await using (ExploreDbContext apply = factory.CreateDbContext())
            {
                var entity = apply.Model.FindEntityType(typeof(Tenant))!;
                string table = apply.GetService<ISqlGenerationHelper>().DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
                await apply.Database.ExecuteSqlRawAsync($"""
                    CREATE TRIGGER reject_second_manifest_tenant BEFORE INSERT ON {table}
                    WHEN NEW.slug = 'second'
                    BEGIN SELECT RAISE(ABORT, 'actual second tenant persistence rejected'); END;
                    """);
                await using var handler = ConfigurationManifestApplicationTestSupport.CreateHandler(factory);

                var response = await handler.ExecuteAsync(
                    new ApplyConfigurationManifestCommand(
                        ConfigurationManifestApplicationTestSupport.Source("first", "second")),
                    CancellationToken.None);

                await Assert.That(response.IsSuccess).IsFalse();
            }

            await using ExploreDbContext verification = factory.CreateDbContext();
            await Assert.That(await verification.Tenants.CountAsync()).IsEqualTo(0);
            await Assert.That(await verification.TenantSettingOverrides
                .IgnoreQueryFilters()
                .CountAsync()).IsEqualTo(0);
            await Assert.That(await verification.TenantSettingsDocuments
                .IgnoreQueryFilters()
                .CountAsync()).IsEqualTo(0);
            await Assert.That(await verification.OutboxMessages.CountAsync()).IsEqualTo(0);
            ConfigurationManifestOperation[] operations =
                await verification.ConfigurationManifestOperations
                    .AsNoTracking()
                    .ToArrayAsync();
            await Assert.That(operations).HasSingleItem();
            await Assert.That(operations[0].Status)
                .IsEqualTo(ConfigurationManifestOperationStatus.Failed);
            await Assert.That(await verification.ConfigurationManifestTenantResults
                .IgnoreQueryFilters()
                .CountAsync()).IsEqualTo(0);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    private static async Task<string> ApplyAsync(
        ConfigurationManifestApplicationTestSupport.TestDbContextFactory factory,
        Explore.Application.Features.ConfigurationManifest.Ingestion
            .ConfigurationManifestReadResult source)
    {
        await using var handler = ConfigurationManifestApplicationTestSupport.CreateHandler(factory);
        var response = await handler.ExecuteAsync(
            new ApplyConfigurationManifestCommand(source),
            CancellationToken.None);
        await Assert.That(response.IsSuccess).IsTrue();
        return handler.ArtifactDigest!;
    }

    private static async Task InitializeAsync(DbContextOptions<ExploreDbContext> options)
    {
        await using var context = new ExploreDbContext(options);
        context.EnableTenantFilterBypass("Configuration manifest application persistence test.");
        await context.Database.EnsureCreatedAsync();
        await SqliteDatabaseInitializer.InitializeAsync(context, CancellationToken.None);
        await LookupTableSeeder.SeedAsync(context, CancellationToken.None);
    }

    private static DbContextOptions<ExploreDbContext> CreateOptions(string databasePath) =>
        TestDbContextOptions.Create<ExploreDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .UseSnakeCaseNamingConvention()
            .Options;

    private static string TemporaryDatabasePath() =>
        Path.Combine(Path.GetTempPath(), $"tcm-apply-{Guid.CreateVersion7():N}.db");

    private sealed class ThrowingConsumer : INotificationHandler<SettingChangedNotification>
    {
        public Task HandleAsync(
            SettingChangedNotification notification,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Simulated post-commit effect failure.");
    }
}
