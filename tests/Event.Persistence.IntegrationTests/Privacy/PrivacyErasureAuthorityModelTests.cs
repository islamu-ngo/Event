// ABOUTME: Verifies generalized privacy-erasure EF models, retained composition, and function-only ACL contracts.
// ABOUTME: Pins User-only fact retention, replay coverage keys, receipt hashing, and topology isolation.

using Explore.Application.Contracts.PrivacyErasure;
using Explore.Domain;
using Explore.Infrastructure;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Privacy.ErasureAuthority;
using Explore.Persistence.Privacy.ErasureAuthority.Repositories;
using Explore.Secrets.Database;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Privacy;

[NotInParallel]
public sealed class PrivacyErasureAuthorityModelTests
{
    [Test]
    public async Task RetainedAuthorityModel_IsMinimizedTypedAndRetentionBounded()
    {
        var options = TestDbContextOptions.Create<PrivacyErasureAuthorityDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=unused;Password=unused")
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var context = new PrivacyErasureAuthorityDbContext(options);

        IModel model = context.GetService<IDesignTimeModel>().Model;
        IEntityType intent = model.FindEntityType(typeof(PrivacyErasureIntent))
            ?? throw new InvalidOperationException("Retained intent is not mapped.");

        await Assert.That(model.GetEntityTypes().Select(entity => entity.ClrType))
            .IsEquivalentTo([typeof(PrivacyErasureIntent), typeof(PrivacyErasureCounter)]);
        await Assert.That(intent.GetSchema()).IsEqualTo("privacy_erasure_authority");
        await Assert.That(intent.GetProperties().Select(property => property.GetColumnName()))
            .IsEquivalentTo([
                "authority_sequence", "intent_id", "policy_version", "reason_code",
                "recorded_at_utc", "requested_at_utc", "retention_expires_at_utc",
                "subject_id", "subject_kind", "is_legal_hold_pseudonymized"
            ]);
        await Assert.That(intent.GetCheckConstraints().Select(check => check.Sql))
            .Contains(sql => sql == "subject_kind = 1");
        await Assert.That(intent.GetCheckConstraints().Select(check => check.Sql))
            .Contains(sql => sql == "retention_expires_at_utc > recorded_at_utc");
    }

    [Test]
    public async Task ExploreModel_MapsSagaReceiptAndPolicyCoverageByTypedIntentIdentity()
    {
        await using ExploreDbContext context = CreateExploreContext();
        IModel model = context.Model;
        IEntityType saga = model.FindEntityType(typeof(PrivacyErasureSaga))
            ?? throw new InvalidOperationException("Privacy erasure saga is not mapped.");
        IEntityType coverage = model.FindEntityType(typeof(PrivacyErasurePolicyCoverage))
            ?? throw new InvalidOperationException("Privacy erasure policy coverage is not mapped.");
        IEntityType checkpoint = model.FindEntityType(typeof(PrivacyErasureReplayCheckpoint))
            ?? throw new InvalidOperationException("Privacy erasure replay checkpoint is not mapped.");

        await Assert.That(saga.FindProperty(nameof(PrivacyErasureSaga.ReceiptHash))!.GetMaxLength())
            .IsEqualTo(32);
        await Assert.That(saga.GetIndexes().Single(index =>
                index.Properties.Count == 1
                && index.Properties[0].Name == nameof(PrivacyErasureSaga.ReceiptHash))
            .IsUnique)
            .IsTrue();
        await Assert.That(coverage.FindPrimaryKey()!.Properties.Select(property => property.Name))
            .IsEquivalentTo([
                nameof(PrivacyErasurePolicyCoverage.IntentId),
                nameof(PrivacyErasurePolicyCoverage.SubjectKind),
                nameof(PrivacyErasurePolicyCoverage.PolicyVersion)
            ]);
        await Assert.That(checkpoint.GetTableName())
            .IsEqualTo("privacy_erasure_replay_checkpoints");
        await Assert.That(model.FindEntityType(typeof(PrivacyErasureIntent))).IsNull();
        await Assert.That(model.FindEntityType(typeof(PrivacyErasureCounter))).IsNull();
    }

    [Test]
    public async Task DataProtectionModel_RemainsUnchanged()
    {
        var options = TestDbContextOptions.Create<DataProtectionKeyContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=unused;Password=unused")
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var context = new DataProtectionKeyContext(options);
        IEntityType entity = context.Model.GetEntityTypes().Single();

        await Assert.That(entity.ClrType).IsEqualTo(typeof(DataProtectionKey));
        await Assert.That(entity.GetTableName()).IsEqualTo("data_protection_keys");
        await Assert.That(entity.GetProperties().Select(property => property.Name))
            .IsEquivalentTo(["FriendlyName", "Id", "Xml"]);
    }

