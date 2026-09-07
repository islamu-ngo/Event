using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Explore.Persistence.Database;

namespace Explore.Persistence.Schema;

public static class PostgresModelConstraintApplier
{
    private const string BtreeGistExtensionName = "btree_gist";

    public static async Task ApplyAsync(ExploreDbContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Database.ProviderName != "Npgsql.EntityFrameworkCore.PostgreSQL")
        {
            return;
        }

        var targetSchema = context.GetService<IDbContextOptions>()
            .FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema;
        var constraints = GetConfiguredConstraints(context.Model, targetSchema);
        if (constraints.Count == 0)
        {
            return;
        }

        await context.Database.ExecuteSqlRawAsync(
            $"CREATE EXTENSION IF NOT EXISTS {BtreeGistExtensionName};",
            cancellationToken);

        foreach (var constraint in constraints)
        {
            await context.Database.ExecuteSqlRawAsync(BuildAddConstraintSql(constraint), cancellationToken);
        }
    }

    private static List<ModelExclusionConstraint> GetConfiguredConstraints(
        IModel model,
        string? targetSchema)
    {
        return model.GetEntityTypes()
            .SelectMany(entityType => GetEntityConstraints(entityType, targetSchema))
            .OrderBy(c => c.TableName, StringComparer.Ordinal)
            .ThenBy(c => c.Descriptor.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static IEnumerable<ModelExclusionConstraint> GetEntityConstraints(
        IEntityType entityType,
        string? targetSchema)
    {
        var tableName = entityType.GetTableName();
        if (string.IsNullOrWhiteSpace(tableName))
        {
            yield break;
        }

        foreach (var annotation in entityType.GetAnnotations()
                     .Where(a => PostgresExclusionConstraintExtensions.IsExclusionConstraintAnnotation(a.Name)))
        {
            yield return new ModelExclusionConstraint(
                tableName,
                targetSchema ?? entityType.GetSchema() ?? entityType.Model.GetDefaultSchema(),
                PostgresExclusionConstraintExtensions.ParseDescriptor(annotation.Value));
        }
    }

    private static string BuildAddConstraintSql(ModelExclusionConstraint constraint)
    {
        var tableIdentifier = QuoteQualifiedIdentifier(constraint.Schema, constraint.TableName);
        var tableRegClass = QuoteLiteral(
            string.IsNullOrWhiteSpace(constraint.Schema)
                ? constraint.TableName
                : $"{constraint.Schema}.{constraint.TableName}");
        var constraintName = QuoteIdentifier(constraint.Descriptor.Name);
        var constraintNameLiteral = QuoteLiteral(constraint.Descriptor.Name);
        var searchPathBlock = BuildSearchPathBlock(constraint.Schema);
        var preflightBlock = BuildPreflightBlock(constraint.Descriptor);

        return $$"""
            DO $$
            BEGIN
                {{searchPathBlock}}
                IF NOT EXISTS (
                    SELECT 1
                    FROM pg_constraint
                    WHERE conname = {{constraintNameLiteral}}
                      AND conrelid = {{tableRegClass}}::regclass
                ) THEN
                    {{preflightBlock}}
                    ALTER TABLE {{tableIdentifier}}
                    ADD CONSTRAINT {{constraintName}}
                    EXCLUDE USING {{constraint.Descriptor.UsingMethod}} (
                        {{constraint.Descriptor.ElementsSql}}
                    )
                    WHERE ({{constraint.Descriptor.PredicateSql}});
                END IF;
            END $$;
            """;
    }

    private static string BuildSearchPathBlock(string? schema)
    {
        if (string.IsNullOrWhiteSpace(schema))
        {
            return string.Empty;
        }

        var searchPath = QuoteLiteral($"{QuoteIdentifier(schema)}, public, pg_catalog");
        return $"PERFORM set_config('search_path', {searchPath}, true);";
    }

    private static string BuildPreflightBlock(PostgresExclusionConstraintDescriptor descriptor)
    {
        if (string.IsNullOrWhiteSpace(descriptor.PreflightConflictExistsSql))
        {
            return string.Empty;
        }

        var failureMessage = QuoteLiteral(
            string.IsNullOrWhiteSpace(descriptor.PreflightFailureMessage)
                ? $"Cannot add PostgreSQL exclusion constraint {descriptor.Name} because existing rows violate it."
                : descriptor.PreflightFailureMessage);

        return $$"""
            IF EXISTS (
                {{descriptor.PreflightConflictExistsSql}}
            ) THEN
                RAISE EXCEPTION {{failureMessage}};
            END IF;

        """;
    }

    private static string QuoteQualifiedIdentifier(string? schema, string tableName)
    {
        return string.IsNullOrWhiteSpace(schema)
            ? QuoteIdentifier(tableName)
            : $"{QuoteIdentifier(schema)}.{QuoteIdentifier(tableName)}";
    }

    private static string QuoteIdentifier(string identifier)
    {
        return "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private static string QuoteLiteral(string value)
    {
        return "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
    }

    private sealed record ModelExclusionConstraint(
        string TableName,
        string? Schema,
        PostgresExclusionConstraintDescriptor Descriptor);
}
