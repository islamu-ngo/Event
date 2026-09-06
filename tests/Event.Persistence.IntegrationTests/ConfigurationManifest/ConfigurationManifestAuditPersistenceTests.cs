// ABOUTME: Verifies configuration-manifest audit persistence is safe, append-oriented, and tenant isolated.
// ABOUTME: Proves failed operations survive through a fresh context without reviving rolled-back writes.

using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Event.Persistence.IntegrationTests.ConfigurationManifest;

[NotInParallel("ConfigurationManifestAuditPersistence")]
public sealed class ConfigurationManifestAuditPersistenceTests
{
    private static readonly DateTime StartedAt = new(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime CompletedAt = StartedAt.AddSeconds(2);

    [Test]
    public async Task Model_StoresOnlyBoundedSafeAuditMetadata()
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseSqlite("Data Source=:memory:")
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var context = new ExploreDbContext(options);
        IModel model = context.GetService<IDesignTimeModel>().Model;

        IEntityType operation = model.FindEntityType(typeof(ConfigurationManifestOperation))!;
        IEntityType result = model.FindEntityType(typeof(ConfigurationManifestTenantResult))!;

        await Assert.That(operation.FindProperty(nameof(ConfigurationManifestOperation.Id))!.ValueGenerated)
            .IsEqualTo(ValueGenerated.Never);
        await Assert.That(operation.FindProperty(nameof(ConfigurationManifestOperation.Digest))!.GetMaxLength())
            .IsEqualTo(64);
        await Assert.That(operation.FindProperty(
                nameof(ConfigurationManifestOperation
                    .InstanceSectionDigest))!.GetMaxLength())
            .IsEqualTo(ConfigurationManifestOperation.DigestLength);
        await Assert.That(operation.FindProperty(
                nameof(ConfigurationManifestOperation
                    .BootstrapGeneration)))
            .IsNotNull();
        await Assert.That(operation.GetIndexes().Any(index =>
                index.Properties.Select(property => property.Name).SequenceEqual(
                    [
                        nameof(ConfigurationManifestOperation.Digest),
                        nameof(ConfigurationManifestOperation.Mode),
                        nameof(ConfigurationManifestOperation.CompletedAt)
                    ])))
            .IsTrue();
        await Assert.That(result.FindDeclaredQueryFilter(QueryFilterNames.Tenant)).IsNotNull();
        await Assert.That(result.GetIndexes().Any(index =>
                index.IsUnique
                && index.Properties.Select(property => property.Name).SequenceEqual(
                    [
                        nameof(ConfigurationManifestTenantResult.TenantId),
                        nameof(ConfigurationManifestTenantResult.OperationId)
                    ])))
            .IsTrue();

        string[] forbiddenFragments = ["Raw", "Payload", "Value", "Secret", "Report", "Content"];
        string[] persistedPropertyNames = operation.GetProperties()
            .Concat(result.GetProperties())
            .Select(property => property.Name)
            .ToArray();

        await Assert.That(persistedPropertyNames.Any(name =>
                forbiddenFragments.Any(fragment => name.Contains(fragment, StringComparison.OrdinalIgnoreCase))))
            .IsFalse();
    }

