using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

public sealed class StorageObjectReferenceRepository(ExploreDbContext dbContext) : IStorageObjectReferenceRepository
{
    public Task<IReadOnlyList<StorageObject>> FenceAsync(
        IReadOnlyCollection<Guid> storageObjectIds, CancellationToken cancellationToken) =>
        dbContext.FenceStorageObjectsAsync(storageObjectIds, cancellationToken);

    public async Task<bool> HasBlockingReferencesAsync(Guid storageObjectId, CancellationToken cancellationToken)
    {
        RequireTransaction();
        var storage = dbContext.Model.FindEntityType(typeof(StorageObject))!;
        var id = storage.FindProperty(nameof(StorageObject.Id))!;
        var sql = dbContext.GetService<ISqlGenerationHelper>();
        var predicates = new List<string>();

        // Query physical owner relations, not a second usage inventory or filtered
        // entity projections. A composite FK contributes every column to its join.
        // Custody is transferred by retirement, not counted as a readable owner.
        foreach (var foreignKey in storage.GetReferencingForeignKeys()
            .Where(key => key.DeclaringEntityType.ClrType != typeof(StorageUploadSession)
                && key.DeclaringEntityType.ClrType != typeof(StorageProducerOperation)))
        {
            var constraints = foreignKey.GetMappedConstraints().ToArray();
            if (constraints.Length == 0)
                throw new InvalidOperationException("A storage reference has no relational constraint mapping.");

            foreach (var constraint in constraints)
            {
                string idColumn = id.GetColumnName(StoreObjectIdentifier.Table(
                    constraint.PrincipalTable.Name, constraint.PrincipalTable.Schema))
                    ?? throw new InvalidOperationException("A storage reference has no mapped object identity.");
                string join = string.Join(" AND ", constraint.Columns.Select((column, index) =>
                    $"owner.{sql.DelimitIdentifier(column.Name)} = source.{sql.DelimitIdentifier(constraint.PrincipalColumns[index].Name)}"));
                predicates.Add(
                    $"EXISTS (SELECT 1 FROM {sql.DelimitIdentifier(constraint.Table.Name, constraint.Table.Schema)} owner " +
                    $"INNER JOIN {sql.DelimitIdentifier(constraint.PrincipalTable.Name, constraint.PrincipalTable.Schema)} source ON {join} " +
                    $"WHERE source.{sql.DelimitIdentifier(idColumn)} = {{0}})");
            }
        }

        if (predicates.Count == 0)
            return false;

        // Only model-owned, provider-delimited identifiers enter SQL. The object
        // identity is parameterized; no owner row or cross-tenant name is returned.
        string query = $"SELECT CASE WHEN {string.Join(" OR ", predicates)} THEN 1 ELSE 0 END AS {sql.DelimitIdentifier("Value")}";
        return await dbContext.Database.SqlQueryRaw<int>(query, storageObjectId)
            .SingleAsync(cancellationToken) == 1;
    }

    public Task<bool> HasBlockingHoldsAsync(
        Guid storageObjectId, DateTime serverNowUtc, CancellationToken cancellationToken)
    {
        RequireTransaction();
        if (serverNowUtc == default || serverNowUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Retention evaluation requires a non-default server UTC instant.", nameof(serverNowUtc));

        // As in registration retention cleanup, the immutable published version
        // remains authority after answers have been erased. Missing authority is
        // not permission to delete. These are object-bounded system predicates.
        var submissions = dbContext.RegistrationSubmissions.AsNoTracking()
            .IgnoreQueryFilters([QueryFilterNames.Tenant, QueryFilterNames.SoftDelete]);
        var orders = dbContext.RegistrationOrders.AsNoTracking()
            .IgnoreQueryFilters([QueryFilterNames.Tenant, QueryFilterNames.SoftDelete]);
        var versions = dbContext.RegistrationFormVersions.AsNoTracking()
            .IgnoreQueryFilters([QueryFilterNames.Tenant, QueryFilterNames.SoftDelete]);
        var fields = dbContext.RegistrationFormFields.AsNoTracking()
            .IgnoreQueryFilters([QueryFilterNames.Tenant, QueryFilterNames.SoftDelete]);
        var policies = dbContext.RegistrationRetentionPolicies.AsNoTracking();
        var deliveries = dbContext.RegistrationProviderSubmissionWriteEffects.AsNoTracking()
            .IgnoreQueryFilters([QueryFilterNames.Tenant]);

        var releasableSubmissions =
            from submission in submissions
            join order in orders
                on new { submission.TenantId, submission.EventId, Id = submission.RegistrationOrderId }
                equals new { order.TenantId, order.EventId, order.Id }
            join version in versions
                on new { submission.TenantId, submission.EventId, submission.RegistrationFormId, Id = submission.RegistrationFormVersionId }
                equals new { version.TenantId, version.EventId, version.RegistrationFormId, version.Id }
            where order.AnonymousPiiRetentionUntilUtc != null
                && version.PublishedAt != null && version.SchemaHash != null
                && fields.Any(field => field.TenantId == submission.TenantId && field.RegistrationFormVersionId == version.Id)
                && !fields.Any(field => field.TenantId == submission.TenantId && field.RegistrationFormVersionId == version.Id
                    && !policies.Any(policy => policy.Id == field.RetentionPolicyId && !policy.IsLegalHold && policy.DurationDays != null))
                && (submission.RegistrationProviderBindingId == null || deliveries.Any(effect =>
                    effect.TenantId == submission.TenantId && effect.EventId == submission.EventId
                    && effect.RegistrationOrderId == submission.RegistrationOrderId
                    && effect.RegistrationAttemptId == submission.RegistrationAttemptId
                    && effect.RegistrationSubmissionId == submission.Id
                    && effect.RegistrationProviderBindingId == submission.RegistrationProviderBindingId
                    && effect.ParkedAt == null
                    && ((effect.Status == OutboxMessageStatus.Completed && effect.CompletedAt != null)
                        || (effect.Status == OutboxMessageStatus.DeadLettered && effect.DeadLetteredAt != null))))
            select submission;

        return dbContext.StorageObjects.AsNoTracking()
            .IgnoreQueryFilters([QueryFilterNames.Tenant, QueryFilterNames.SoftDelete])
            .AnyAsync(storage => storage.Id == storageObjectId
                && storage.OwningResourceKind == "registration_submission_sink"
                && (storage.RegistrationContentRetentionUntilUtc == null
                    || storage.RegistrationContentRetentionUntilUtc > serverNowUtc
                    || !releasableSubmissions.Any(submission =>
                        submission.TenantId == storage.TenantId && submission.Id == storage.OwningResourceId)),
                cancellationToken);
    }

    private void RequireTransaction()
    {
        if (dbContext.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Storage retirement scans require a caller-owned fenced transaction.");
    }
}
