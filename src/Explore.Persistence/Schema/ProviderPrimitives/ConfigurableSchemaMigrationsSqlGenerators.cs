using System.Collections;
using System.Reflection;
using Explore.Persistence.Database;
using Microting.EntityFrameworkCore.MySql.Infrastructure.Internal;
using Microting.EntityFrameworkCore.MySql.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Update;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.Internal;
using Npgsql.EntityFrameworkCore.PostgreSQL.Migrations;

namespace Explore.Persistence.Schema;

internal sealed class ConfigurableNpgsqlMigrationsSqlGenerator(
    MigrationsSqlGeneratorDependencies dependencies,
    INpgsqlSingletonOptions npgsqlOptions)
    : NpgsqlMigrationsSqlGenerator(dependencies, npgsqlOptions)
{
    public override IReadOnlyList<MigrationCommand> Generate(
        IReadOnlyList<MigrationOperation> operations,
        Microsoft.EntityFrameworkCore.Metadata.IModel? model = null,
        MigrationsSqlGenerationOptions options = MigrationsSqlGenerationOptions.Default)
    {
        operations = ConfigurableSchemaMigrationOperations.PrepareInstanceBootstrapLifecycleBackfill(
            operations,
            Dependencies.CurrentContext.Context);
        ConfigurableSchemaMigrationOperations.Rewrite(operations, Dependencies.CurrentContext.Context);
        return ConfigurableSchemaMigrationOperations.RewriteCommands(
            base.Generate(operations, model, options),
            Dependencies);
    }
}

internal sealed class ConfigurableSqlServerMigrationsSqlGenerator(
    MigrationsSqlGeneratorDependencies dependencies,
    ICommandBatchPreparer commandBatchPreparer)
    : SqlServerMigrationsSqlGenerator(dependencies, commandBatchPreparer)
{
    public override IReadOnlyList<MigrationCommand> Generate(
        IReadOnlyList<MigrationOperation> operations,
        Microsoft.EntityFrameworkCore.Metadata.IModel? model = null,
        MigrationsSqlGenerationOptions options = MigrationsSqlGenerationOptions.Default)
    {
        operations = ConfigurableSchemaMigrationOperations.PrepareInstanceBootstrapLifecycleBackfill(
            operations,
            Dependencies.CurrentContext.Context);
        ConfigurableSchemaMigrationOperations.Rewrite(operations, Dependencies.CurrentContext.Context);
        return ConfigurableSchemaMigrationOperations.RewriteCommands(
            base.Generate(operations, model, options),
            Dependencies);
    }
}

internal sealed class ConfigurableSqliteMigrationsSqlGenerator(
    MigrationsSqlGeneratorDependencies dependencies,
    IRelationalAnnotationProvider migrationsAnnotations)
    : SqliteMigrationsSqlGenerator(dependencies, migrationsAnnotations)
{
    public override IReadOnlyList<MigrationCommand> Generate(
        IReadOnlyList<MigrationOperation> operations,
        Microsoft.EntityFrameworkCore.Metadata.IModel? model = null,
        MigrationsSqlGenerationOptions options = MigrationsSqlGenerationOptions.Default)
    {
        IReadOnlyList<MigrationOperation> executableOperations =
            ConfigurableSchemaMigrationOperations.PrepareInstanceBootstrapLifecycleBackfill(
                operations,
                Dependencies.CurrentContext.Context);
        executableOperations =
            ConfigurableSchemaMigrationOperations.RemoveRedundantForeignKeyDrops(executableOperations);
        IReadOnlyList<MigrationCommand> commands =
            base.Generate(executableOperations, model, options);
        commands = ConfigurableSchemaMigrationOperations.WrapSqliteTableDrops(
            commands,
            executableOperations,
            Dependencies);
        return ConfigurableSchemaMigrationOperations.AppendPromotionCodeBackfill(
            commands,
            executableOperations,
            Dependencies);
    }
}

