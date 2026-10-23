using System.Globalization;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Schema;

/// <summary>Preserves existing publication coordinates when their immutable principal is introduced.</summary>
/// <remarks>
/// Requires an exclusive migration window without application writers. Nontransactional DDL providers
/// can retain the integer-only guard table after rejected preflight; operator cleanup must not bypass validation.
/// SQLite's calling generator must defer publication removal until after its native owner-table rebuilds.
/// </remarks>
internal static class RegistrationProviderPublicationTransfer
{
    private const string PublicationSuffix = "registration_provider_binding_publications";
    private const string PreparedAnnotation = "Explore:RegistrationPublicationTransferProvider";
    internal const string GuardDropAnnotation = "Explore:RegistrationPublicationTransferGuardDrop";
    private static readonly string[] AttemptColumns = ["tenant_id", "registration_provider_binding_id", "provider_mapping_revision_hash_key"];
    private static readonly string[] LegacyKeyColumns = ["tenant_id", "id", "published_mapping_revision_hash_key"];
    private static readonly string[] PublicationKeyColumns = ["tenant_id", "registration_provider_binding_id", "mapping_revision_hash_key"];

    internal static IReadOnlyList<MigrationOperation> Prepare(
        IReadOnlyList<MigrationOperation> operations,
        ISqlGenerationHelper sql,
        string providerName,
        bool sqlitePostRebuildDrop = false,
        IModel? model = null)
    {
        var preparedProvider = operations.OfType<SqlOperation>()
            .Select(operation => operation[PreparedAnnotation]).FirstOrDefault(value => value is not null);
        if (preparedProvider is not null)
        {
            if (!Equals(preparedProvider, providerName))
                throw UnsafeShape("Prepared publication SQL cannot be reused for another provider.");
            return operations;
        }

        var transitions = operations.Where(operation => operation switch
        {
            CreateTableOperation create => IsPublication(create.Name),
            DropTableOperation drop => IsPublication(drop.Name),
            _ => false
        }).ToArray();
        if (transitions.Length == 0)
            return operations;
        if (transitions.Length != 1)
            throw UnsafeShape("Exactly one publication-table transition is supported per migration.");

        var transition = transitions[0];
        var (publication, schema) = transition is CreateTableOperation creation
            ? (creation.Name, creation.Schema)
            : (((DropTableOperation)transition).Name, ((DropTableOperation)transition).Schema);
        string prefix = publication[..^PublicationSuffix.Length];
        string binding = prefix + "registration_provider_bindings";
        string attempts = prefix + "registration_attempts";
        if (transition is CreateTableOperation && operations.OfType<CreateTableOperation>()
            .Any(operation => operation.Name == binding && operation.Schema == schema))
            return operations; // Initial creation has no historical bindings to transfer.

        string provider = providerName switch
        {
            "Npgsql.EntityFrameworkCore.PostgreSQL" => "PostgreSql",
            "Microsoft.EntityFrameworkCore.Sqlite" => "Sqlite",
            "Microsoft.EntityFrameworkCore.SqlServer" => "SqlServer",
            "Microting.EntityFrameworkCore.MySql" => "MySql",
            _ => throw UnsafeShape("Unsupported publication-transfer SQL provider.")
        };
        if (schema is not null && (provider is "Sqlite" or "MySql"))
            throw UnsafeShape("This provider requires prefixed tables, not schemas.");
        ValidateRetainedEvidence(operations, binding, attempts, schema, provider);

        bool upgrade = transition is CreateTableOperation;
        if (transition is CreateTableOperation create)
            ValidatePublication(create, operations, binding, attempts);
        else
        {
            if (provider == "Sqlite" && !sqlitePostRebuildDrop)
                throw UnsafeShape("SQLite publication downgrade requires a generator-owned post-rebuild drop boundary.");
            ValidateInverse(operations, binding, attempts, schema);
        }

        operations = StageComputedAttemptHash(operations, attempts, schema, provider, model);

        string Identifier(string name) => sql.DelimitIdentifier(name);
        string Column(string alias, string name) => alias + "." + Identifier(name);
        string Table(string name) => sql.DelimitIdentifier(name, schema);
        string Equal(string left, string right) => provider switch
        {
            "PostgreSql" => $"({left} COLLATE \"C\" = {right} COLLATE \"C\")",
            "Sqlite" => $"(CAST({left} AS BLOB) = CAST({right} AS BLOB))",
            "SqlServer" => $"(CONVERT(varbinary(max), {left}) = CONVERT(varbinary(max), {right}) AND DATALENGTH({left}) = DATALENGTH({right}))",
            _ => $"(CAST({left} AS BINARY) = CAST({right} AS BINARY))"
        };
        string HashLength(string value) => provider switch
        {
            "Sqlite" => $"LENGTH(CAST({value} AS BLOB)) = 44",
            // SQL Server retains its native nvarchar representation; the sentinel counts trailing spaces too.
            "SqlServer" => $"LEN({value} + N'#') = 45",
            _ => $"OCTET_LENGTH({value}) = 44"
        };
        string ValidHash(string value)
        {
            const string pattern = "^[A-Za-z0-9+/]{42}[AEIMQUYcgkosw048]=$";
            string canonical = provider switch
            {
                "PostgreSql" => $"{value} COLLATE \"C\" ~ '{pattern}'",
                "Sqlite" => $"SUBSTR({value}, 1, 42) NOT GLOB '*[^A-Za-z0-9+/]*' "
                    + $"AND SUBSTR({value}, 43, 1) GLOB '[AEIMQUYcgkosw048]' AND SUBSTR({value}, 44, 1) = '='",
                "SqlServer" => $"LEFT({value}, 42) COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Za-z0-9+/]%' "
                    + $"AND SUBSTRING({value}, 43, 1) COLLATE Latin1_General_100_BIN2 LIKE '[AEIMQUYcgkosw048]' "
                    + $"AND RIGHT({value}, 1) COLLATE Latin1_General_100_BIN2 = '='",
                _ => $"CONVERT({value} USING utf8mb4) COLLATE utf8mb4_bin REGEXP '{pattern}'"
            };
            return $"({HashLength(value)} AND {canonical})";
        }
        string published = ((int)RegistrationProviderBindingStateEnum.Published).ToString(CultureInfo.InvariantCulture);
        string unpublished = string.Join(", ", Enum.GetValues<RegistrationProviderBindingStateEnum>()
            .Where(state => state != RegistrationProviderBindingStateEnum.Published)
            .Select(state => ((int)state).ToString(CultureInfo.InvariantCulture)));
        string state = Column("b", "state_id");
        string hash = Column("b", "published_mapping_revision_hash");
        string key = Column("b", "published_mapping_revision_hash_key");
        string time = Column("b", "published_at");
        string validPublished = $"({state} = {published} AND {hash} IS NOT NULL AND {key} IS NOT NULL "
            + $"AND {time} IS NOT NULL AND {ValidHash(hash)} AND {Equal(hash, key)})";
        string validUnpublished = $"({state} IN ({unpublished}) AND {hash} IS NULL AND {time} IS NULL "
            + $"AND {key} IS NOT NULL AND {Equal(key, "''")})";
        string invalidBinding = $"EXISTS (SELECT 1 FROM {Table(binding)} AS b "
            + $"WHERE CASE WHEN {validPublished} OR {validUnpublished} THEN 0 ELSE 1 END = 1)";
        string attemptBinding = Column("a", "registration_provider_binding_id");
        string attemptHash = Column("a", "provider_mapping_revision_hash");
        string attemptKey = Column("a", "provider_mapping_revision_hash_key");
        string bindingMatch = $"{Column("b", "tenant_id")} = {Column("a", "tenant_id")} "
            + $"AND {Column("b", "id")} = {attemptBinding} AND {validPublished} AND {Equal(key, attemptKey)}";
        string publicationMatch = $"{Column("p", "tenant_id")} = {Column("a", "tenant_id")} "
            + $"AND {Column("p", "registration_provider_binding_id")} = {attemptBinding} "
            + $"AND {Equal(Column("p", "mapping_revision_hash_key"), attemptKey)}";
        string validAttempt = $"({attemptBinding} IS NULL AND {attemptHash} IS NULL AND {attemptKey} IS NOT NULL AND {Equal(attemptKey, "''")}) "
            + $"OR ({attemptBinding} IS NOT NULL AND {attemptHash} IS NOT NULL AND {attemptKey} IS NOT NULL "
            + $"AND {ValidHash(attemptHash)} AND {Equal(attemptHash, attemptKey)} "
            + $"AND EXISTS (SELECT 1 FROM {Table(binding)} AS b WHERE {bindingMatch})"
            + (upgrade ? ")" : $" AND EXISTS (SELECT 1 FROM {Table(publication)} AS p WHERE {publicationMatch}))");
        string invalidAttempt = $"EXISTS (SELECT 1 FROM {Table(attempts)} AS a "
            + $"WHERE CASE WHEN {validAttempt} THEN 0 ELSE 1 END = 1)";
        string invalid = $"{invalidBinding} OR {invalidAttempt}";
        if (!upgrade)
        {
            string exactPublication = $"{Column("b", "tenant_id")} = {Column("p", "tenant_id")} "
                + $"AND {Column("b", "id")} = {Column("p", "registration_provider_binding_id")} "
                + $"AND {time} = {Column("p", "published_at")} AND {Equal(key, Column("p", "mapping_revision_hash_key"))} AND {validPublished}";
            invalid += $" OR EXISTS (SELECT 1 FROM {Table(publication)} AS p "
                + $"WHERE NOT EXISTS (SELECT 1 FROM {Table(binding)} AS b WHERE {exactPublication}))"
                + $" OR EXISTS (SELECT 1 FROM {Table(binding)} AS b WHERE {state} = {published} "
                + $"AND NOT EXISTS (SELECT 1 FROM {Table(publication)} AS p WHERE {exactPublication}))";
        }

        string guardName = prefix + "registration_publication_transfer_guard";
        var guard = new CreateTableOperation { Name = guardName, Schema = schema };
        guard.Columns.Add(new AddColumnOperation
        {
            Name = "violation",
            Table = guardName,
            Schema = schema,
            ClrType = typeof(int),
            IsNullable = false
        });
        guard.CheckConstraints.Add(new AddCheckConstraintOperation
        {
            Name = "ck_registration_publication_transfer",
            Table = guardName,
            Schema = schema,
            Sql = $"{Identifier("violation")} = 0"
        });
        // A dedicated integer-only CHECK avoids subqueries in CHECK expressions and row-value error disclosure.
        var preflight = new SqlOperation
        {
            Sql = $"INSERT INTO {Table(guardName)} ({Identifier("violation")}) SELECT 1 WHERE {invalid};",
            SuppressTransaction = false
        };
        preflight[PreparedAnnotation] = providerName;
        var dropGuard = new DropTableOperation { Name = guardName, Schema = schema };
        dropGuard[GuardDropAnnotation] = true;
        var prepared = new List<MigrationOperation>(operations.Count + 4)
        {
            guard, preflight, dropGuard
        };
        if (!upgrade && provider == "Sqlite")
        {
            // Native attempt copies precede the binding rebuild that restores its legacy alternate key.
            prepared.Add(new SqlOperation
            {
                Sql = $"CREATE UNIQUE INDEX {Identifier(prefix + "registration_publication_transfer_legacy_key")} "
                    + $"ON {Table(binding)} ({string.Join(", ", LegacyKeyColumns.Select(Identifier))});",
                SuppressTransaction = false
            });
        }
        if (!upgrade)
        {
            prepared.AddRange(operations.Where(operation => !ReferenceEquals(operation, transition)));
            prepared.Add(transition);
            return prepared;
        }

        var deferredConstraints = operations.Where(operation => operation is AddCheckConstraintOperation check
                && check.Table == binding && check.Schema == schema
            || operation is AddForeignKeyOperation foreignKey
                && foreignKey.PrincipalTable == publication && foreignKey.PrincipalSchema == schema).ToArray();
        foreach (var operation in operations)
        {
            if (deferredConstraints.Contains(operation))
                continue;
            prepared.Add(operation);
            if (ReferenceEquals(operation, transition))
                prepared.Add(new SqlOperation
                {
                    Sql = $"INSERT INTO {Table(publication)} ({Identifier("id")}, {Identifier("tenant_id")}, "
                        + $"{Identifier("registration_provider_binding_id")}, {Identifier("mapping_revision_hash_key")}, {Identifier("published_at")})\n"
                        + $"SELECT {Column("b", "id")}, {Column("b", "tenant_id")}, {Column("b", "id")}, {key}, {time}\n"
                        + $"FROM {Table(binding)} AS b WHERE {state} = {published};",
                    SuppressTransaction = false
                });
        }
        prepared.AddRange(deferredConstraints);
        return prepared;
    }

