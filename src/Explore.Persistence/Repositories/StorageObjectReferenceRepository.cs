using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class StorageObjectReferenceRepository(ExploreDbContext dbContext)
    : IStorageObjectReferenceRepository, IStorageObjectRetirementEligibilityReader
{
    public Task<IReadOnlyList<StorageObject>> FenceAsync(
        IReadOnlyCollection<Guid> storageObjectIds, CancellationToken cancellationToken) =>
        dbContext.FenceStorageObjectsAsync(storageObjectIds, cancellationToken);

    public async Task<bool> HasBlockingReferencesAsync(Guid storageObjectId, CancellationToken cancellationToken)
    {
        RequireTransaction();
        return await HasBlockingReferencesCoreAsync(storageObjectId, cancellationToken);
    }

    public async Task<bool> CanRetireAsync(
        StorageObject storageObject,
        DateTime serverNowUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(storageObject);
        if (serverNowUtc == default || serverNowUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Retirement evaluation requires a non-default server UTC instant.",
                nameof(serverNowUtc));
        if (storageObject.IsDeleted || storageObject.DeletedAt is not null
            || storageObject.LifecycleState is StorageObjectLifecycleStates.Deleted
                or StorageObjectLifecycleStates.DeleteRequested
            || storageObject.Purpose == StorageObjectPurposes.EventResource
            || storageObject.OwningResourceKind == StorageOwningResourceKinds.EventResource
            || await dbContext.StorageObjectDeletionTombstones.AsNoTracking()
                .AnyAsync(item => item.Id == storageObject.Id, cancellationToken)
            || await HasBlockingReferencesCoreAsync(storageObject.Id, cancellationToken)
            || await HasBlockingHoldsCoreAsync(storageObject.Id, serverNowUtc, cancellationToken))
            return false;

        var sessions = await dbContext.StorageUploadSessions
            .IgnoreQueryFilters([QueryFilterNames.Tenant])
            .AsNoTracking()
            .Where(item => item.StorageObjectId == storageObject.Id || item.Id == storageObject.Id)
            .ToArrayAsync(cancellationToken);
        var operation = await dbContext.Set<StorageProducerOperation>()
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == storageObject.Id, cancellationToken);

        StorageObjectDeletionTombstone target;
        try
        {
            target = StorageRetirementTarget.Capture(storageObject, sessions, operation, serverNowUtc);
        }
        catch (ArgumentException)
        {
            return false;
        }

        return await dbContext.StorageProviderBindings.AsNoTracking()
            .AnyAsync(binding => binding.Id == target.ProviderBindingId
                && binding.Provider == target.Provider, cancellationToken);
    }

    private Task<bool> HasBlockingReferencesCoreAsync(
        Guid storageObjectId,
        CancellationToken cancellationToken) =>
        StorageObjectPhysicalReferenceQuery.HasBlockingReferencesAsync(
            dbContext, storageObjectId, cancellationToken);

    public Task<bool> HasBlockingHoldsAsync(
        Guid storageObjectId, DateTime serverNowUtc, CancellationToken cancellationToken)
    {
        RequireTransaction();
        return HasBlockingHoldsCoreAsync(storageObjectId, serverNowUtc, cancellationToken);
    }

    private Task<bool> HasBlockingHoldsCoreAsync(
        Guid storageObjectId,
        DateTime serverNowUtc,
        CancellationToken cancellationToken)
    {
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