internal sealed class ConfigurableMySqlMigrationsSqlGenerator(
    MigrationsSqlGeneratorDependencies dependencies,
    ICommandBatchPreparer commandBatchPreparer,
    IMySqlOptions options)
    : MySqlMigrationsSqlGenerator(dependencies, commandBatchPreparer, options)
{
    public override IReadOnlyList<MigrationCommand> Generate(
        IReadOnlyList<MigrationOperation> operations,
        Microsoft.EntityFrameworkCore.Metadata.IModel? model = null,
        MigrationsSqlGenerationOptions sqlOptions = MigrationsSqlGenerationOptions.Default)
    {
        operations = ConfigurableSchemaMigrationOperations.PrepareInstanceBootstrapLifecycleBackfill(
            operations,
            Dependencies.CurrentContext.Context);
        return ConfigurableSchemaMigrationOperations.AppendPromotionCodeBackfill(
            base.Generate(operations, model, sqlOptions),
            operations,
            Dependencies);
    }
}

internal static class ConfigurableSchemaMigrationOperations
{
    private const string BootstrapBackfillMarker = "headless-instance-bootstrap-backfill";
    private const string BootstrapDowngradeBackfillMarker = "headless-instance-bootstrap-downgrade-backfill";

    public static IReadOnlyList<MigrationOperation> PrepareInstanceBootstrapLifecycleBackfill(
        IReadOnlyList<MigrationOperation> operations,
        Microsoft.EntityFrameworkCore.DbContext context)
    {
        operations = PrepareInstanceBootstrapLifecycleDowngradeBackfill(operations, context);
        DropColumnOperation[] legacyDrops = operations
            .OfType<DropColumnOperation>()
            .Where(IsLegacyBootstrapColumnDrop)
            .ToArray();
        if (legacyDrops.Length != 2
            || !operations.OfType<AddColumnOperation>().Any(IsBootstrapStatusColumn)
            || operations.OfType<SqlOperation>().Any(operation =>
                operation.Sql.Contains(BootstrapBackfillMarker, StringComparison.Ordinal)))
        {
            return operations;
        }

        var prepared = operations
            .Where(operation => operation is not DropColumnOperation drop
                || !IsLegacyBootstrapColumnDrop(drop))
            .ToList();
        int lastBootstrapColumn = prepared.FindLastIndex(operation =>
            operation is AddColumnOperation column && IsBootstrapLifecycleColumn(column));
        if (lastBootstrapColumn < 0)
        {
            return operations;
        }

        prepared.Insert(lastBootstrapColumn + 1, new SqlOperation
        {
            Sql = BuildInstanceBootstrapBackfillSql(context),
            SuppressTransaction = false,
        });
        prepared.InsertRange(lastBootstrapColumn + 2, legacyDrops);
        return prepared;
    }

    private static IReadOnlyList<MigrationOperation> PrepareInstanceBootstrapLifecycleDowngradeBackfill(
        IReadOnlyList<MigrationOperation> operations,
        Microsoft.EntityFrameworkCore.DbContext context)
    {
        AddColumnOperation[] legacyAdds = operations
            .OfType<AddColumnOperation>()
            .Where(IsLegacyBootstrapColumnAdd)
            .ToArray();
        DropColumnOperation[] lifecycleDrops = operations
            .OfType<DropColumnOperation>()
            .Where(IsBootstrapLifecycleColumn)
            .ToArray();
        if (legacyAdds.Length != 2
            || !lifecycleDrops.Any(operation => operation.Name == "status")
            || !lifecycleDrops.Any(operation => operation.Name == "deployment_mode")
            || operations.OfType<SqlOperation>().Any(operation =>
                operation.Sql.Contains(BootstrapDowngradeBackfillMarker, StringComparison.Ordinal)))
        {
            return operations;
        }

        int firstLifecycleDrop = operations
            .Select((operation, index) => (operation, index))
            .First(item => item.operation is DropColumnOperation drop
                && IsBootstrapLifecycleColumn(drop))
            .index;
        int insertionIndex = operations.Take(firstLifecycleDrop).Count(operation =>
            operation is not AddColumnOperation add || !IsLegacyBootstrapColumnAdd(add));
        var prepared = operations
            .Where(operation => operation is not AddColumnOperation add
                    || !IsLegacyBootstrapColumnAdd(add))
            .Where(operation => operation is not DropColumnOperation drop
                    || !IsBootstrapLifecycleColumn(drop))
            .ToList();
        prepared.InsertRange(insertionIndex, legacyAdds);
        prepared.Insert(insertionIndex + legacyAdds.Length, new SqlOperation
        {
            Sql = BuildInstanceBootstrapDowngradeBackfillSql(context),
            SuppressTransaction = false,
        });
        prepared.InsertRange(insertionIndex + legacyAdds.Length + 1, lifecycleDrops);
        return prepared;
    }