    private static IReadOnlyList<MigrationOperation> StageComputedAttemptHash(
        IReadOnlyList<MigrationOperation> operations, string attempts, string? schema, string provider, IModel? model)
    {
        if (provider is not ("PostgreSql" or "SqlServer")) return operations;
        var alterations = operations.OfType<AlterColumnOperation>()
            .Where(column => column.Table == attempts && column.Schema == schema).ToArray();
        var source = alterations.SingleOrDefault(column => column.Name == "provider_mapping_revision_hash");
        if (source is null) return operations;
        var computed = alterations.SingleOrDefault(column => column.Name == "provider_mapping_revision_hash_key");
        if (computed is null || computed.ComputedColumnSql != "COALESCE(provider_mapping_revision_hash, '')"
            || computed.OldColumn.ComputedColumnSql != computed.ComputedColumnSql
            || computed.IsStored != true || computed.OldColumn.IsStored != true
            || operations.ToList().IndexOf(computed) <= operations.ToList().IndexOf(source))
            throw UnsafeShape("Attempt hash collation requires its original stored computed key and ordered recreation.");
        var table = model?.GetRelationalModel().FindTable(attempts, schema)
            ?? throw UnsafeShape("Attempt hash recreation requires the exact migration target model.");
        if (table.UniqueConstraints.Any(key => key.Columns.Any(column => column.Name == computed.Name)))
            throw UnsafeShape("Attempt hash recreation cannot replace an unplanned alternate key.");
        var indexes = table.Indexes.Where(index => index.Columns.Any(column => column.Name == computed.Name))
            .Select(CreateIndexOperation.CreateFrom).ToArray();
        if (indexes.Length == 0)
            throw UnsafeShape("Attempt hash recreation requires its native reference index.");
        var checks = provider == "SqlServer"
            ? table.CheckConstraints.Where(check => check.Sql.Contains(source.Name, StringComparison.OrdinalIgnoreCase)
                    || check.Sql.Contains(computed.Name, StringComparison.OrdinalIgnoreCase))
                .Select(check => new AddCheckConstraintOperation
                {
                    Name = check.Name!,
                    Table = attempts,
                    Schema = schema,
                    Sql = check.Sql
                }).ToArray()
            : [];
        if (provider == "SqlServer" && (checks.Length != 1 || checks[0].Name != "ck_registration_attempts_provider_pair"
            || operations.OfType<DropCheckConstraintOperation>().Any(check => check.Table == attempts
                && check.Schema == schema && checks.Any(original => original.Name == check.Name))))
            throw UnsafeShape("Attempt hash collation requires its unchanged native provider-pair check.");
        var recreated = new AddColumnOperation
        {
            Name = computed.Name,
            Table = attempts,
            Schema = schema,
            ClrType = computed.ClrType,
            ColumnType = computed.ColumnType,
            IsNullable = computed.IsNullable,
            MaxLength = computed.MaxLength,
            IsUnicode = computed.IsUnicode,
            IsFixedLength = computed.IsFixedLength,
            Precision = computed.Precision,
            Scale = computed.Scale,
            Collation = computed.Collation,
            ComputedColumnSql = computed.ComputedColumnSql,
            IsStored = computed.IsStored,
            Comment = computed.Comment
        };
        foreach (var annotation in computed.GetAnnotations()) recreated.AddAnnotation(annotation.Name, annotation.Value);
        var staged = new List<MigrationOperation>(operations.Count + indexes.Length * 2 + 1);
        foreach (var operation in operations)
        {
            if (ReferenceEquals(operation, source))
            {
                // Both engines retain the computed dependency; preflight has already validated its source evidence.
                staged.AddRange(checks.Select(check => new DropCheckConstraintOperation
                {
                    Name = check.Name,
                    Table = attempts,
                    Schema = schema
                }));
                staged.AddRange(indexes.Select(index => new DropIndexOperation
                {
                    Name = index.Name,
                    Table = attempts,
                    Schema = schema
                }));
                staged.Add(new DropColumnOperation { Name = computed.Name, Table = attempts, Schema = schema });
            }
            if (ReferenceEquals(operation, computed))
            {
                staged.Add(recreated);
                staged.AddRange(indexes);
                staged.AddRange(checks);
            }
            else staged.Add(operation);
        }
        return staged;
    }

