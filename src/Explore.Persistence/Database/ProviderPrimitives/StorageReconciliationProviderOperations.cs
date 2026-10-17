using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Database.ProviderPrimitives;

internal sealed class StorageReconciliationProviderOperations
{
    private readonly ExploreDbContext context;
    private readonly PostgresNativeQueueEnrollment? enrollment;

    private StorageReconciliationProviderOperations(
        ExploreDbContext context, PostgresNativeQueueEnrollment? enrollment)
    {
        this.context = context;
        this.enrollment = enrollment;
    }

    internal static StorageReconciliationProviderOperations? TryBindNative(
        ExploreDbContext context, PostgresNativeQueueEnrollment? enrollment) =>
        context.Database.IsNpgsql() ? new(context, enrollment) : null;

    internal Task<IReadOnlyList<StorageReconciliationSnapshot>> ListAsync(
        DateTime createdBeforeUtc, int limit, CancellationToken cancellationToken) =>
        PostgresStorageReconciliationContract.ListAsync(
            context, enrollment ?? throw new InvalidOperationException("storage_reconciliation_enrollment_required"),
            createdBeforeUtc, limit, cancellationToken);

    internal Task<bool> TryQuarantineAsync(
        StorageReconciliationSnapshot snapshot, DateTime utcNow, CancellationToken ct) =>
        PostgresStorageReconciliationContract.TryQuarantineAsync(
            context, enrollment ?? throw new InvalidOperationException("storage_reconciliation_enrollment_required"),
            snapshot, utcNow, ct);

    internal static IQueryable<StorageObject> EligibleSources(ExploreDbContext context)
    {
        string ordinalCollation = context.Database.ProviderName switch
        {
            RelationalNamedLock.SqliteProvider => "BINARY",
            RelationalNamedLock.SqlServerProvider => "Latin1_General_100_BIN2",
            RelationalNamedLock.MySqlProvider => "utf8mb4_bin",
            _ => throw new InvalidOperationException("Unsupported native storage reconciliation provider.")
        };
        var sessions = context.StorageUploadSessions.IgnoreQueryFilters([QueryFilterNames.Tenant]).AsNoTracking();
        var producers = context.Set<StorageProducerOperation>().AsNoTracking();
        return context.StorageObjects.IgnoreQueryFilters([QueryFilterNames.Tenant, QueryFilterNames.SoftDelete])
            .AsNoTracking().Where(row =>
                !row.IsDeleted && row.DeletedAt == null
                && row.LifecycleState == StorageObjectLifecycleStates.Active
                && row.StorageProviderBindingId != null && row.ObjectKey != null
                && (row.Provider == StorageProviders.Local || row.Provider == StorageProviders.S3Compatible)
                && row.Purpose != StorageObjectPurposes.EventResource
                && row.OwningResourceKind != StorageOwningResourceKinds.EventResource
                && !context.EventResources.IgnoreQueryFilters(new[] { QueryFilterNames.Tenant, QueryFilterNames.SoftDelete })
                    .Any(resource => resource.StorageObjectId == row.Id)
                && !context.OrganizationTenantEvidence.IgnoreQueryFilters(new[] { QueryFilterNames.Tenant, QueryFilterNames.SoftDelete })
                    .Any(evidence => evidence.DocumentStorageObjectId == row.Id)
                && !context.StorageObjectDeletionTombstones.Any(work => work.Id == row.Id)
                && context.StorageProviderBindings.Any(binding => binding.Id == row.StorageProviderBindingId
                    && binding.Provider == row.Provider)
                && sessions.Count(session => session.StorageObjectId == row.Id || session.Id == row.Id) <= 1
                && !(sessions.Any(session => session.StorageObjectId == row.Id || session.Id == row.Id)
                    && producers.Any(producer => producer.Id == row.Id))
                && !sessions.Any(session => (session.StorageObjectId == row.Id || session.Id == row.Id)
                    && (!session.ProducerSettled || session.TenantId != row.TenantId
                        || (session.StorageObjectId ?? session.Id) != row.Id
                        || EF.Functions.Collate(session.Provider, ordinalCollation) != row.Provider
                        || session.StorageProviderBindingId != row.StorageProviderBindingId
                        || EF.Functions.Collate(session.ObjectKey!, ordinalCollation) != row.ObjectKey
                        || session.ProviderVersionId != null
                            && EF.Functions.Collate(session.ProviderVersionId, ordinalCollation) != row.ProviderVersionId
                        || EF.Functions.Collate(session.Purpose, ordinalCollation) != row.Purpose
                        || EF.Functions.Collate(session.OwningResourceKind!, ordinalCollation) != row.OwningResourceKind
                        || session.OwningResourceId != row.OwningResourceId))
                && !producers.Any(producer => producer.Id == row.Id
                    && (!producer.ProducerSettled || producer.TenantId != row.TenantId
                        || EF.Functions.Collate(producer.Provider, ordinalCollation) != row.Provider
                        || producer.ProviderBindingId != row.StorageProviderBindingId
                        || EF.Functions.Collate(producer.ObjectKey, ordinalCollation) != row.ObjectKey
                        || producer.ProviderVersionId != null
                            && EF.Functions.Collate(producer.ProviderVersionId, ordinalCollation) != row.ProviderVersionId)));
    }
}