    public static IReadOnlyList<MigrationOperation> RemoveRedundantForeignKeyDrops(
        IReadOnlyList<MigrationOperation> operations)
    {
        HashSet<(string? Schema, string Table)> droppedTables = operations
            .OfType<DropTableOperation>()
            .Select(operation => (operation.Schema, operation.Name))
            .ToHashSet();
        if (droppedTables.Count == 0)
        {
            return operations;
        }

        return operations
            .Where(operation => operation is not DropForeignKeyOperation foreignKey
                || !droppedTables.Contains((foreignKey.Schema, foreignKey.Table)))
            .ToArray();
    }

    public static IReadOnlyList<MigrationCommand> WrapSqliteTableDrops(
        IReadOnlyList<MigrationCommand> commands,
        IReadOnlyList<MigrationOperation> operations,
        MigrationsSqlGeneratorDependencies dependencies)
    {
        if (!operations.Any(operation => operation is DropTableOperation))
        {
            return commands;
        }

        var wrapped = new List<MigrationCommand>(commands.Count + 2)
        {
            CreateSqlitePragmaCommand(
                "PRAGMA foreign_keys = OFF;",
                dependencies)
        };
        wrapped.AddRange(commands);
        wrapped.Add(CreateSqlitePragmaCommand(
            "PRAGMA foreign_keys = ON;",
            dependencies));
        return wrapped;
    }

    private static MigrationCommand CreateSqlitePragmaCommand(
        string sql,
        MigrationsSqlGeneratorDependencies dependencies)
    {
        var relationalCommand = dependencies.CommandBuilderFactory.Create()
            .Append(sql)
            .Build();
        return new MigrationCommand(
            relationalCommand,
            dependencies.CurrentContext.Context,
            dependencies.Logger,
            transactionSuppressed: true);
    }

    public static void Rewrite(IReadOnlyList<MigrationOperation> operations, Microsoft.EntityFrameworkCore.DbContext context)
    {
        var configuredSchema = GetConfiguredSchema(context);
        if (StringComparer.Ordinal.Equals(configuredSchema, RelationalModelNamespace.DefaultSchema))
        {
            return;
        }

        foreach (var operation in operations)
        {
            RewriteOperation(operation, configuredSchema);
        }
    }

    public static IReadOnlyList<MigrationCommand> RewriteCommands(
        IReadOnlyList<MigrationCommand> commands,
        MigrationsSqlGeneratorDependencies dependencies)
    {
        commands = AppendPromotionCodeBackfill(commands, [], dependencies);
        var configuredSchema = GetConfiguredSchema(dependencies.CurrentContext.Context);
        if (StringComparer.Ordinal.Equals(configuredSchema, RelationalModelNamespace.DefaultSchema))
        {
            return commands;
        }

        return commands.Select(command =>
        {
            var commandText = command.CommandText.Replace(
                RelationalModelNamespace.DefaultSchema,
                configuredSchema,
                StringComparison.Ordinal);
            var relationalCommand = dependencies.CommandBuilderFactory.Create()
                .Append(commandText)
                .Build();
            return new MigrationCommand(
                relationalCommand,
                dependencies.CurrentContext.Context,
                command.CommandLogger,
                command.TransactionSuppressed);
        }).ToArray();
    }

    public static IReadOnlyList<MigrationCommand> AppendPromotionCodeBackfill(
        IReadOnlyList<MigrationCommand> commands,
        IReadOnlyList<MigrationOperation> operations,
        MigrationsSqlGeneratorDependencies dependencies)
    {
        if (CommandTextContainsPromotionCodeBackfill(commands) ||
            (!AddsPromotionCodeSnapshotColumns(operations) && !CommandTextAddsPromotionCodeSnapshotColumns(commands)))
        {
            return commands;
        }

        if (commands.Count == 0)
        {
            return commands;
        }

        var backfill = BuildPromotionCodeBackfillSql(dependencies.CurrentContext.Context);
        var relationalCommand = dependencies.CommandBuilderFactory.Create()
            .Append(backfill)
            .Build();
        return commands
            .Concat([
                new MigrationCommand(
                    relationalCommand,
                    dependencies.CurrentContext.Context,
                    commands[^1].CommandLogger,
                    transactionSuppressed: false)
            ])
            .ToArray();
    }