    private static void ValidateRetainedEvidence(
        IReadOnlyList<MigrationOperation> operations, string binding, string attempts, string? schema, string provider)
    {
        bool IsOwner(string table, string? tableSchema) => tableSchema == schema && (table == binding || table == attempts);
        string[] evidenceColumns =
        [
            "id", "tenant_id", "state_id", "published_mapping_revision_hash", "published_mapping_revision_hash_key", "published_at",
            "registration_provider_binding_id", "provider_mapping_revision_hash", "provider_mapping_revision_hash_key"
        ];
        foreach (var operation in operations)
        {
            bool unsafeOperation = operation switch
            {
                DropTableOperation drop => IsOwner(drop.Name, drop.Schema),
                CreateTableOperation create => IsOwner(create.Name, create.Schema),
                RenameTableOperation rename => IsOwner(rename.Name, rename.Schema) || IsOwner(rename.NewName ?? rename.Name, rename.NewSchema),
                AlterColumnOperation alter when IsOwner(alter.Table, alter.Schema)
                    && evidenceColumns.Contains(alter.Name, StringComparer.Ordinal) => !IsCollationOnly(alter, provider),
                ColumnOperation column => IsOwner(column.Table, column.Schema) && evidenceColumns.Contains(column.Name, StringComparer.Ordinal),
                DropColumnOperation drop => IsOwner(drop.Table, drop.Schema) && evidenceColumns.Contains(drop.Name, StringComparer.Ordinal),
                RenameColumnOperation rename => IsOwner(rename.Table, rename.Schema)
                    && (evidenceColumns.Contains(rename.Name, StringComparer.Ordinal) || evidenceColumns.Contains(rename.NewName, StringComparer.Ordinal)),
                SqlOperation command => command.Sql.Contains(binding, StringComparison.OrdinalIgnoreCase)
                    || command.Sql.Contains(attempts, StringComparison.OrdinalIgnoreCase),
                _ => false
            };
            if (unsafeOperation)
                throw UnsafeShape("Publication transfer requires unchanged, existing binding and attempt evidence columns.");
        }
    }

