using System.Text.Json;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Schema;

public static class PostgresExclusionConstraintExtensions
{
    internal const string AnnotationPrefix = "Explore:PostgresExclusionConstraint:";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static EntityTypeBuilder<TEntity> HasPostgresExclusionConstraint<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        string name,
        string usingMethod,
        string elementsSql,
        string predicateSql,
        string? preflightConflictExistsSql = null,
        string? preflightFailureMessage = null)
        where TEntity : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(usingMethod);
        ArgumentException.ThrowIfNullOrWhiteSpace(elementsSql);
        ArgumentException.ThrowIfNullOrWhiteSpace(predicateSql);

        var descriptor = new PostgresExclusionConstraintDescriptor(
            name,
            usingMethod,
            elementsSql.Trim(),
            predicateSql.Trim(),
            string.IsNullOrWhiteSpace(preflightConflictExistsSql) ? null : preflightConflictExistsSql.Trim(),
            string.IsNullOrWhiteSpace(preflightFailureMessage) ? null : preflightFailureMessage.Trim());

        builder.HasAnnotation(GetAnnotationName(name), JsonSerializer.Serialize(descriptor, SerializerOptions));

        return builder;
    }

    internal static bool IsExclusionConstraintAnnotation(string annotationName)
    {
        return annotationName.StartsWith(AnnotationPrefix, StringComparison.Ordinal);
    }

    internal static PostgresExclusionConstraintDescriptor ParseDescriptor(object? annotationValue)
    {
        if (annotationValue is not string serialized || string.IsNullOrWhiteSpace(serialized))
        {
            throw new InvalidOperationException("PostgreSQL exclusion constraint annotation value is missing.");
        }

        return JsonSerializer.Deserialize<PostgresExclusionConstraintDescriptor>(serialized, SerializerOptions)
               ?? throw new InvalidOperationException("PostgreSQL exclusion constraint annotation value is invalid.");
    }

    private static string GetAnnotationName(string name)
    {
        return AnnotationPrefix + name;
    }
}

internal sealed record PostgresExclusionConstraintDescriptor(
    string Name,
    string UsingMethod,
    string ElementsSql,
    string PredicateSql,
    string? PreflightConflictExistsSql,
    string? PreflightFailureMessage);