    private static string GetConfiguredSchema(Microsoft.EntityFrameworkCore.DbContext context) =>
        context.GetService<IDbContextOptions>()
            .FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
        ?? RelationalModelNamespace.DefaultSchema;

    private static bool AddsPromotionCodeSnapshotColumns(IReadOnlyList<MigrationOperation> operations) =>
        operations.OfType<AddColumnOperation>().Any(operation =>
            StringComparer.Ordinal.Equals(operation.Table, "registration_orders") &&
            StringComparer.Ordinal.Equals(operation.Name, "pre_discount_organizer_directed_total_minor_snapshot")) &&
        operations.OfType<AddColumnOperation>().Any(operation =>
            StringComparer.Ordinal.Equals(operation.Table, "registration_order_lines") &&
            StringComparer.Ordinal.Equals(operation.Name, "pre_discount_line_subtotal_minor_snapshot"));

    private static bool CommandTextAddsPromotionCodeSnapshotColumns(IReadOnlyList<MigrationCommand> commands)
    {
        var text = string.Join('\n', commands.Select(command => command.CommandText));
        return text.Contains("pre_discount_organizer_directed_total_minor_snapshot", StringComparison.Ordinal) &&
               text.Contains("pre_discount_line_subtotal_minor_snapshot", StringComparison.Ordinal) &&
               !text.Contains("line_subtotal_snapshot\"", StringComparison.Ordinal) &&
               !text.Contains("line_subtotal_snapshot]", StringComparison.Ordinal) &&
               !text.Contains("line_subtotal_snapshot`", StringComparison.Ordinal);
    }

    private static bool CommandTextContainsPromotionCodeBackfill(IReadOnlyList<MigrationCommand> commands)
    {
        var text = string.Join('\n', commands.Select(command => command.CommandText));
        return text.Contains("organizer_directed_total_minor_snapshot", StringComparison.Ordinal) &&
               text.Contains("line_subtotal_snapshot", StringComparison.Ordinal);
    }

    private static string BuildPromotionCodeBackfillSql(Microsoft.EntityFrameworkCore.DbContext context)
    {
        var provider = context.Database.ProviderName ?? string.Empty;
        var prefix = UsesPrefixedTables(provider) ? RelationalModelNamespace.Prefix : string.Empty;
        var schema = UsesSchemas(provider) ? GetConfiguredSchema(context) : null;
        var quote = QuoteStyle(provider);
        var orders = Table(schema, prefix + "registration_orders", quote);
        var lines = Table(schema, prefix + "registration_order_lines", quote);

        return $"""
               UPDATE {orders}
               SET {Identifier("pre_discount_organizer_directed_total_minor_snapshot", quote)} = {Identifier("organizer_directed_total_minor_snapshot", quote)},
                   {Identifier("post_discount_organizer_directed_total_minor_snapshot", quote)} = {Identifier("organizer_directed_total_minor_snapshot", quote)};
               UPDATE {lines}
               SET {Identifier("pre_discount_line_subtotal_minor_snapshot", quote)} = {Identifier("line_subtotal_snapshot", quote)},
                   {Identifier("post_discount_line_subtotal_minor_snapshot", quote)} = {Identifier("line_subtotal_snapshot", quote)};
               """;
    }

    private static bool IsLegacyBootstrapColumnDrop(DropColumnOperation operation) =>
        IsInstanceBootstrapTable(operation.Table)
        && operation.Name is "is_completed" or "selected_deployment_mode";

    private static bool IsLegacyBootstrapColumnAdd(AddColumnOperation operation) =>
        IsInstanceBootstrapTable(operation.Table)
        && operation.Name is "is_completed" or "selected_deployment_mode";

    private static bool IsBootstrapStatusColumn(AddColumnOperation operation) =>
        IsInstanceBootstrapTable(operation.Table)
        && StringComparer.Ordinal.Equals(operation.Name, "status");

