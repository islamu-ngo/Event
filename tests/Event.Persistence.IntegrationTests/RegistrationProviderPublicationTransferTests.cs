using System.Reflection;
using Event.Persistence.IntegrationTests.Database;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;

namespace Event.Persistence.IntegrationTests;

// Generation-only contracts: these tests never open a connection or execute migration SQL.
public sealed class RegistrationProviderPublicationTransferTests
{
    [Test]
    [Arguments("PostgreSql")]
    [Arguments("Sqlite")]
    [Arguments("SqlServer")]
    [Arguments("MariaDb")]
    [Arguments("MySql")]
    public async Task TransferProjectsUnchangedEvidenceBeforeNativeConstraintsOrSqliteRebuild(string provider)
    {
        using var context = CreateContext(provider);
        var sql = context.GetService<ISqlGenerationHelper>();
        var create = PublicationTable(context);
        var binding = BindingTable(create.Name);
        var attempts = AttemptsTable(create.Name);
        MigrationOperation[] operations =
        [
            new DropForeignKeyOperation { Table = attempts, Schema = create.Schema, Name = "fk_old_publication" },
            new DropUniqueConstraintOperation { Table = binding, Schema = create.Schema, Name = "ak_old_publication" },
            new AddCheckConstraintOperation
            {
                Table = binding, Schema = create.Schema, Name = "ck_transfer_binding", Sql = "state_id > 0"
            },
            create,
            PublicationForeignKey(create, binding, binding: true),
            PublicationForeignKey(create, attempts, binding: false)
        ];
        var prepared = Prepare(context, operations);
        var transfer = prepared.OfType<SqlOperation>().Single(operation =>
            operation.Sql.StartsWith($"INSERT INTO {sql.DelimitIdentifier(create.Name, create.Schema)}", StringComparison.Ordinal));
        string projection = $"SELECT b.{sql.DelimitIdentifier("id")}, b.{sql.DelimitIdentifier("tenant_id")}, "
            + $"b.{sql.DelimitIdentifier("id")}, b.{sql.DelimitIdentifier("published_mapping_revision_hash_key")}, "
            + $"b.{sql.DelimitIdentifier("published_at")}";
        await Assert.That(transfer.Sql).Contains(projection);
        await Assert.That(transfer.Sql).Contains($"b.{sql.DelimitIdentifier("state_id")} = 2");
        await Assert.That(transfer.Sql.Contains("is_deleted", StringComparison.Ordinal)).IsFalse();
        await Assert.That(transfer.SuppressTransaction).IsFalse();
        await Assert.That(ReferenceEquals(Prepare(context, prepared), prepared)).IsTrue();

        string script = Generate(context, prepared);
        int creation = script.IndexOf($"CREATE TABLE {sql.DelimitIdentifier(create.Name, create.Schema)}", StringComparison.Ordinal);
        int insertion = script.IndexOf(transfer.Sql.TrimEnd(';', '\r', '\n'), StringComparison.Ordinal);
        int constraint = script.IndexOf(provider == "Sqlite" ? "ef_temp_" : "ck_transfer_binding", StringComparison.Ordinal);
        await Assert.That(creation >= 0 && insertion > creation && constraint > insertion).IsTrue();
        await Assert.That(operations[2] is AddCheckConstraintOperation).IsTrue();
    }

