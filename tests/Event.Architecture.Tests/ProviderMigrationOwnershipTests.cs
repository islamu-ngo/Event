using Explore.Domain.Federation;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Schema;
using Explore.Persistence.ValueGenerators;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Event.Architecture.Tests;

public sealed class ProviderMigrationOwnershipTests
{
    [Test]
    [Arguments(PrimaryDatabaseProvider.PostgreSql, "Explore.Persistence.Schema.ConfigurableNpgsqlMigrationsSqlGenerator", 2)]
    [Arguments(PrimaryDatabaseProvider.Sqlite, "Explore.Persistence.Schema.ConfigurableSqliteMigrationsSqlGenerator", 2)]
    [Arguments(PrimaryDatabaseProvider.SqlServer, "Explore.Persistence.Schema.ConfigurableSqlServerMigrationsSqlGenerator", 2)]
    [Arguments(PrimaryDatabaseProvider.MariaDb, "Explore.Persistence.Schema.ConfigurableMySqlMigrationsSqlGenerator", 3)]
    [Arguments(PrimaryDatabaseProvider.MySql, "Explore.Persistence.Schema.ConfigurableMySqlMigrationsSqlGenerator", 3)]
    public async Task MigrationServicesResolveConfiguredGeneratorAndStableConstructorShape(
        PrimaryDatabaseProvider provider,
        string expectedGeneratorTypeName,
        int expectedConstructorParameterCount)
    {
        var builder = new DbContextOptionsBuilder<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(
            builder,
            CreateOptions(provider));
        await using var context = new ExploreDbContext(builder.Options);

        IMigrationsSqlGenerator generator =
            context.GetService<IMigrationsSqlGenerator>();
        IHistoryRepository history = context.GetService<IHistoryRepository>();
        IMigrationsAssembly migrations = context.GetService<IMigrationsAssembly>();

        Type generatorType = generator.GetType();
        await Assert.That(generatorType.FullName).IsEqualTo(expectedGeneratorTypeName);
        await Assert.That(generatorType.GetConstructors()).Count().IsEqualTo(1);
        await Assert.That(generatorType.GetConstructors().Single().GetParameters())
            .Count()
            .IsEqualTo(expectedConstructorParameterCount);
        await Assert.That(history).IsNotNull();
        await Assert.That(migrations.Assembly).IsNotNull();
    }

    [Test]
    [Arguments(PrimaryDatabaseProvider.PostgreSql)]
    [Arguments(PrimaryDatabaseProvider.Sqlite)]
    [Arguments(PrimaryDatabaseProvider.SqlServer)]
    [Arguments(PrimaryDatabaseProvider.MariaDb)]
    [Arguments(PrimaryDatabaseProvider.MySql)]
    public async Task MigrationServicesUseProviderModelDifferWithoutScaffoldTimeBackfillAdapter(
        PrimaryDatabaseProvider provider)
    {
        var builder = new DbContextOptionsBuilder<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(
            builder,
            CreateOptions(provider));
        await using var context = new ExploreDbContext(builder.Options);

        IMigrationsModelDiffer modelDiffer =
            context.GetService<IMigrationsModelDiffer>();

        await Assert.That(modelDiffer.GetType().FullName)
            .IsNotEqualTo("Explore.Persistence.Schema.ApplicationMigrationsModelDiffer");
        await Assert.That(modelDiffer.GetType().Assembly)
            .IsNotEqualTo(typeof(ExploreDbContext).Assembly);
    }

    [Test]
    [Arguments(PrimaryDatabaseProvider.PostgreSql, "Explore.Persistence", "Explore.Persistence")]
    [Arguments(PrimaryDatabaseProvider.Sqlite, "Explore.Persistence.Migrations.Sqlite", "Explore.Persistence.DataProtection.Migrations.Sqlite")]
    [Arguments(PrimaryDatabaseProvider.SqlServer, "Explore.Persistence.Migrations.SqlServer", "Explore.Persistence.DataProtection.Migrations.SqlServer")]
    [Arguments(PrimaryDatabaseProvider.MariaDb, "Explore.Persistence.Migrations.MySql", "Explore.Persistence.DataProtection.Migrations.MySql")]
    [Arguments(PrimaryDatabaseProvider.MySql, "Explore.Persistence.Migrations.MySql", "Explore.Persistence.DataProtection.Migrations.MySql")]
    public async Task ProviderCompositionAssignsExactMigrationOwners(
        PrimaryDatabaseProvider provider,
        string expectedApplicationAssembly,
        string expectedDataProtectionAssembly)
    {
        RelationalOptionsExtension application = Configure(
            provider,
            PrimaryDatabaseMigrationTarget.Application);
        RelationalOptionsExtension dataProtection = Configure(
            provider,
            PrimaryDatabaseMigrationTarget.DataProtection);

        await Assert.That(application.MigrationsAssembly).IsEqualTo(expectedApplicationAssembly);
        await Assert.That(dataProtection.MigrationsAssembly).IsEqualTo(expectedDataProtectionAssembly);
    }