    private static bool IsBootstrapLifecycleColumn(AddColumnOperation operation) =>
        IsInstanceBootstrapTable(operation.Table)
        && operation.Name is
            "completed_identity_fingerprint"
            or "configuration_fingerprint"
            or "deployment_mode"
            or "generation"
            or "mode"
            or "provider_kind"
            or "selector_fingerprint"
            or "status"
            or "superseded_at";

    private static bool IsBootstrapLifecycleColumn(DropColumnOperation operation) =>
        IsInstanceBootstrapTable(operation.Table)
        && operation.Name is
            "completed_identity_fingerprint"
            or "configuration_fingerprint"
            or "deployment_mode"
            or "generation"
            or "mode"
            or "provider_kind"
            or "selector_fingerprint"
            or "status"
            or "superseded_at";

    private static bool IsInstanceBootstrapTable(string table) =>
        table.EndsWith("instance_bootstrap_states", StringComparison.Ordinal);

    private static string BuildInstanceBootstrapBackfillSql(
        Microsoft.EntityFrameworkCore.DbContext context)
    {
        string provider = context.Database.ProviderName ?? string.Empty;
        string prefix = UsesPrefixedTables(provider) ? RelationalModelNamespace.Prefix : string.Empty;
        string? schema = UsesSchemas(provider) ? GetConfiguredSchema(context) : null;
        string quote = QuoteStyle(provider);
        string table = Table(schema, prefix + "instance_bootstrap_states", quote);
        string id = Identifier("id", quote);
        string createdAt = Identifier("created_at", quote);
        string isCompleted = Identifier("is_completed", quote);
        string selectedDeploymentMode = Identifier("selected_deployment_mode", quote);
        string status = Identifier("status", quote);
        string mode = Identifier("mode", quote);
        string deploymentMode = Identifier("deployment_mode", quote);
        string generation = Identifier("generation", quote);
        string completedAt = Identifier("completed_at", quote);
        string completedByUserId = Identifier("completed_by_user_id", quote);
        string completedPredicate = provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase)
            ? isCompleted
            : $"{isCompleted} = 1";
        string assignments = $"""
                             {status} = CASE WHEN {completedPredicate} THEN 3 ELSE 1 END,
                             {mode} = 1,
                             {deploymentMode} = CASE WHEN {selectedDeploymentMode} = 'MultiTenant' THEN 2 ELSE 1 END,
                             {generation} = ranked.bootstrap_generation,
                             {completedAt} = CASE WHEN {completedPredicate} THEN {completedAt} ELSE NULL END,
                             {completedByUserId} = CASE WHEN {completedPredicate} THEN {completedByUserId} ELSE NULL END
                             """;

        if (provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
        {
            return $"""
                    /* {BootstrapBackfillMarker} */
                    WITH ranked AS (
                        SELECT {id}, ROW_NUMBER() OVER (ORDER BY {createdAt}, {id}) AS bootstrap_generation
                        FROM {table}
                    )
                    UPDATE {table} AS target
                    SET {assignments}
                    FROM ranked
                    WHERE target.{id} = ranked.{id};
                    """;
        }

        if (provider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            return $"""
                    /* {BootstrapBackfillMarker} */
                    WITH ranked AS (
                        SELECT {id}, ROW_NUMBER() OVER (ORDER BY {createdAt}, {id}) AS bootstrap_generation
                        FROM {table}
                    )
                    UPDATE target
                    SET {assignments}
                    FROM {table} AS target
                    INNER JOIN ranked ON target.{id} = ranked.{id};
                    """;
        }

        if (provider.Contains("MySql", StringComparison.OrdinalIgnoreCase))
        {
            return $"""
                    /* {BootstrapBackfillMarker} */
                    UPDATE {table} AS target
                    INNER JOIN (
                        SELECT {id}, ROW_NUMBER() OVER (ORDER BY {createdAt}, {id}) AS bootstrap_generation
                        FROM {table}
                    ) AS ranked ON target.{id} = ranked.{id}
                    SET {assignments};
                    """;
        }

        return $"""
                /* {BootstrapBackfillMarker} */
                WITH ranked AS (
                    SELECT {id}, ROW_NUMBER() OVER (ORDER BY {createdAt}, {id}) AS bootstrap_generation
                    FROM {table}
                )
                UPDATE {table}
                SET {status} = CASE WHEN {completedPredicate} THEN 3 ELSE 1 END,
                    {mode} = 1,
                    {deploymentMode} = CASE WHEN {selectedDeploymentMode} = 'MultiTenant' THEN 2 ELSE 1 END,
                    {generation} = (
                        SELECT ranked.bootstrap_generation
                        FROM ranked
                        WHERE ranked.{id} = {table}.{id}
                    ),
                    {completedAt} = CASE WHEN {completedPredicate} THEN {completedAt} ELSE NULL END,
                    {completedByUserId} = CASE WHEN {completedPredicate} THEN {completedByUserId} ELSE NULL END;
                """;
    }