    [Test]
    public async Task TenantResult_Create_NormalizesChangedKeyNamesAndRejectsUnsafeReasonData()
    {
        var result = ConfigurationManifestTenantResult.Create(
            operationId: Guid.CreateVersion7(),
            tenantId: Guid.CreateVersion7(),
            status: ConfigurationManifestTenantResultStatus.Created,
            changedSettingKeyNames:
            [
                "event_reporting.intake_enabled",
                "event_reporting.intake_enabled"
            ],
            changedDocumentKeyNames: ["branding"],
            completedAt: CompletedAt);

        await Assert.That(result.Id.Version).IsEqualTo(7);
        await Assert.That(result.ChangedKeyNames)
            .IsEquivalentTo(["branding", "event_reporting.intake_enabled"]);

        await Assert.That(() => ConfigurationManifestOperation.Create(
                ConfigurationManifestAuditMode.Bootstrap,
                "configuration.islamu.org/v1alpha2",
                "ConfigurationManifest",
                "production",
                new string('a', 64),
                ConfigurationManifestOperationStatus.Failed,
                requestedTenantCount: 1,
                createdTenantCount: 0,
                skippedExistingTenantCount: 0,
                failedTenantCount: 1,
                reasonCode: "invalid",
                reason: new string('x', 501),
                StartedAt,
                CompletedAt))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Repository_CurrentTenantResultCannotCrossAmbientTenant()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using ExploreDbContext seedContext = CreateContext(connection);
        await seedContext.Database.EnsureCreatedAsync();
        await SqliteDatabaseInitializer.InitializeAsync(seedContext, CancellationToken.None);
        await Explore.Persistence.Seed.LookupTableSeeder.SeedAsync(seedContext, CancellationToken.None);

        Tenant tenantA = CreateTenant("manifest-a");
        Tenant tenantB = CreateTenant("manifest-b");
        seedContext.AddRange(tenantA, tenantB);
        await seedContext.SaveChangesAsync();

        ConfigurationManifestOperation operation = CreateAppliedOperation();
        var repository = new ConfigurationManifestOperationRepository(seedContext);
        await repository.CreateAsync(
            operation,
            [
                CreateAppliedResult(operation.Id, tenantA.Id, "branding"),
                CreateAppliedResult(operation.Id, tenantB.Id, "event_reporting.intake_enabled")
            ],
            CancellationToken.None);
        ConfigurationManifestOperation? bootstrap =
            await repository.GetLatestAppliedBootstrapAsync(
                CancellationToken.None);
        await Assert.That(bootstrap?.InstanceSectionDigest)
            .IsEqualTo(operation.InstanceSectionDigest);
        await Assert.That(bootstrap?.BootstrapGeneration)
            .IsEqualTo(operation.BootstrapGeneration);
        IReadOnlyList<ConfigurationManifestTenantResult> replayResults =
            await repository.GetResultsByOperationIdAsync(
                operation.Id,
                CancellationToken.None);
        await Assert.That(replayResults.Count).IsEqualTo(2);

        await using ExploreDbContext tenantAContext = CreateContext(connection, tenantA.Id);
        await using ExploreDbContext tenantBContext = CreateContext(connection, tenantB.Id);
        await using ExploreDbContext noTenantContext = CreateContext(connection);

        ConfigurationManifestTenantResult? tenantAResult =
            await new ConfigurationManifestOperationRepository(tenantAContext)
                .GetCurrentTenantResultAsync(operation.Id, CancellationToken.None);
        ConfigurationManifestTenantResult? tenantBResult =
            await new ConfigurationManifestOperationRepository(tenantBContext)
                .GetCurrentTenantResultAsync(operation.Id, CancellationToken.None);
        ConfigurationManifestTenantResult? hiddenResult =
            await new ConfigurationManifestOperationRepository(noTenantContext)
                .GetCurrentTenantResultAsync(operation.Id, CancellationToken.None);

        await Assert.That(tenantAResult!.TenantId).IsEqualTo(tenantA.Id);
        await Assert.That(tenantBResult!.TenantId).IsEqualTo(tenantB.Id);
        await Assert.That(hiddenResult).IsNull();
    }