    [Test]
    [Arguments(PrimaryDatabaseProvider.PostgreSql, "__EFMigrationsHistory", "__EFDataProtectionMigrationsHistory", "islamu_event")]
    [Arguments(PrimaryDatabaseProvider.Sqlite, "ie___EFMigrationsHistory", "ie___EFDataProtectionMigrationsHistory", null)]
    [Arguments(PrimaryDatabaseProvider.SqlServer, "__EFMigrationsHistory", "__EFDataProtectionMigrationsHistory", "islamu_event")]
    [Arguments(PrimaryDatabaseProvider.MariaDb, "ie___EFMigrationsHistory", "ie___EFDataProtectionMigrationsHistory", null)]
    [Arguments(PrimaryDatabaseProvider.MySql, "ie___EFMigrationsHistory", "ie___EFDataProtectionMigrationsHistory", null)]
    public async Task ProviderCompositionAssignsExactDistinctHistoryNamespaces(
        PrimaryDatabaseProvider provider,
        string expectedApplicationTable,
        string expectedDataProtectionTable,
        string? expectedSchema)
    {
        RelationalOptionsExtension application = Configure(
            provider,
            PrimaryDatabaseMigrationTarget.Application);
        RelationalOptionsExtension dataProtection = Configure(
            provider,
            PrimaryDatabaseMigrationTarget.DataProtection);

        await Assert.That(application.MigrationsHistoryTableName).IsEqualTo(expectedApplicationTable);
        await Assert.That(dataProtection.MigrationsHistoryTableName).IsEqualTo(expectedDataProtectionTable);
        await Assert.That(application.MigrationsHistoryTableSchema).IsEqualTo(expectedSchema);
        await Assert.That(dataProtection.MigrationsHistoryTableSchema).IsEqualTo(expectedSchema);
        await Assert.That(application.MigrationsHistoryTableName)
            .IsNotEqualTo(dataProtection.MigrationsHistoryTableName);
    }

    [Test]
    public async Task ProviderModelsUsePortableJetstreamCursorColumnNames()
    {
        foreach (var provider in Enum.GetValues<PrimaryDatabaseProvider>())
        {
            var builder = new DbContextOptionsBuilder<ExploreDbContext>();
            PrimaryDatabaseProviderComposition.ConfigureApplication(builder, CreateOptions(provider));
            await using var db = new ExploreDbContext(builder.Options);

            await AssertPortableCursorMapping<AtprotoJetstreamConsumerState>(
                db,
                nameof(AtprotoJetstreamConsumerState.Cursor),
                "ck_atproto_jetstream_cursor");
            await AssertPortableCursorMapping<AtprotoJetstreamQuarantine>(
                db,
                nameof(AtprotoJetstreamQuarantine.Cursor),
                "ck_atproto_jetstream_quarantine_cursor");
        }
    }

    [Test]
    public async Task ProviderModelsUseClientUuidV7AndPortableMySqlDefaults()
    {
        foreach (var provider in Enum.GetValues<PrimaryDatabaseProvider>())
        {
            var builder = new DbContextOptionsBuilder<ExploreDbContext>();
            PrimaryDatabaseProviderComposition.ConfigureApplication(builder, CreateOptions(provider));
            await using var db = new ExploreDbContext(builder.Options);
            IModel model = db.GetService<IDesignTimeModel>().Model;

            string[] defaultSql = model.GetEntityTypes()
                .SelectMany(entityType => entityType.GetProperties())
                .Select(property => property.GetDefaultValueSql())
                .Where(sql => sql is not null)
                .Cast<string>()
                .ToArray();
            await Assert.That(defaultSql.Any(sql =>
                sql.Contains("uuidv7()", StringComparison.OrdinalIgnoreCase))).IsFalse();

            IProperty idProperty = model.FindEntityType(typeof(AtprotoJetstreamConsumerState))!
                .FindProperty(nameof(AtprotoJetstreamConsumerState.Id))!;
            Type? generatorType = idProperty.GetValueGeneratorFactory()?
                .Invoke(idProperty, idProperty.DeclaringType)
                .GetType();
            await Assert.That(generatorType).IsEqualTo(typeof(GuidVersion7ValueGenerator));

            if (provider is PrimaryDatabaseProvider.MariaDb or PrimaryDatabaseProvider.MySql)
            {
                string[] unparenthesizedExpressions = defaultSql
                    .Where(sql => sql.Contains("UTC_TIMESTAMP()", StringComparison.OrdinalIgnoreCase))
                    .Where(sql => !sql.StartsWith('(') || !sql.EndsWith(')'))
                    .ToArray();
                await Assert.That(unparenthesizedExpressions).IsEmpty();

                string[] overlongColumnNames = model.GetEntityTypes()
                    .SelectMany(entityType => entityType.GetProperties())
                    .Select(property => property.GetColumnName())
                    .Where(columnName => columnName is not null && columnName.Length > 64)
                    .Cast<string>()
                    .ToArray();
                await Assert.That(overlongColumnNames).IsEmpty();

                IProperty[] portableCoalesceProperties = model.GetEntityTypes()
                    .SelectMany(entityType => entityType.GetProperties())
                    .Where(property => property.Name is
                        nameof(WebhookConsumer.ConfigurationScopeId) or
                        "RegistrationWorkflowVersionKey" or
                        "RegistrationProviderBindingKey" or
                        nameof(RegistrationRequirement.AppliesToSubjectKey) or
                        nameof(RegistrationAnswer.RequirementSubjectKey) or
                        nameof(RegistrationAnswer.EffectiveSubjectIdentity))
                    .ToArray();
                await Assert.That(portableCoalesceProperties).IsNotEmpty();
                await Assert.That(portableCoalesceProperties)
                    .All(property => property.GetComputedColumnSql() is null);
                await Assert.That(portableCoalesceProperties)
                    .All(property => property.ValueGenerated == ValueGenerated.Never);
                await Assert.That(portableCoalesceProperties)
                    .All(property => property.GetBeforeSaveBehavior() == PropertySaveBehavior.Save);
            }
        }
    }