    private static bool IsCollationOnly(AlterColumnOperation column, string provider)
    {
        var old = column.OldColumn;
        string ordinal = provider switch
        {
            "PostgreSql" => "C",
            "Sqlite" => "BINARY",
            "SqlServer" => "Latin1_General_100_BIN2",
            _ => "ascii_bin"
        };
        bool ordinalTransition = column.Collation == ordinal && old.Collation is null
            || old.Collation == ordinal && column.Collation is null;
        string[] hashes = ["published_mapping_revision_hash", "published_mapping_revision_hash_key", "provider_mapping_revision_hash", "provider_mapping_revision_hash_key"];
        return hashes.Contains(column.Name, StringComparer.Ordinal) && ordinalTransition
            && column.GetAnnotations().All(annotation => SameAnnotation(annotation.Name, annotation.Value, old[annotation.Name]))
            && old.GetAnnotations().All(annotation => SameAnnotation(annotation.Name, column[annotation.Name], annotation.Value))
            && column.ClrType == typeof(string) && old.ClrType == column.ClrType
            && column.MaxLength == 44 && old.MaxLength == column.MaxLength
            && column.ColumnType == old.ColumnType && column.IsNullable == old.IsNullable
            && column.IsUnicode == old.IsUnicode && column.IsFixedLength == old.IsFixedLength
            && column.Precision == old.Precision && column.Scale == old.Scale
            && Equals(column.DefaultValue, old.DefaultValue) && column.DefaultValueSql == old.DefaultValueSql
            && column.ComputedColumnSql == old.ComputedColumnSql && column.IsStored == old.IsStored
            && column.Comment == old.Comment;

        bool SameAnnotation(string name, object? currentValue, object? oldValue)
        {
            if (Equals(currentValue, oldValue)) return true;
            // Preflight proves all retained hashes are canonical ASCII before this exact native conversion.
            return provider == "MySql" && name == "MySql:CharSet"
                && (column.Collation == ordinal && (string?)currentValue == "ascii" && (string?)oldValue == "utf8mb4"
                    || old.Collation == ordinal && (string?)oldValue == "ascii" && (string?)currentValue == "utf8mb4");
        }
    }