    [Test]
    public async Task DefaultPersistenceComposition_RegistersEmbeddedAuthorityOnly()
    {
        var services = new ServiceCollection();
        services.ConfigurePersistenceServices(
            new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["PrivacyErasureAuthorityEmbedded:Path"] =
                        Path.Combine(Path.GetTempPath(), $"authority-{Guid.CreateVersion7():N}.db")
                }).Build(),
            skipDbContextRegistration: true,
            skipLookupCacheInitializer: true);

        await Assert.That(services.Any(item =>
            item.ServiceType == typeof(PrivacyErasureAuthorityDbContext))).IsFalse();
        await Assert.That(services.Any(item =>
            item.ServiceType == typeof(IPrivacyErasureAuthority)
            && item.ImplementationType == typeof(EmbeddedPrivacyErasureAuthorityRepository))).IsTrue();
        await Assert.That(services.Any(item =>
            item.ServiceType == typeof(IDbContextFactory<EmbeddedPrivacyErasureAuthorityDbContext>))).IsTrue();
    }

    [Test]
    public async Task DefaultComposition_PoisonAuthorityProviderIsNeverReadOrResolved()
    {
        var provider = new PoisonAuthorityConfigurationProvider();
        using var configuration = new ConfigurationRoot([provider]);
        var services = new ServiceCollection();

        services.ConfigurePersistenceServices(
            configuration,
            skipDbContextRegistration: true,
            skipLookupCacheInitializer: true);
        services.ConfigureInfrastructureServices(configuration);

        await Assert.That(provider.AuthorityReadCount).IsEqualTo(0);
        await Assert.That(services.Any(item =>
            item.ServiceType == typeof(PrivacyErasureAuthorityDbContext))).IsFalse();
        await Assert.That(services.Any(item =>
            item.ServiceType == typeof(IPrivacyErasureAuthority)
            && item.ImplementationType == typeof(EmbeddedPrivacyErasureAuthorityRepository))).IsTrue();
        await Assert.That(services.Any(item =>
            item.ServiceType.FullName?.Contains(
                "IPrivacyErasureReplayService",
                StringComparison.Ordinal) == true)).IsTrue();
    }

    [Test]
    public async Task ExternalPersistenceComposition_RegistersOnlyGeneralizedAuthoritySurface()
    {
        var services = new ServiceCollection();
        services.ConfigurePersistenceServices(
            new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["PrivacyErasure:Authority:Topology"] = "ExternalDatabase",
                    ["PrivacyErasureAuthorityDatabase:Provider"] = "PostgreSql",
                    ["PrivacyErasureAuthorityDatabase:Host"] = "localhost",
                    ["PrivacyErasureAuthorityDatabase:Database"] = "privacy_erasure",
                    ["PrivacyErasureAuthorityDatabase:TlsMode"] = "Prefer",
                    ["PrivacyErasureAuthorityDatabase:Runtime:Username"] = "runtime",
                    ["PrivacyErasureAuthorityDatabase:Runtime:Password"] = "unused"
                }).Build(),
            skipDbContextRegistration: true,
            skipLookupCacheInitializer: true);

        await Assert.That(services.Any(item =>
            item.ServiceType == typeof(PrivacyErasureAuthorityDbContext))).IsTrue();
        await Assert.That(services.Any(item =>
            item.ServiceType == typeof(IPrivacyErasureAuthority))).IsTrue();
    }

    [Test]
    public async Task RuntimeAuthorityContract_IsFunctionOnlyWithDefaultPrivilegeRevokes()
    {
        string acl = PrivacyErasureAuthorityDatabaseContract.RuntimeAclSql;
        string normalized = acl.ToUpperInvariant();

        await Assert.That(normalized).Contains("GRANT EXECUTE ON FUNCTION");
        await Assert.That(normalized).Contains("REVOKE ALL ON ALL TABLES");
        await Assert.That(normalized).Contains("REVOKE ALL ON ALL SEQUENCES");
        await Assert.That(normalized).Contains("ALTER DEFAULT PRIVILEGES");
        await Assert.That(normalized).DoesNotContain("GRANT SELECT ON");
        await Assert.That(normalized).DoesNotContain("GRANT INSERT ON");
        await Assert.That(normalized).DoesNotContain("GRANT UPDATE ON");
        await Assert.That(normalized).DoesNotContain("GRANT DELETE ON");
        await Assert.That(normalized).DoesNotContain("GRANT TRUNCATE ON");
        await Assert.That(typeof(EfCorePrivacyErasureAuthorityRepository)
            .GetMethods()
            .Where(method =>
                method.DeclaringType == typeof(EfCorePrivacyErasureAuthorityRepository))
            .Select(method => method.Name)
            .Distinct())
            .IsEquivalentTo([
                "AppendAsync",
                "ReadAfterAsync",
                "GetStateAsync",
                "EvaluateRetentionAsync",
                "CompactExpiredIntentsAsync"
            ]);
    }

    [Test]
    public async Task AuthorityModels_HaveExactNonOverlappingOwnership()
    {
        await using ExploreDbContext application = CreateExploreContext();
        await using var external = new PrivacyErasureAuthorityDbContext(
            TestDbContextOptions.Create<PrivacyErasureAuthorityDbContext>()
                .UseNpgsql("Host=localhost;Database=model_only;Username=unused;Password=unused")
                .UseSnakeCaseNamingConvention()
                .Options);
        var coLocatedOptions = TestDbContextOptions.Create<CoLocatedPrivacyErasureAuthorityDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureCoLocatedPrivacyErasureAuthority(
            coLocatedOptions,
            new PrimaryDatabaseConnectionOptions
            {
                Role = PrimaryDatabaseRole.Runtime,
                Provider = PrimaryDatabaseProvider.PostgreSql,
                Host = "localhost",
                Database = "model_only",
                Schema = "custom_event",
                Username = "unused",
                Password = "unused",
                TlsMode = PrimaryDatabaseTlsMode.Disabled
            });
        await using var coLocated = new CoLocatedPrivacyErasureAuthorityDbContext(
            coLocatedOptions.Options);
        await using var embedded = new EmbeddedPrivacyErasureAuthorityDbContext(
            TestDbContextOptions.Create<EmbeddedPrivacyErasureAuthorityDbContext>()
                .UseSqlite("Data Source=:memory:")
                .Options);

        await Assert.That(application.Model.FindEntityType(typeof(PrivacyErasureIntent))).IsNull();
        await Assert.That(application.Model.FindEntityType(typeof(PrivacyErasureCounter))).IsNull();
        await Assert.That(external.Model.FindEntityType(typeof(PrivacyErasureIntent))!.GetSchema())
            .IsEqualTo("privacy_erasure_authority");
        await Assert.That(coLocated.Model.FindEntityType(typeof(PrivacyErasureIntent))!.GetSchema())
            .IsEqualTo("custom_event");
        await Assert.That(embedded.Model.FindEntityType(typeof(PrivacyErasureIntent))!.GetTableName())
            .IsEqualTo("ie_erasure_intents");
        await Assert.That(embedded.Model.FindEntityType(typeof(PrivacyErasureIntent))!.GetSchema())
            .IsNull();
        await Assert.That(EmbeddedPrivacyErasureAuthorityDbContext.MigrationsHistoryTable)
            .IsNotEqualTo("__EFMigrationsHistory");
    }

    [Test]
    [TUnit.Core.Executors.TestExecutor<FreshEfProcessExecutor>]
    public async Task AuthorityTopologies_HaveExactMigrationOwnersAndHistoryNamespaces()
    {
        var embeddedOptions = TestDbContextOptions.Create<EmbeddedPrivacyErasureAuthorityDbContext>();
        EmbeddedPrivacyErasureAuthorityDbContextFactory.Configure(
            embeddedOptions,
            new EmbeddedPrivacyErasureAuthorityOptions
            {
                Path = Path.Combine(Path.GetTempPath(), $"authority-{Guid.CreateVersion7():N}.db")
            });
        await using var embedded = new EmbeddedPrivacyErasureAuthorityDbContext(embeddedOptions.Options);

        var coLocatedSqliteOptions =
            TestDbContextOptions.Create<EmbeddedPrivacyErasureAuthorityDbContext>();
        EmbeddedPrivacyErasureAuthorityDbContextFactory.ConfigureCoLocated(
            coLocatedSqliteOptions,
            new PrimaryDatabaseConnectionOptions
            {
                Role = PrimaryDatabaseRole.Migrator,
                Provider = PrimaryDatabaseProvider.Sqlite,
                Database = "model-only.db"
            });

        var coLocatedOptions = TestDbContextOptions.Create<CoLocatedPrivacyErasureAuthorityDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureCoLocatedPrivacyErasureAuthority(
            coLocatedOptions,
            new PrimaryDatabaseConnectionOptions
            {
                Role = PrimaryDatabaseRole.Migrator,
                Provider = PrimaryDatabaseProvider.PostgreSql,
                Host = "localhost",
                Database = "model_only",
                Schema = "custom_event",
                Username = "unused",
                Password = "unused",
                TlsMode = PrimaryDatabaseTlsMode.Disabled
            });
        await using var coLocated = new CoLocatedPrivacyErasureAuthorityDbContext(coLocatedOptions.Options);

        await using PrivacyErasureAuthorityDbContext external =
            new PrivacyErasureAuthorityDbContextFactory().CreateDbContext(
                new ConfigurationBuilder().AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["PrivacyErasureAuthorityDatabase:Provider"] = "PostgreSql",
                        ["PrivacyErasureAuthorityDatabase:Host"] = "localhost",
                        ["PrivacyErasureAuthorityDatabase:Database"] = "model_only",
                        ["PrivacyErasureAuthorityDatabase:TlsMode"] = "Prefer",
                        ["PrivacyErasureAuthorityDatabase:Migrator:Username"] = "unused",
                        ["PrivacyErasureAuthorityDatabase:Migrator:Password"] = "unused"
                    }).Build());

        RelationalOptionsExtension embeddedRelational = embeddedOptions.Options.Extensions
            .OfType<RelationalOptionsExtension>()
            .Single();
        RelationalOptionsExtension coLocatedRelational = coLocatedOptions.Options.Extensions
            .OfType<RelationalOptionsExtension>()
            .Single();
        RelationalOptionsExtension coLocatedSqliteRelational = coLocatedSqliteOptions.Options.Extensions
            .OfType<RelationalOptionsExtension>()
            .Single();
        RelationalOptionsExtension externalRelational = external.GetService<IDbContextOptions>()
            .Extensions
            .OfType<RelationalOptionsExtension>()
            .Single();

        await Assert.That((
            embeddedRelational.MigrationsAssembly,
            embeddedRelational.MigrationsHistoryTableName,
            embeddedRelational.MigrationsHistoryTableSchema)).IsEqualTo((
                EmbeddedPrivacyErasureAuthorityDbContext.MigrationsAssembly,
                EmbeddedPrivacyErasureAuthorityDbContext.MigrationsHistoryTable,
                (string?)null));
        await Assert.That((
            coLocatedSqliteRelational.MigrationsAssembly,
            coLocatedSqliteRelational.MigrationsHistoryTableName,
            coLocatedSqliteRelational.MigrationsHistoryTableSchema)).IsEqualTo((
                EmbeddedPrivacyErasureAuthorityDbContext.MigrationsAssembly,
                EmbeddedPrivacyErasureAuthorityDbContext.MigrationsHistoryTable,
                (string?)null));
        await Assert.That((
            coLocatedRelational.MigrationsAssembly,
            coLocatedRelational.MigrationsHistoryTableName,
            coLocatedRelational.MigrationsHistoryTableSchema)).IsEqualTo((
                "Explore.Persistence",
                PrivacyErasureAuthorityDatabaseConfiguration.MigrationsHistoryTable,
                "custom_event"));
        await Assert.That((
            externalRelational.MigrationsAssembly,
            externalRelational.MigrationsHistoryTableName,
            externalRelational.MigrationsHistoryTableSchema)).IsEqualTo((
                "Explore.Persistence",
                PrivacyErasureAuthorityDatabaseConfiguration.MigrationsHistoryTable,
                (string?)null));

        await Assert.That(embedded.GetService<IHistoryRepository>().GetCreateIfNotExistsScript())
            .Contains(EmbeddedPrivacyErasureAuthorityDbContext.MigrationsHistoryTable);
        await Assert.That(coLocated.GetService<IHistoryRepository>().GetCreateIfNotExistsScript())
            .Contains("custom_event");
        await Assert.That(external.GetService<IHistoryRepository>().GetCreateIfNotExistsScript())
            .Contains(PrivacyErasureAuthorityDatabaseConfiguration.MigrationsHistoryTable);
    }

    private static ExploreDbContext CreateExploreContext()
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=unused;Password=unused")
            .UseSnakeCaseNamingConvention()
            .Options;
        return new ExploreDbContext(options);
    }

    private sealed class PoisonAuthorityConfigurationProvider : ConfigurationProvider
    {
        private static readonly string[] ForbiddenPrefixes =
        [
            "ConnectionStrings:PrivacyErasureAuthority",
            "ConnectionStrings:LocationPrivacyAuthority",
            "LocationPrivacy:ErasureAuthority",
            "LocationPrivacy:ErasureDurability"
        ];

        private int _authorityReadCount;

        public int AuthorityReadCount => Volatile.Read(ref _authorityReadCount);

        public override bool TryGet(string key, out string? value)
        {
            ThrowIfAuthorityKey(key);
            value = null;
            return false;
        }

        public override IEnumerable<string> GetChildKeys(
            IEnumerable<string> earlierKeys,
            string? parentPath)
        {
            if (parentPath is not null)
            {
                ThrowIfAuthorityKey(parentPath);
            }

            return earlierKeys;
        }

        private void ThrowIfAuthorityKey(string key)
        {
            if (!ForbiddenPrefixes.Any(prefix =>
                    key.Equals(prefix, StringComparison.OrdinalIgnoreCase)
                    || key.StartsWith($"{prefix}:", StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            Interlocked.Increment(ref _authorityReadCount);
            throw new InvalidOperationException("Default composition read an authority configuration key.");
        }
    }
}