    [Test]
    [Arguments("PostgreSql", "COLLATE \"C\"", "{42}[AEIMQUYcgkosw048]=$")]
    [Arguments("Sqlite", "AS BLOB", "NOT GLOB '*[^A-Za-z0-9+/]*'")]
    [Arguments("SqlServer", "varbinary(max)", "Latin1_General_100_BIN2 NOT LIKE '%[^A-Za-z0-9+/]%'")]
    [Arguments("MariaDb", "AS BINARY", "{42}[AEIMQUYcgkosw048]=$")]
    [Arguments("MySql", "AS BINARY", "{42}[AEIMQUYcgkosw048]=$")]
    public async Task PreflightChecksNullShapesOrdinalHashesAndTenantBoundAttemptEvidence(string provider, string ordinalSql, string canonicalSql)
    {
        using var context = CreateContext(provider);
        var sql = context.GetService<ISqlGenerationHelper>();
        var prepared = Prepare(context, [PublicationTable(context)]);
        string preflight = prepared.OfType<SqlOperation>().First().Sql;
        await Assert.That(preflight).Contains(ordinalSql);
        await Assert.That(preflight).Contains(canonicalSql);
        await Assert.That(preflight).Contains("THEN 0 ELSE 1 END = 1");
        await Assert.That(preflight).Contains($"b.{sql.DelimitIdentifier("published_mapping_revision_hash")} IS NULL");
        await Assert.That(preflight).Contains($"b.{sql.DelimitIdentifier("published_at")} IS NULL");
        await Assert.That(preflight).Contains($"a.{sql.DelimitIdentifier("registration_provider_binding_id")} IS NULL");
        await Assert.That(preflight).Contains($"a.{sql.DelimitIdentifier("provider_mapping_revision_hash")} IS NULL");
        await Assert.That(preflight).Contains($"a.{sql.DelimitIdentifier("provider_mapping_revision_hash_key")}");
        await Assert.That(preflight).Contains($"b.{sql.DelimitIdentifier("tenant_id")} = a.{sql.DelimitIdentifier("tenant_id")}");
        await Assert.That(preflight).Contains("EXISTS (SELECT 1 FROM");
        await Assert.That(preflight.Contains("is_deleted", StringComparison.Ordinal)).IsFalse();

        // Native CHECK DDL must guard a PII-free integer, never expose a failing binding row.
        var guard = (CreateTableOperation)prepared[0];
        await Assert.That(guard.Columns.Single().ClrType).IsEqualTo(typeof(int));
        await Assert.That(guard.CheckConstraints.Single().Sql).Contains("= 0");
        string script = Generate(context, prepared);
        int check = script.IndexOf("CHECK", StringComparison.Ordinal);
        int validation = script.IndexOf(preflight.TrimEnd(';', '\r', '\n'), StringComparison.Ordinal);
        await Assert.That(check >= 0 && validation > check).IsTrue();
        if (provider == "Sqlite")
        {
            int disableForeignKeys = script.IndexOf("PRAGMA foreign_keys = OFF", StringComparison.OrdinalIgnoreCase);
            await Assert.That(disableForeignKeys < 0 || validation < disableForeignKeys).IsTrue();
        }
    }

    [Test]
    [Arguments("PostgreSql")]
    [Arguments("Sqlite")]
    [Arguments("SqlServer")]
    [Arguments("MariaDb")]
    [Arguments("MySql")]
    public async Task InitialCreateAndUnrelatedMigrationsDoNotReadNonexistentOldTables(string provider)
    {
        using var context = CreateContext(provider);
        var publication = PublicationTable(context);
        MigrationOperation[] initial =
        [
            new CreateTableOperation { Name = BindingTable(publication.Name), Schema = publication.Schema },
            publication
        ];
        MigrationOperation[] unrelated = [new SqlOperation { Sql = "SELECT 1;" }];
        await Assert.That(ReferenceEquals(Prepare(context, initial), initial)).IsTrue();
        await Assert.That(ReferenceEquals(Prepare(context, unrelated), unrelated)).IsTrue();
    }