    [Test]
    public async Task ProviderModelsPreserveAtomicAnswerChecksAndDecimalPrecision()
    {
        foreach (PrimaryDatabaseProvider provider in Enum.GetValues<PrimaryDatabaseProvider>())
        {
            var builder = new DbContextOptionsBuilder<ExploreDbContext>();
            PrimaryDatabaseProviderComposition.ConfigureApplication(builder, CreateOptions(provider));
            await using var db = new ExploreDbContext(builder.Options);
            IModel model = db.GetService<IDesignTimeModel>().Model;
            IEntityType answer = model.FindEntityType(typeof(RegistrationAnswer))!;
            string[] constraintSql = answer.GetCheckConstraints()
                .Where(constraint =>
                    constraint.Name == "ck_registration_answers_exactly_one_value" ||
                    constraint.Name == "ck_registration_answers_subject_shape")
                .Select(constraint => constraint.Sql)
                .ToArray();
            IProperty decimalValue = answer.FindProperty(nameof(RegistrationAnswer.DecimalValue))!;

            await Assert.That(constraintSql.Length).IsEqualTo(2);
            await Assert.That(constraintSql.All(sql =>
                !sql.Contains("num_nonnulls", StringComparison.OrdinalIgnoreCase) &&
                sql.Contains("CASE WHEN", StringComparison.OrdinalIgnoreCase))).IsTrue();
            await Assert.That(decimalValue.GetPrecision()).IsEqualTo(19);
            await Assert.That(decimalValue.GetScale()).IsEqualTo(4);
        }
    }

    private static async Task AssertPortableCursorMapping<TEntity>(
        ExploreDbContext db,
        string propertyName,
        string constraintName)
    {
        IModel model = db.GetService<IDesignTimeModel>().Model;
        IEntityType entityType = model.FindEntityType(typeof(TEntity))!;
        var table = StoreObjectIdentifier.Table(entityType.GetTableName()!, entityType.GetSchema());
        string? columnName = entityType.FindProperty(propertyName)!.GetColumnName(table);
        string? constraintSql = entityType.GetCheckConstraints()
            .Single(constraint => constraint.Name == constraintName)
            .Sql;

        await Assert.That(columnName).IsEqualTo("jetstream_cursor");
        await Assert.That(constraintSql).IsEqualTo("jetstream_cursor >= 0");
    }

    private static RelationalOptionsExtension Configure(
        PrimaryDatabaseProvider provider,
        PrimaryDatabaseMigrationTarget target)
    {
        var builder = new DbContextOptionsBuilder();
        var options = CreateOptions(provider);

        if (target == PrimaryDatabaseMigrationTarget.Application)
        {
            PrimaryDatabaseProviderComposition.ConfigureApplication(builder, options);
        }
        else
        {
            PrimaryDatabaseProviderComposition.ConfigureDataProtection(builder, options);
        }

        return builder.Options.Extensions.OfType<RelationalOptionsExtension>().Single();
    }

    private static PrimaryDatabaseConnectionOptions CreateOptions(PrimaryDatabaseProvider provider)
    {
        if (provider == PrimaryDatabaseProvider.Sqlite)
        {
            return new PrimaryDatabaseConnectionOptions
            {
                Role = PrimaryDatabaseRole.Migrator,
                Provider = provider,
                Database = "architecture-event.db",
            };
        }

        PrimaryDatabaseServerFlavor? flavor =
            Enum.TryParse(provider.ToString(), out PrimaryDatabaseServerFlavor parsedFlavor)
                ? parsedFlavor
                : null;
        return new PrimaryDatabaseConnectionOptions
        {
            Role = PrimaryDatabaseRole.Migrator,
            Provider = provider,
            Host = "database.example.test",
            Database = "event_db",
            Username = "migration_user",
            Password = "test-only-password",
            TlsMode = PrimaryDatabaseTlsMode.Required,
            ServerFlavor = flavor,
            ServerVersion = flavor is null ? null : new Version(11, 4),
        };
    }
}