    [Test]
    public async Task FailureRepository_PersistsOnlyFailureAfterApplyTransactionRollsBack()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"tcm-audit-{Guid.CreateVersion7():N}.db");
        string connectionString = $"Data Source={databasePath}";
        try
        {
            DbContextOptions<ExploreDbContext> options = TestDbContextOptions.Create<ExploreDbContext>()
                .UseSqlite(connectionString)
                .UseSnakeCaseNamingConvention()
                .Options;
            await using (var initializeContext = new ExploreDbContext(options))
            {
                await initializeContext.Database.EnsureCreatedAsync();
            }

            await using (var applyContext = new ExploreDbContext(options))
            {
                var unitOfWork = new EfCoreUnitOfWork(applyContext);
                await Assert.That(async () => await unitOfWork.ExecuteInTransactionAsync(
                        async cancellationToken =>
                        {
                            applyContext.Add(CreateAppliedOperation());
                            await applyContext.SaveChangesAsync(cancellationToken);
                            throw new InvalidOperationException("Simulated bootstrap failure.");
                        },
                        CancellationToken.None))
                    .Throws<InvalidOperationException>();
            }

            var recorder = new ConfigurationManifestFailureRepository(
                new TestExploreDbContextFactory(options));
            await recorder.RecordAsync(CreateFailedOperation(), CancellationToken.None);

            await using var verificationContext = new ExploreDbContext(options);
            ConfigurationManifestOperation[] persisted =
                await verificationContext.ConfigurationManifestOperations
                    .AsNoTracking()
                    .ToArrayAsync();

            await Assert.That(persisted).HasSingleItem();
            await Assert.That(persisted[0].Status)
                .IsEqualTo(ConfigurationManifestOperationStatus.Failed);
            await Assert.That(persisted[0].ReasonCode).IsEqualTo("bootstrap_write_failed");
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Test]
    public async Task AuditEvidence_AllowsRepeatedDigestButRejectsMutationAndDeletion()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using ExploreDbContext context = CreateContext(connection);
        await context.Database.EnsureCreatedAsync();
        var repository = new ConfigurationManifestOperationRepository(context);
        ConfigurationManifestOperation first = CreateFailedOperation();
        ConfigurationManifestOperation second = CreateFailedOperation();

        await repository.CreateAsync(first, [], CancellationToken.None);
        await repository.CreateAsync(second, [], CancellationToken.None);

        ConfigurationManifestOperation[] repeatedDigest =
            await context.ConfigurationManifestOperations
                .AsNoTracking()
                .Where(operation => operation.Digest == first.Digest)
                .ToArrayAsync();
        await Assert.That(repeatedDigest).Count().IsEqualTo(2);

        context.Entry(first).Property(nameof(ConfigurationManifestOperation.Reason)).CurrentValue =
            "Tampered audit evidence.";
        await Assert.That(() => context.SaveChangesAsync())
            .Throws<InvalidOperationException>();

        context.Entry(first).State = EntityState.Unchanged;
        context.Remove(first);
        await Assert.That(() => context.SaveChangesAsync())
            .Throws<InvalidOperationException>();
    }

    private static ExploreDbContext CreateContext(SqliteConnection connection, Guid? tenantId = null)
    {
        var context = new ExploreDbContext(
            TestDbContextOptions.Create<ExploreDbContext>()
                .UseSqlite(connection)
                .UseSnakeCaseNamingConvention()
                .Options);
        context.TenantContext = tenantId.HasValue ? new TestTenantContext(tenantId.Value) : null;
        return context;
    }

    private static Tenant CreateTenant(string slug) => new()
    {
        Id = Guid.CreateVersion7(),
        FullName = slug,
        Slug = slug,
        TenantStatusId = (int)TenantStatusEnum.Active,
        TenantStatus = null!,
        CreatedAt = StartedAt
    };

    private static ConfigurationManifestOperation CreateAppliedOperation() =>
        ConfigurationManifestOperation.Create(
            ConfigurationManifestAuditMode.Bootstrap,
            "configuration.islamu.org/v1alpha2",
            "ConfigurationManifest",
            "production",
            new string('a', 64),
            ConfigurationManifestOperationStatus.Applied,
            requestedTenantCount: 2,
            createdTenantCount: 2,
            skippedExistingTenantCount: 0,
            failedTenantCount: 0,
            reasonCode: null,
            reason: null,
            StartedAt,
            CompletedAt,
            instanceSectionDigest: new string('c', 64),
            bootstrapGeneration: 1);

    private static ConfigurationManifestOperation CreateFailedOperation() =>
        ConfigurationManifestOperation.Create(
            ConfigurationManifestAuditMode.Bootstrap,
            "configuration.islamu.org/v1alpha2",
            "ConfigurationManifest",
            "production",
            new string('b', 64),
            ConfigurationManifestOperationStatus.Failed,
            requestedTenantCount: 1,
            createdTenantCount: 0,
            skippedExistingTenantCount: 0,
            failedTenantCount: 1,
            "bootstrap_write_failed",
            "Tenant configuration bootstrap could not be committed.",
            StartedAt,
            CompletedAt);

    private static ConfigurationManifestTenantResult CreateAppliedResult(
        Guid operationId,
        Guid tenantId,
        string changedKeyName) =>
        ConfigurationManifestTenantResult.Create(
            operationId,
            tenantId,
            ConfigurationManifestTenantResultStatus.Created,
            [changedKeyName],
            [],
            CompletedAt);

    private sealed record TestTenantContext(Guid TenantId) : ITenantContext;

    private sealed class TestExploreDbContextFactory(DbContextOptions<ExploreDbContext> options)
        : IDbContextFactory<ExploreDbContext>
    {
        public ExploreDbContext CreateDbContext() => new(options);
    }
}