    private static string BuildInstanceBootstrapDowngradeBackfillSql(
        Microsoft.EntityFrameworkCore.DbContext context)
    {
        string provider = context.Database.ProviderName ?? string.Empty;
        string prefix = UsesPrefixedTables(provider) ? RelationalModelNamespace.Prefix : string.Empty;
        string? schema = UsesSchemas(provider) ? GetConfiguredSchema(context) : null;
        string quote = QuoteStyle(provider);
        string table = Table(schema, prefix + "instance_bootstrap_states", quote);
        string isCompleted = Identifier("is_completed", quote);
        string selectedDeploymentMode = Identifier("selected_deployment_mode", quote);
        string status = Identifier("status", quote);
        string deploymentMode = Identifier("deployment_mode", quote);
        string trueValue = provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ? "TRUE" : "1";
        string falseValue = provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ? "FALSE" : "0";

        return $"""
                /* {BootstrapDowngradeBackfillMarker} */
                UPDATE {table}
                SET {isCompleted} = CASE WHEN {status} = 3 THEN {trueValue} ELSE {falseValue} END,
                    {selectedDeploymentMode} = CASE WHEN {deploymentMode} = 2 THEN 'MultiTenant' ELSE 'SingleTenant' END;
                """;
    }

    private static bool UsesSchemas(string provider) =>
        provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ||
        provider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase);

    private static bool UsesPrefixedTables(string provider) => !UsesSchemas(provider);

    private static string QuoteStyle(string provider) =>
        provider.Contains("MySql", StringComparison.OrdinalIgnoreCase) ? "`" :
        provider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) ? "[]" :
        "\"";

    private static string Table(string? schema, string name, string quote) => schema is null
        ? Identifier(name, quote)
        : $"{Identifier(schema, quote)}.{Identifier(name, quote)}";

    private static string Identifier(string value, string quote) => quote == "[]"
        ? $"[{value.Replace("]", "]]", StringComparison.Ordinal)}]"
        : $"{quote}{value.Replace(quote, quote + quote, StringComparison.Ordinal)}{quote}";

    private static void RewriteOperation(MigrationOperation operation, string configuredSchema)
    {
        if (operation is EnsureSchemaOperation ensureSchema &&
            StringComparer.Ordinal.Equals(ensureSchema.Name, RelationalModelNamespace.DefaultSchema))
        {
            ensureSchema.Name = configuredSchema;
        }
        else if (operation is DropSchemaOperation dropSchema &&
                 StringComparer.Ordinal.Equals(dropSchema.Name, RelationalModelNamespace.DefaultSchema))
        {
            dropSchema.Name = configuredSchema;
        }

        foreach (var property in operation.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.GetIndexParameters().Length != 0)
            {
                continue;
            }

            if (property.CanRead && property.CanWrite && property.PropertyType == typeof(string) &&
                property.GetValue(operation) is string schema &&
                StringComparer.Ordinal.Equals(schema, RelationalModelNamespace.DefaultSchema))
            {
                property.SetValue(operation, configuredSchema);
            }

            if (!property.CanRead || property.PropertyType == typeof(string) ||
                property.GetValue(operation) is not IEnumerable children)
            {
                continue;
            }

            foreach (var child in children.OfType<MigrationOperation>())
            {
                RewriteOperation(child, configuredSchema);
            }
        }
    }
}
