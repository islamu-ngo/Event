using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Database.ProviderPrimitives;

internal static class StorageObjectRetentionQuery
{
    internal static async Task<bool> HasBlockingHoldsAsync(
        ExploreDbContext context, Guid storageObjectId, DateTime serverNowUtc, CancellationToken cancellationToken)
    {
        var sql = context.GetService<ISqlGenerationHelper>();
        string query = $"SELECT CASE WHEN {BuildPredicate(context, "{0}", "{1}")} THEN 1 ELSE 0 END AS {sql.DelimitIdentifier("Value")}";
        return await context.Database.SqlQueryRaw<int>(query, storageObjectId, serverNowUtc)
            .SingleAsync(cancellationToken) == 1;
    }

    /// <summary>
    /// Shares the physical retention predicate with bounded native capabilities.
    /// Expressions are compiled SQL parameters, never caller-supplied SQL.
    /// </summary>
    internal static string BuildPredicate(
        ExploreDbContext context, string storageObjectIdSql, string serverNowUtcSql)
    {
        var sql = context.GetService<ISqlGenerationHelper>();
        string Table<T>() => sql.DelimitIdentifier(
            context.Model.FindEntityType(typeof(T))!.GetTableName()!,
            context.Model.FindEntityType(typeof(T))!.GetSchema());
        string Column<T>(string property)
        {
            var entity = context.Model.FindEntityType(typeof(T))!;
            var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            return sql.DelimitIdentifier(entity.FindProperty(property)!.GetColumnName(store)!);
        }
        string S(string property) => "s." + Column<StorageObject>(property);
        string R(string property) => "r." + Column<RegistrationSubmission>(property);
        string O(string property) => "o." + Column<RegistrationOrder>(property);
        string V(string property) => "v." + Column<RegistrationFormVersion>(property);
        string F(string property) => "f." + Column<RegistrationFormField>(property);
        string P(string property) => "p." + Column<RegistrationRetentionPolicy>(property);
        string D(string property) => "d." + Column<RegistrationProviderSubmissionWriteEffect>(property);
        string falseSql = context.GetService<IRelationalTypeMappingSource>()
            .FindMapping(typeof(bool))!.GenerateSqlLiteral(false);
        var statusMapping = context.Model.FindEntityType(typeof(RegistrationProviderSubmissionWriteEffect))!
            .FindProperty(nameof(RegistrationProviderSubmissionWriteEffect.Status))!.GetRelationalTypeMapping();
        string completed = statusMapping.GenerateSqlLiteral(OutboxMessageStatus.Completed);
        string deadLettered = statusMapping.GenerateSqlLiteral(OutboxMessageStatus.DeadLettered);
        return $"""
            EXISTS (
                SELECT 1 FROM {Table<StorageObject>()} s
                WHERE {S(nameof(StorageObject.Id))} = {storageObjectIdSql}
                  AND {S(nameof(StorageObject.OwningResourceKind))} = 'registration_submission_sink'
                  AND ({S(nameof(StorageObject.RegistrationContentRetentionUntilUtc))} IS NULL
                    OR {S(nameof(StorageObject.RegistrationContentRetentionUntilUtc))} > {serverNowUtcSql}
                    OR NOT EXISTS (
                        SELECT 1 FROM {Table<RegistrationSubmission>()} r
                        JOIN {Table<RegistrationOrder>()} o
                          ON {O(nameof(RegistrationOrder.TenantId))} = {R(nameof(RegistrationSubmission.TenantId))}
                         AND {O(nameof(RegistrationOrder.EventId))} = {R(nameof(RegistrationSubmission.EventId))}
                         AND {O(nameof(RegistrationOrder.Id))} = {R(nameof(RegistrationSubmission.RegistrationOrderId))}
                        JOIN {Table<RegistrationFormVersion>()} v
                          ON {V(nameof(RegistrationFormVersion.TenantId))} = {R(nameof(RegistrationSubmission.TenantId))}
                         AND {V(nameof(RegistrationFormVersion.EventId))} = {R(nameof(RegistrationSubmission.EventId))}
                         AND {V(nameof(RegistrationFormVersion.RegistrationFormId))} = {R(nameof(RegistrationSubmission.RegistrationFormId))}
                         AND {V(nameof(RegistrationFormVersion.Id))} = {R(nameof(RegistrationSubmission.RegistrationFormVersionId))}
                        WHERE {R(nameof(RegistrationSubmission.TenantId))} = {S(nameof(StorageObject.TenantId))}
                          AND {R(nameof(RegistrationSubmission.Id))} = {S(nameof(StorageObject.OwningResourceId))}
                          AND {O(nameof(RegistrationOrder.AnonymousPiiRetentionUntilUtc))} IS NOT NULL
                          AND {V(nameof(RegistrationFormVersion.PublishedAt))} IS NOT NULL
                          AND {V(nameof(RegistrationFormVersion.SchemaHash))} IS NOT NULL
                          AND EXISTS (
                              SELECT 1 FROM {Table<RegistrationFormField>()} f
                              WHERE {F(nameof(RegistrationFormField.TenantId))} = {R(nameof(RegistrationSubmission.TenantId))}
                                AND {F(nameof(RegistrationFormField.RegistrationFormVersionId))} = {V(nameof(RegistrationFormVersion.Id))})
                          AND NOT EXISTS (
                              SELECT 1 FROM {Table<RegistrationFormField>()} f
                              WHERE {F(nameof(RegistrationFormField.TenantId))} = {R(nameof(RegistrationSubmission.TenantId))}
                                AND {F(nameof(RegistrationFormField.RegistrationFormVersionId))} = {V(nameof(RegistrationFormVersion.Id))}
                                AND NOT EXISTS (
                                    SELECT 1 FROM {Table<RegistrationRetentionPolicy>()} p
                                    WHERE {P(nameof(RegistrationRetentionPolicy.Id))} = {F(nameof(RegistrationFormField.RetentionPolicyId))}
                                      AND {P(nameof(RegistrationRetentionPolicy.IsLegalHold))} = {falseSql}
                                      AND {P(nameof(RegistrationRetentionPolicy.DurationDays))} IS NOT NULL))
                          AND ({R(nameof(RegistrationSubmission.RegistrationProviderBindingId))} IS NULL
                            OR EXISTS (
                                SELECT 1 FROM {Table<RegistrationProviderSubmissionWriteEffect>()} d
                                WHERE {D(nameof(RegistrationProviderSubmissionWriteEffect.TenantId))} = {R(nameof(RegistrationSubmission.TenantId))}
                                  AND {D(nameof(RegistrationProviderSubmissionWriteEffect.EventId))} = {R(nameof(RegistrationSubmission.EventId))}
                                  AND {D(nameof(RegistrationProviderSubmissionWriteEffect.RegistrationOrderId))} = {R(nameof(RegistrationSubmission.RegistrationOrderId))}
                                  AND {D(nameof(RegistrationProviderSubmissionWriteEffect.RegistrationAttemptId))} = {R(nameof(RegistrationSubmission.RegistrationAttemptId))}
                                  AND {D(nameof(RegistrationProviderSubmissionWriteEffect.RegistrationSubmissionId))} = {R(nameof(RegistrationSubmission.Id))}
                                  AND {D(nameof(RegistrationProviderSubmissionWriteEffect.RegistrationProviderBindingId))} = {R(nameof(RegistrationSubmission.RegistrationProviderBindingId))}
                                  AND {D(nameof(RegistrationProviderSubmissionWriteEffect.ParkedAt))} IS NULL
                                  AND (({D(nameof(RegistrationProviderSubmissionWriteEffect.Status))} = {completed}
                                        AND {D(nameof(RegistrationProviderSubmissionWriteEffect.CompletedAt))} IS NOT NULL)
                                    OR ({D(nameof(RegistrationProviderSubmissionWriteEffect.Status))} = {deadLettered}
                                        AND {D(nameof(RegistrationProviderSubmissionWriteEffect.DeadLetteredAt))} IS NOT NULL)))))))
            """;
    }
}
