// ABOUTME: Proves manifest tenant, setting, document, result, and operation writes commit atomically.
// ABOUTME: Covers relational rerun idempotency, partial overlap, and isolated failure evidence.

using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Application.Features.ConfigurationManifest.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Settings.Documents;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Seed;
using MediatR;
using Microsoft.EntityFrameworkCore;

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
                var repository = new ConfigurationManifestOperationRepository(firstContext);
                var handler = ConfigurationManifestApplicationTestSupport.CreateHandler(
                    firstContext,
                    new ConfigurationManifestApplicationTestSupport.ExistencePreflight(
                        new TenantRepository(firstContext)),
                    repository,
                    new ConfigurationManifestFailureRepository(factory));
                var first = await handler.Handle(
                    new ApplyConfigurationManifestCommand(source),
                    CancellationToken.None);
                await Assert.That(first.IsSuccess).IsTrue();
            }

            await using (ExploreDbContext secondContext = factory.CreateDbContext())
            {
                var repository = new ConfigurationManifestOperationRepository(secondContext);
                var handler = ConfigurationManifestApplicationTestSupport.CreateHandler(
                    secondContext,
                    new ConfigurationManifestApplicationTestSupport.ExistencePreflight(
                        new TenantRepository(secondContext)),
                    repository,
                    new ConfigurationManifestFailureRepository(factory));
                var second = await handler.Handle(
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
                var repository = new ConfigurationManifestOperationRepository(apply);
                var handler = ConfigurationManifestApplicationTestSupport.CreateHandler(
                    apply,
                    new ConfigurationManifestApplicationTestSupport.ExistencePreflight(
                        new TenantRepository(apply)),
                    repository,
                    new ConfigurationManifestFailureRepository(factory));
                var response = await handler.Handle(
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
            string firstDigest = new('d', ConfigurationManifestOperation.DigestLength);
            string secondDigest = new('e', ConfigurationManifestOperation.DigestLength);

            await ApplyAsync(
                factory,
                ConfigurationManifestApplicationTestSupport.DifferentDigestSource(
                    firstDigest,
                    "Original Catalog",
                    "digest"));
            await ApplyAsync(
                factory,
                ConfigurationManifestApplicationTestSupport.DifferentDigestSource(
                    secondDigest,
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
            await Assert.That(secondOperation.CreatedTenantCount).IsEqualTo(0);
            await Assert.That(secondOperation.SkippedExistingTenantCount).IsEqualTo(1);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Test]
    public async Task Bootstrap_GuardedAndOrdinarySettingsCommitThroughCanonicalBoundaries()
    {
        string databasePath = TemporaryDatabasePath();
        try
        {
            DbContextOptions<ExploreDbContext> options = CreateOptions(databasePath);
            await InitializeAsync(options);
            var factory = new ConfigurationManifestApplicationTestSupport.TestDbContextFactory(options);
            await using (ExploreDbContext apply = factory.CreateDbContext())
            {
                var repository = new ConfigurationManifestOperationRepository(apply);
                var handler = ConfigurationManifestApplicationTestSupport.CreateHandler(
                    apply,
                    new ConfigurationManifestApplicationTestSupport.ExistencePreflight(
                        new TenantRepository(apply)),
                    repository,
                    new ConfigurationManifestFailureRepository(factory),
                    useRealPolicyBoundary: true);

                var response = await handler.Handle(
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
                var repository = new ConfigurationManifestOperationRepository(firstContext);
                var handler = ConfigurationManifestApplicationTestSupport.CreateHandler(
                    firstContext,
                    new ConfigurationManifestApplicationTestSupport.ExistencePreflight(
                        new TenantRepository(firstContext)),
                    repository,
                    new ConfigurationManifestFailureRepository(factory),
                    effectPublisher: new ThrowingPublisher());
                await Assert.That(() => handler.Handle(
                        new ApplyConfigurationManifestCommand(source),
                        CancellationToken.None))
                    .Throws<AggregateException>();
            }

            await using (ExploreDbContext secondContext = factory.CreateDbContext())
            {
                var repository = new ConfigurationManifestOperationRepository(secondContext);
                var handler = ConfigurationManifestApplicationTestSupport.CreateHandler(
                    secondContext,
                    new ConfigurationManifestApplicationTestSupport.ExistencePreflight(
                        new TenantRepository(secondContext)),
                    repository,
                    new ConfigurationManifestFailureRepository(factory));
                var response = await handler.Handle(
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
                var inner = new ConfigurationManifestOperationRepository(apply);
                var handler = ConfigurationManifestApplicationTestSupport.CreateHandler(
                    apply,
                    new ConfigurationManifestApplicationTestSupport.ExistencePreflight(
                        new TenantRepository(apply)),
                    new ThrowingOperationRepository(inner),
                    new ConfigurationManifestFailureRepository(factory));

                var response = await handler.Handle(
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
                var innerCreation = new Explore.Application.Services.TenantCreationService(
                    new TenantRepository(apply),
                    new TenantSettingsDocumentRepository(apply));
                var repository = new ConfigurationManifestOperationRepository(apply);
                var handler = ConfigurationManifestApplicationTestSupport.CreateHandler(
                    apply,
                    new ConfigurationManifestApplicationTestSupport.ExistencePreflight(
                        new TenantRepository(apply)),
                    repository,
                    new ConfigurationManifestFailureRepository(factory),
                    tenantCreationService: new FailOnSecondTenantCreationService(innerCreation));

                var response = await handler.Handle(
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

    private static async Task ApplyAsync(
        ConfigurationManifestApplicationTestSupport.TestDbContextFactory factory,
        Explore.Application.Features.ConfigurationManifest.Ingestion
            .ConfigurationManifestReadResult source)
    {
        await using ExploreDbContext context = factory.CreateDbContext();
        var repository = new ConfigurationManifestOperationRepository(context);
        var handler = ConfigurationManifestApplicationTestSupport.CreateHandler(
            context,
            new ConfigurationManifestApplicationTestSupport.ExistencePreflight(
                new TenantRepository(context)),
            repository,
            new ConfigurationManifestFailureRepository(factory));
        var response = await handler.Handle(
            new ApplyConfigurationManifestCommand(source),
            CancellationToken.None);
        await Assert.That(response.IsSuccess).IsTrue();
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
        new DbContextOptionsBuilder<ExploreDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .UseSnakeCaseNamingConvention()
            .Options;

    private static string TemporaryDatabasePath() =>
        Path.Combine(Path.GetTempPath(), $"tcm-apply-{Guid.CreateVersion7():N}.db");

    private sealed class ThrowingOperationRepository(
        IConfigurationManifestOperationRepository inner)
        : IConfigurationManifestOperationRepository
    {
        public Task<ConfigurationManifestOperation> CreateAsync(
            ConfigurationManifestOperation operation,
            IReadOnlyCollection<ConfigurationManifestTenantResult> tenantResults,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Simulated operation audit write failure.");

        public Task<ConfigurationManifestOperation?> GetLatestByDigestAsync(
            string digest,
            CancellationToken cancellationToken) =>
            inner.GetLatestByDigestAsync(digest, cancellationToken);

        public Task<ConfigurationManifestOperation?> GetByIdAsync(
            Guid operationId,
            CancellationToken cancellationToken) =>
            inner.GetByIdAsync(operationId, cancellationToken);

        public Task<ConfigurationManifestOperation?>
            GetLatestAppliedBootstrapAsync(
                CancellationToken cancellationToken) =>
            inner.GetLatestAppliedBootstrapAsync(cancellationToken);

        public Task<IReadOnlyList<ConfigurationManifestTenantResult>>
            GetResultsByOperationIdAsync(
                Guid operationId,
                CancellationToken cancellationToken) =>
            inner.GetResultsByOperationIdAsync(operationId, cancellationToken);

        public Task<ConfigurationManifestTenantResult?> GetCurrentTenantResultAsync(
            Guid operationId,
            CancellationToken cancellationToken) =>
            inner.GetCurrentTenantResultAsync(operationId, cancellationToken);
    }

    private sealed class FailOnSecondTenantCreationService(
        Explore.Application.Contracts.Services.ITenantCreationService inner)
        : Explore.Application.Contracts.Services.ITenantCreationService
    {
        private int _created;

        public async Task<Explore.Application.Contracts.Services.TenantCreationOutcome>
            CreateInCurrentTransactionAsync(
                Explore.Application.Contracts.Services.TenantCreationRequest request,
                CancellationToken cancellationToken)
        {
            Explore.Application.Contracts.Services.TenantCreationOutcome outcome =
                await inner.CreateInCurrentTransactionAsync(request, cancellationToken);
            if (Interlocked.Increment(ref _created) == 2)
            {
                throw new InvalidOperationException("Simulated second tenant failure.");
            }

            return outcome;
        }
    }

    private sealed class ThrowingPublisher : IPublisher
    {
        public Task Publish<TNotification>(
            TNotification notification,
            CancellationToken cancellationToken = default)
            where TNotification : INotification =>
            throw new InvalidOperationException("Simulated post-commit effect failure.");

        public Task Publish(
            object notification,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated post-commit effect failure.");
    }
}
