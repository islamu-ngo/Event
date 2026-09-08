using Explore.Persistence.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Explore.Persistence.Database;

internal static class RelationalConstraintDescriptorResolver
{
    internal static RelationalConstraintDescriptor UniqueIndex<TEntity>(
        DbContext context,
        params string[] propertyNames)
        where TEntity : class
    {
        IEntityType entityType = FindEntityType<TEntity>(context);
        IReadOnlyIndex index = entityType.GetIndexes().Single(candidate =>
            candidate.IsUnique &&
            candidate.Properties.Select(property => property.Name).SequenceEqual(propertyNames));
        return CreateDescriptor(index.GetDatabaseName(), entityType, index.Properties);
    }

    internal static RelationalConstraintDescriptor PrimaryKey<TEntity>(DbContext context)
        where TEntity : class
    {
        IEntityType entityType = FindEntityType<TEntity>(context);
        IReadOnlyKey key = entityType.FindPrimaryKey()
            ?? throw new InvalidOperationException($"{entityType.DisplayName()} has no primary key.");
        return CreateDescriptor(key.GetName(), entityType, key.Properties);
    }

    internal static string ExclusionConstraint<TEntity>(DbContext context)
        where TEntity : class
    {
        IEntityType entityType = FindEntityType<TEntity>(context);
        return entityType.GetAnnotations()
            .Where(annotation =>
                PostgresExclusionConstraintExtensions.IsExclusionConstraintAnnotation(annotation.Name))
            .Select(annotation =>
                PostgresExclusionConstraintExtensions.ParseDescriptor(annotation.Value).Name)
            .Single();
    }

    private static IEntityType FindEntityType<TEntity>(DbContext context)
        where TEntity : class =>
        context.Model.FindEntityType(typeof(TEntity))
        ?? throw new InvalidOperationException($"{typeof(TEntity).Name} is not part of the active EF model.");

    private static RelationalConstraintDescriptor CreateDescriptor(
        string? name,
        IReadOnlyEntityType entityType,
        IReadOnlyList<IReadOnlyProperty> properties)
    {
        StoreObjectIdentifier table = StoreObjectIdentifier.Create(entityType, StoreObjectType.Table)
            ?? throw new InvalidOperationException($"{entityType.DisplayName()} is not mapped to a table.");
        string resolvedName = name
            ?? throw new InvalidOperationException(
                $"{entityType.DisplayName()} relational constraint has no finalized database name.");
        string[] qualifiedColumns = properties
            .Select(property =>
                $"{table.Name}.{property.GetColumnName(table) ??
                    throw new InvalidOperationException($"{property.Name} has no finalized column name.")}")
            .ToArray();
        return new RelationalConstraintDescriptor(resolvedName, qualifiedColumns);
    }
}

internal sealed record RelationalConstraintDescriptor(
    string Name,
    IReadOnlyList<string> QualifiedColumns);