    [Test]
    [Arguments("PostgreSql")]
    [Arguments("SqlServer")]
    [Arguments("MariaDb")]
    [Arguments("MySql")]
    public async Task DowngradeValidatesEveryPublicationAndAttemptBeforeRestoringLegacyConstraintsAndDroppingEvidence(string provider)
    {
        using var context = CreateContext(provider);
        var publication = PublicationTable(context);
        var sql = context.GetService<ISqlGenerationHelper>();
        var drop = new DropTableOperation { Name = publication.Name, Schema = publication.Schema };
        var restoreKey = new AddUniqueConstraintOperation
        {
            Name = "ak_restore_publication",
            Table = BindingTable(publication.Name),
            Schema = publication.Schema,
            Columns = ["tenant_id", "id", "published_mapping_revision_hash_key"]
        };
        var restoreForeignKey = new AddForeignKeyOperation
        {
            Name = "fk_restore_publication",
            Table = AttemptsTable(publication.Name),
            Schema = publication.Schema,
            Columns = ["tenant_id", "registration_provider_binding_id", "provider_mapping_revision_hash_key"],
            PrincipalTable = restoreKey.Table,
            PrincipalSchema = restoreKey.Schema,
            PrincipalColumns = restoreKey.Columns
        };
        var prepared = Prepare(context, [drop, restoreKey, restoreForeignKey]);
        string preflight = prepared.OfType<SqlOperation>().Single().Sql;
        await Assert.That(preflight).Contains($"FROM {sql.DelimitIdentifier(publication.Name, publication.Schema)} AS p");
        await Assert.That(preflight).Contains($"b.{sql.DelimitIdentifier("published_at")} = p.{sql.DelimitIdentifier("published_at")}");
        await Assert.That(preflight).Contains($"p.{sql.DelimitIdentifier("mapping_revision_hash_key")}");
        await Assert.That(prepared.ToList().IndexOf(drop) > prepared.ToList().IndexOf(restoreForeignKey)).IsTrue();
        string script = Generate(context, prepared);
        int restoration = script.IndexOf("fk_restore_publication", StringComparison.Ordinal);
        int removal = script.IndexOf($"DROP TABLE {sql.DelimitIdentifier(publication.Name, publication.Schema)}", StringComparison.Ordinal);
        await Assert.That(restoration >= 0 && removal > restoration).IsTrue();
    }