    private static void ValidatePublication(
        CreateTableOperation create, IReadOnlyList<MigrationOperation> operations, string binding, string attempts)
    {
        (string Name, Type Type)[] columns =
        [
            ("id", typeof(Guid)), ("tenant_id", typeof(Guid)), ("registration_provider_binding_id", typeof(Guid)),
            ("mapping_revision_hash_key", typeof(string)), ("published_at", typeof(DateTime))
        ];
        if (create.Columns.Count != columns.Length || columns.Any(expected => !create.Columns.Any(column =>
                column.Name == expected.Name && column.ClrType == expected.Type && !column.IsNullable
                && column.ComputedColumnSql is null && column.DefaultValue is null && column.DefaultValueSql is null
                && (expected.Type != typeof(string) || column.MaxLength == 44)))
            || create.PrimaryKey?.Columns.SequenceEqual(["id"]) != true
            || !create.UniqueConstraints.Any(key => key.Columns.SequenceEqual(PublicationKeyColumns))
            || !create.UniqueConstraints.Any(key => key.Columns.SequenceEqual([.. PublicationKeyColumns, "published_at"])))
            throw UnsafeShape("Publication creation does not have the required immutable evidence columns and exact keys.");
        string[] uniqueBindingColumns = ["tenant_id", "registration_provider_binding_id"];
        if (!create.UniqueConstraints.Any(key => key.Columns.SequenceEqual(uniqueBindingColumns))
            && !operations.OfType<CreateIndexOperation>().Any(index => index.Table == create.Name && index.Schema == create.Schema
                && index.IsUnique && index.Filter is null && index.Columns.SequenceEqual(uniqueBindingColumns)))
            throw UnsafeShape("Publication creation requires unique tenant/binding ownership.");
        foreach (var foreignKey in operations.OfType<CreateTableOperation>().SelectMany(table => table.ForeignKeys)
                     .Concat(operations.OfType<AddForeignKeyOperation>()))
        {
            if (foreignKey.Table == create.Name && foreignKey.PrincipalTable == binding)
                throw UnsafeShape("A publication-to-binding foreign key would create an insertion cycle.");
            if (foreignKey.PrincipalTable != create.Name || foreignKey.PrincipalSchema != create.Schema)
                continue;
            bool bindingReference = foreignKey.Table == binding && foreignKey.Schema == create.Schema
                && foreignKey.Columns.SequenceEqual(["tenant_id", "id", "published_mapping_revision_hash_key", "published_at"])
                && foreignKey.PrincipalColumns?.SequenceEqual([.. PublicationKeyColumns, "published_at"]) == true;
            bool attemptReference = foreignKey.Table == attempts && foreignKey.Schema == create.Schema
                && foreignKey.Columns.SequenceEqual(AttemptColumns) && foreignKey.PrincipalColumns?.SequenceEqual(PublicationKeyColumns) == true;
            if (!bindingReference && !attemptReference)
                throw UnsafeShape("Publication foreign keys must retain exact tenant, binding, hash and publication-time coordinates.");
        }
    }

    private static void ValidateInverse(IReadOnlyList<MigrationOperation> operations, string binding, string attempts, string? schema)
    {
        var key = operations.OfType<AddUniqueConstraintOperation>().SingleOrDefault(operation =>
            operation.Table == binding && operation.Schema == schema && operation.Columns.SequenceEqual(LegacyKeyColumns));
        var foreignKey = operations.OfType<AddForeignKeyOperation>().SingleOrDefault(operation =>
            operation.Table == attempts && operation.Schema == schema && operation.Columns.SequenceEqual(AttemptColumns)
            && operation.PrincipalTable == binding && operation.PrincipalSchema == schema
            && operation.PrincipalColumns?.SequenceEqual(LegacyKeyColumns) == true);
        if (key is null || foreignKey is null || operations.ToList().IndexOf(key) > operations.ToList().IndexOf(foreignKey))
            throw UnsafeShape("Publication downgrade must restore the exact legacy binding key before the attempt foreign key.");
    }

    private static bool IsPublication(string table) => table.EndsWith(PublicationSuffix, StringComparison.Ordinal);
    private static InvalidOperationException UnsafeShape(string reason) => new(reason);
}