    [Test]
    [Arguments("PostgreSql")]
    [Arguments("Sqlite")]
    [Arguments("SqlServer")]
    [Arguments("MySql")]
    public async Task Ordinal_only_hash_metadata_changes_preserve_transfer_evidence(string provider)
    {
        using var context = CreateContext(provider);
        var publication = PublicationTable(context);
        var alteration = new AlterColumnOperation
        {
            Name = "published_mapping_revision_hash",
            Table = BindingTable(publication.Name),
            Schema = publication.Schema,
            ClrType = typeof(string),
            ColumnType = "TEXT",
            MaxLength = 44,
            IsNullable = true,
            Collation = provider switch { "PostgreSql" => "C", "SqlServer" => "Latin1_General_100_BIN2", "MySql" => "ascii_bin", _ => "BINARY" },
            OldColumn = new AddColumnOperation
            {
                ClrType = typeof(string),
                ColumnType = "TEXT",
                MaxLength = 44,
                IsNullable = true
            }
        };
        var prepared = Prepare(context, [alteration, publication]);
        await Assert.That(prepared.Contains(alteration)).IsTrue();
        await Assert.That(prepared.OfType<SqlOperation>().Any(operation =>
            operation.Sql.StartsWith("INSERT INTO", StringComparison.Ordinal)
            && operation.Sql.Contains("SELECT b.", StringComparison.Ordinal))).IsTrue();
        alteration.OldColumn.IsNullable = false;
        await Assert.That(() => Prepare(context, [alteration, publication])).Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Canonical_hash_charset_transition_is_exactly_ascii_and_rejects_other_annotations(bool reverse)
    {
        using var context = CreateContext("MySql");
        var publication = PublicationTable(context);
        var alteration = new AlterColumnOperation
        {
            Name = "published_mapping_revision_hash",
            Table = BindingTable(publication.Name),
            Schema = publication.Schema,
            ClrType = typeof(string),
            ColumnType = "varchar(44)",
            MaxLength = 44,
            IsNullable = true,
            Collation = reverse ? null : "ascii_bin",
            OldColumn = new AddColumnOperation
            {
                ClrType = typeof(string),
                ColumnType = "varchar(44)",
                MaxLength = 44,
                IsNullable = true,
                Collation = reverse ? "ascii_bin" : null
            }
        };
        alteration["MySql:CharSet"] = reverse ? "utf8mb4" : "ascii";
        alteration.OldColumn["MySql:CharSet"] = reverse ? "ascii" : "utf8mb4";
        await Assert.That(Prepare(context, [alteration, publication]).Contains(alteration)).IsTrue();
        alteration["MySql:CharSet"] = "latin1";
        await Assert.That(() => Prepare(context, [alteration, publication])).Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments("PostgreSql", false)]
    [Arguments("PostgreSql", true)]
    [Arguments("SqlServer", false)]
    [Arguments("SqlServer", true)]
    public async Task Computed_attempt_hash_dependency_is_rebuilt_around_source_collation(string provider, bool reverse)
    {
        using var context = CreateContext(provider);
        var sql = context.GetService<ISqlGenerationHelper>();
        var assembly = provider == "PostgreSql" ? typeof(ExploreDbContext).Assembly
            : System.Reflection.Assembly.Load("Explore.Persistence.Migrations.SqlServer");
        Migration ReadMigration(string suffix)
        {
            Type type = assembly.GetTypes().Single(candidate =>
                candidate.GetCustomAttribute<MigrationAttribute>()?.Id.EndsWith("_" + suffix, StringComparison.Ordinal) == true);
            var migration = (Migration)Activator.CreateInstance(type)!;
            migration.ActiveProvider = context.Database.ProviderName!;
            return migration;
        }
        var migration = ReadMigration("ImmutableRegistrationProviderBindingPublication");
        var targetMigration = reverse
            ? ReadMigration(provider == "PostgreSql" ? "AddNativeModerationSourceOwners" : "PreservePrivateBirthDatePiiCascade")
            : migration;
        var model = context.GetService<IModelRuntimeInitializer>().Initialize(targetMigration.TargetModel, designTime: true);
        var operations = reverse ? migration.DownOperations : migration.UpOperations;
        var source = operations.OfType<AlterColumnOperation>().Single(column => column.Name == "provider_mapping_revision_hash");
        var computed = operations.OfType<AlterColumnOperation>().Single(column => column.Name == "provider_mapping_revision_hash_key");
        var prepared = RegistrationProviderPublicationTransfer.Prepare(operations, sql, context.Database.ProviderName!, model: model);
        int sourcePosition = prepared.ToList().IndexOf(source);
        var removed = prepared.OfType<DropColumnOperation>().Single();
        var recreated = prepared.OfType<AddColumnOperation>().Single();
        await Assert.That(removed.Name).IsEqualTo(computed.Name);
        await Assert.That(prepared.ToList().IndexOf(removed) < sourcePosition).IsTrue();
        await Assert.That(prepared.ToList().IndexOf(recreated) > sourcePosition).IsTrue();
        await Assert.That(recreated.ComputedColumnSql).IsEqualTo(computed.ComputedColumnSql);
        await Assert.That(recreated.Collation).IsEqualTo(computed.Collation);
        await Assert.That(prepared.OfType<DropIndexOperation>().Any()).IsTrue();
        await Assert.That(prepared.OfType<CreateIndexOperation>().Any(index => index.Columns.Contains(computed.Name))).IsTrue();
        if (provider == "SqlServer")
        {
            var removedCheck = prepared.OfType<DropCheckConstraintOperation>()
                .Single(check => check.Name == "ck_registration_attempts_provider_pair");
            var restoredCheck = prepared.OfType<AddCheckConstraintOperation>()
                .Single(check => check.Name == removedCheck.Name);
            await Assert.That(prepared.ToList().IndexOf(removedCheck) < sourcePosition).IsTrue();
            await Assert.That(prepared.ToList().IndexOf(restoredCheck) > prepared.ToList().IndexOf(recreated)).IsTrue();
            await Assert.That(restoredCheck.Sql).Contains(source.Name);
        }
        // Fresh native operations exercise generator/model handoff, not prepared-annotation reuse.
        var freshMigration = ReadMigration("ImmutableRegistrationProviderBindingPublication");
        string script = string.Join('\n', context.GetService<IMigrationsSqlGenerator>().Generate(
            reverse ? freshMigration.DownOperations : freshMigration.UpOperations, model).Select(command => command.CommandText));
        int dropColumn = script.IndexOf($"DROP COLUMN {sql.DelimitIdentifier(computed.Name)}", StringComparison.Ordinal);
        int alterSource = script.IndexOf($"ALTER COLUMN {sql.DelimitIdentifier(source.Name)}", StringComparison.Ordinal);
        int addColumn = script.IndexOf($"ADD {sql.DelimitIdentifier(computed.Name)}", StringComparison.Ordinal);
        await Assert.That(dropColumn >= 0 && alterSource > dropColumn && addColumn > alterSource).IsTrue();
        computed.ComputedColumnSql = computed.OldColumn.ComputedColumnSql = "'replacement'";
        await Assert.That(() => RegistrationProviderPublicationTransfer.Prepare(
            operations, sql, context.Database.ProviderName!, model: model)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task SqliteDowngradeRefusesToDropEvidenceBeforeDeferredNativeRebuilds()
    {
        using var context = CreateContext("Sqlite");
        var publication = PublicationTable(context);
        var restoreKey = new AddUniqueConstraintOperation
        {
            Name = "ak_restore_publication",
            Table = BindingTable(publication.Name),
            Schema = publication.Schema,
            Columns = ["tenant_id", "id", "published_mapping_revision_hash_key"]
        };
        MigrationOperation[] inverse =
        [
            new DropTableOperation { Name = publication.Name, Schema = publication.Schema }, restoreKey,
            new AddForeignKeyOperation
            {
                Name = "fk_restore_publication", Table = AttemptsTable(publication.Name), Schema = publication.Schema,
                Columns = ["tenant_id", "registration_provider_binding_id", "provider_mapping_revision_hash_key"],
                PrincipalTable = restoreKey.Table, PrincipalSchema = restoreKey.Schema, PrincipalColumns = restoreKey.Columns
            }
        ];
        await Assert.That(() => Prepare(context, inverse)).Throws<InvalidOperationException>();
        await Assert.That(RegistrationProviderPublicationTransfer.Prepare(inverse,
            context.GetService<ISqlGenerationHelper>(), context.Database.ProviderName!, sqlitePostRebuildDrop: true).Count)
            .IsGreaterThan(inverse.Length);
    }

    [Test]
    public async Task Sqlite_downgrade_restores_the_legacy_parent_key_before_copying_attempts()
    {
        using var context = CreateContext("Sqlite");
        var assembly = System.Reflection.Assembly.Load("Explore.Persistence.Migrations.Sqlite");
        Migration ReadMigration(string suffix)
        {
            Type type = assembly.GetTypes().Single(candidate =>
                candidate.GetCustomAttribute<MigrationAttribute>()?.Id.EndsWith("_" + suffix, StringComparison.Ordinal) == true);
            var migration = (Migration)Activator.CreateInstance(type)!;
            migration.ActiveProvider = context.Database.ProviderName!;
            return migration;
        }
        var migration = ReadMigration("ImmutableRegistrationProviderBindingPublication");
        var model = context.GetService<IModelRuntimeInitializer>().Initialize(
            ReadMigration("AddNativeModerationSourceOwners").TargetModel, designTime: true);
        string script = string.Join('\n', context.GetService<IMigrationsSqlGenerator>()
            .Generate(migration.DownOperations, model).Select(command => command.CommandText));
        var identifiers = context.GetService<ISqlGenerationHelper>();
        string publication = migration.DownOperations.OfType<DropTableOperation>()
            .Single(table => table.Name.EndsWith("registration_provider_binding_publications", StringComparison.Ordinal)).Name;
        string prefix = publication[..^"registration_provider_binding_publications".Length];
        string binding = prefix + "registration_provider_bindings";
        int validation = script.IndexOf($"INSERT INTO {identifiers.DelimitIdentifier(prefix + "registration_publication_transfer_guard")}", StringComparison.Ordinal);
        int bridge = script.IndexOf($"CREATE UNIQUE INDEX {identifiers.DelimitIdentifier(prefix + "registration_publication_transfer_legacy_key")}", StringComparison.Ordinal);
        int copy = script.IndexOf($"INSERT INTO {identifiers.DelimitIdentifier("ef_temp_" + prefix + "registration_attempts")}", StringComparison.Ordinal);
        int replace = script.IndexOf($"DROP TABLE {identifiers.DelimitIdentifier(binding)}", StringComparison.Ordinal);
        int publicationDrop = script.IndexOf($"DROP TABLE {identifiers.DelimitIdentifier(publication)}", StringComparison.Ordinal);
        await Assert.That(validation >= 0 && bridge > validation && copy > bridge && replace > copy
            && publicationDrop > replace).IsTrue();
        await Assert.That(script).Contains($"ON {identifiers.DelimitIdentifier(binding)} ({identifiers.DelimitIdentifier("tenant_id")}, {identifiers.DelimitIdentifier("id")}, {identifiers.DelimitIdentifier("published_mapping_revision_hash_key")})");
    }

    [Test]
    [Arguments("reverse-foreign-key")]
    [Arguments("missing-column")]
    [Arguments("changed-evidence")]
    [Arguments("dropped-bindings")]
    [Arguments("new-attempt-table")]
    [Arguments("duplicate-publication-create")]
    [Arguments("missing-binding-uniqueness")]
    [Arguments("missing-exact-key")]
    [Arguments("nullable-publication-coordinate")]
    [Arguments("wrong-attempt-hash-column")]
    [Arguments("changed-attempt-evidence")]
    [Arguments("unknown-raw-sql")]
    public async Task UnknownOrEvidenceDestroyingUpgradeShapesFailClosed(string shape)
    {
        using var context = CreateContext("PostgreSql");
        var publication = PublicationTable(context);
        var operations = new List<MigrationOperation> { publication };
        switch (shape)
        {
            case "reverse-foreign-key":
                publication.ForeignKeys.Add(new AddForeignKeyOperation
                {
                    Table = publication.Name,
                    Name = "fk_cycle",
                    Columns = ["tenant_id", "registration_provider_binding_id"],
                    PrincipalTable = BindingTable(publication.Name),
                    PrincipalColumns = ["tenant_id", "id"]
                });
                break;
            case "missing-column": publication.Columns.RemoveAt(0); break;
            case "changed-evidence":
                operations.Add(new AlterColumnOperation
                {
                    Table = BindingTable(publication.Name),
                    Schema = publication.Schema,
                    Name = "published_at",
                    ClrType = typeof(DateTime),
                    IsNullable = true
                });
                break;
            case "dropped-bindings":
                operations.Add(new DropTableOperation { Name = BindingTable(publication.Name), Schema = publication.Schema });
                break;
            case "new-attempt-table":
                operations.Add(new CreateTableOperation { Name = AttemptsTable(publication.Name), Schema = publication.Schema });
                break;
            case "duplicate-publication-create": operations.Add(PublicationTable(context)); break;
            case "missing-binding-uniqueness": publication.UniqueConstraints.RemoveAt(2); break;
            case "missing-exact-key": publication.UniqueConstraints.RemoveAt(1); break;
            case "nullable-publication-coordinate": publication.Columns[4].IsNullable = true; break;
            case "wrong-attempt-hash-column":
                var foreignKey = PublicationForeignKey(publication, AttemptsTable(publication.Name), binding: false);
                foreignKey.Columns[2] = "provider_mapping_revision_hash";
                operations.Add(foreignKey);
                break;
            case "changed-attempt-evidence":
                operations.Add(new DropColumnOperation
                {
                    Table = AttemptsTable(publication.Name),
                    Schema = publication.Schema,
                    Name = "provider_mapping_revision_hash_key"
                });
                break;
            case "unknown-raw-sql":
                operations.Add(new SqlOperation { Sql = $"DELETE FROM {BindingTable(publication.Name)};" });
                break;
        }
        await Assert.That(() => Prepare(context, operations)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task UnsupportedProviderAndIncompleteDowngradeFailClosed()
    {
        using var context = CreateContext("PostgreSql");
        var publication = PublicationTable(context);
        await Assert.That(() => RegistrationProviderPublicationTransfer.Prepare(
            [publication], context.GetService<ISqlGenerationHelper>(), "Unknown.Provider"))
            .Throws<InvalidOperationException>();
        await Assert.That(() => Prepare(context,
            [new DropTableOperation { Name = publication.Name, Schema = publication.Schema }]))
            .Throws<InvalidOperationException>();
    }

    private static ExploreDbContext CreateContext(string provider)
    {
        var context = ExploreDbContextModelProviderTests.CreateContext(provider, "transfer_schema");
        if (provider != "MySql")
            return context;
        // Match the supported MySQL 8.4 default, not the older model-only fixture's 8.0 capabilities.
        var builder = TestDbContextOptions.Create((DbContextOptions<ExploreDbContext>)context.GetService<IDbContextOptions>());
        builder.UseMySql(context.Database.GetConnectionString()!, new MySqlServerVersion(new Version(8, 4)));
        context.Dispose();
        return new ExploreDbContext(builder.Options);
    }

    private static IReadOnlyList<MigrationOperation> Prepare(ExploreDbContext context, IReadOnlyList<MigrationOperation> operations) =>
        RegistrationProviderPublicationTransfer.Prepare(operations, context.GetService<ISqlGenerationHelper>(), context.Database.ProviderName!,
            model: context.GetService<IDesignTimeModel>().Model);

    private static string Generate(ExploreDbContext context, IReadOnlyList<MigrationOperation> operations) =>
        string.Join('\n', context.GetService<IMigrationsSqlGenerator>().Generate(
            operations, context.GetService<IDesignTimeModel>().Model).Select(command => command.CommandText));

    private static CreateTableOperation PublicationTable(ExploreDbContext context)
    {
        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(RegistrationProviderBindingPublication))!;
        var table = new CreateTableOperation { Name = entity.GetTableName()!, Schema = entity.GetSchema() };
        foreach (var (name, type) in new[]
        {
            ("id", typeof(Guid)), ("tenant_id", typeof(Guid)), ("registration_provider_binding_id", typeof(Guid)),
            ("mapping_revision_hash_key", typeof(string)), ("published_at", typeof(DateTime))
        })
            table.Columns.Add(new AddColumnOperation
            {
                Name = name,
                Table = table.Name,
                Schema = table.Schema,
                ClrType = type,
                MaxLength = type == typeof(string) ? 44 : null
            });
        table.PrimaryKey = new AddPrimaryKeyOperation { Name = "pk_transfer_publication", Table = table.Name, Schema = table.Schema, Columns = ["id"] };
        table.UniqueConstraints.Add(new AddUniqueConstraintOperation
        {
            Name = "ak_transfer_publication",
            Table = table.Name,
            Schema = table.Schema,
            Columns = ["tenant_id", "registration_provider_binding_id", "mapping_revision_hash_key"]
        });
        table.UniqueConstraints.Add(new AddUniqueConstraintOperation
        {
            Name = "ak_transfer_publication_time",
            Table = table.Name,
            Schema = table.Schema,
            Columns = ["tenant_id", "registration_provider_binding_id", "mapping_revision_hash_key", "published_at"]
        });
        table.UniqueConstraints.Add(new AddUniqueConstraintOperation
        {
            Name = "ak_transfer_publication_binding",
            Table = table.Name,
            Schema = table.Schema,
            Columns = ["tenant_id", "registration_provider_binding_id"]
        });
        return table;
    }

    private static AddForeignKeyOperation PublicationForeignKey(CreateTableOperation publication, string table, bool binding) => new()
    {
        Name = binding ? "fk_binding_publication" : "fk_attempt_publication",
        Table = table,
        Schema = publication.Schema,
        Columns = binding ? ["tenant_id", "id", "published_mapping_revision_hash_key", "published_at"]
            : ["tenant_id", "registration_provider_binding_id", "provider_mapping_revision_hash_key"],
        PrincipalTable = publication.Name,
        PrincipalSchema = publication.Schema,
        PrincipalColumns = binding ? ["tenant_id", "registration_provider_binding_id", "mapping_revision_hash_key", "published_at"]
            : ["tenant_id", "registration_provider_binding_id", "mapping_revision_hash_key"]
    };

    private static string BindingTable(string publication) => publication[..^"registration_provider_binding_publications".Length] + "registration_provider_bindings";
    private static string AttemptsTable(string publication) => publication[..^"registration_provider_binding_publications".Length] + "registration_attempts";
}
