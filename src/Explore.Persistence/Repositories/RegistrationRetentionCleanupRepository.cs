// ABOUTME: Executes bounded tenant-scoped deletion of expired registration answers and PII.
// ABOUTME: Deletes dependent answers before ciphertext atomically while preserving consent and export audit evidence.

using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class RegistrationRetentionCleanupRepository(ExploreDbContext dbContext, IUnitOfWork unitOfWork)
    : IRegistrationRetentionCleanupRepository
{
    public async Task<RegistrationRetentionCleanupResult> CleanupTenantAsync(
        Guid tenantId,
        DateTime utcNow,
        int batchSize,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(tenantId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);
        if (utcNow.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Cleanup time must be UTC.", nameof(utcNow));
        }

        return await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            await ScheduleExpiredCsvDeletionAsync(tenantId, utcNow, batchSize, token);
            Guid[] answerIds = await dbContext.RegistrationAnswers
                .IgnoreQueryFilters([QueryFilterNames.Tenant, QueryFilterNames.SoftDelete])
                .Where(answer => answer.TenantId == tenantId && answer.RetentionUntil <= utcNow)
                .OrderBy(answer => answer.RetentionUntil)
                .Select(answer => answer.Id)
                .Take(batchSize)
                .ToArrayAsync(token);
            Guid[] sensitiveValueIds = await dbContext.RegistrationAnswers
                .IgnoreQueryFilters([QueryFilterNames.Tenant, QueryFilterNames.SoftDelete])
                .Where(answer => answerIds.Contains(answer.Id) && answer.SensitiveAnswerValueId != null)
                .Select(answer => answer.SensitiveAnswerValueId!.Value)
                .ToArrayAsync(token);
            int answersDeleted = await dbContext.RegistrationAnswers
                .IgnoreQueryFilters([QueryFilterNames.Tenant, QueryFilterNames.SoftDelete])
                .Where(answer => answer.TenantId == tenantId && answerIds.Contains(answer.Id))
                .ExecuteDeleteAsync(token);
            int sensitiveValuesDeleted = await dbContext.RegistrationSensitiveAnswerValues
                .IgnoreQueryFilters([QueryFilterNames.Tenant, QueryFilterNames.SoftDelete])
                .Where(value => value.TenantId == tenantId && sensitiveValueIds.Contains(value.Id))
                .ExecuteDeleteAsync(token);
            int orderPiiDeleted = await dbContext.RegistrationOrderPii
                .IgnoreQueryFilters([QueryFilterNames.Tenant])
                .Where(pii => pii.TenantId == tenantId && pii.RetentionUntil <= utcNow)
                .Take(batchSize)
                .ExecuteDeleteAsync(token);
            int participantPiiDeleted = await dbContext.RegistrationParticipantPii
                .IgnoreQueryFilters([QueryFilterNames.Tenant])
                .Where(pii => pii.TenantId == tenantId && pii.RetentionUntil <= utcNow)
                .Take(batchSize)
                .ExecuteDeleteAsync(token);

            return new RegistrationRetentionCleanupResult(
                answersDeleted, sensitiveValuesDeleted, orderPiiDeleted, participantPiiDeleted);
        }, cancellationToken);
    }

    private async Task ScheduleExpiredCsvDeletionAsync(
        Guid tenantId, DateTime utcNow, int batchSize, CancellationToken cancellationToken)
    {
        // The immutable published form version remains authority after source answers are swept.
        // Any held, unknown, missing or draft authority preserves the physical artifact.
        Guid[] storageIds = await (from storage in dbContext.StorageObjects
                .IgnoreQueryFilters([QueryFilterNames.Tenant, QueryFilterNames.SoftDelete])
            join submission in dbContext.RegistrationSubmissions
                    .IgnoreQueryFilters([QueryFilterNames.Tenant, QueryFilterNames.SoftDelete])
                on new { storage.TenantId, Id = storage.OwningResourceId }
                equals new { submission.TenantId, Id = (Guid?)submission.Id }
            join order in dbContext.RegistrationOrders
                    .IgnoreQueryFilters([QueryFilterNames.Tenant, QueryFilterNames.SoftDelete])
                on new { submission.TenantId, submission.EventId, Id = submission.RegistrationOrderId }
                equals new { order.TenantId, order.EventId, order.Id }
            join version in dbContext.RegistrationFormVersions
                    .IgnoreQueryFilters([QueryFilterNames.Tenant, QueryFilterNames.SoftDelete])
                on new { submission.TenantId, submission.EventId, submission.RegistrationFormId, Id = submission.RegistrationFormVersionId }
                equals new { version.TenantId, version.EventId, version.RegistrationFormId, version.Id }
            where storage.TenantId == tenantId && !storage.IsDeleted &&
                storage.OwningResourceKind == "registration_submission_sink" &&
                storage.LifecycleState == StorageObjectLifecycleStates.Active &&
                storage.RegistrationContentRetentionUntilUtc <= utcNow &&
                order.AnonymousPiiRetentionUntilUtc != null &&
                version.PublishedAt != null && version.SchemaHash != null &&
                dbContext.RegistrationFormFields
                    .IgnoreQueryFilters(new[] { QueryFilterNames.Tenant, QueryFilterNames.SoftDelete })
                    .Any(field => field.TenantId == tenantId && field.RegistrationFormVersionId == version.Id) &&
                !dbContext.RegistrationFormFields
                    .IgnoreQueryFilters(new[] { QueryFilterNames.Tenant, QueryFilterNames.SoftDelete })
                    .Any(field => field.TenantId == tenantId && field.RegistrationFormVersionId == version.Id &&
                        !dbContext.RegistrationRetentionPolicies.Any(policy => policy.Id == field.RetentionPolicyId &&
                            !policy.IsLegalHold && policy.DurationDays != null))
            orderby storage.RegistrationContentRetentionUntilUtc, storage.Id
            select storage.Id).Take(batchSize).ToArrayAsync(cancellationToken);

        await dbContext.StorageObjects
            .IgnoreQueryFilters([QueryFilterNames.Tenant, QueryFilterNames.SoftDelete])
            .Where(storage => storage.TenantId == tenantId && storageIds.Contains(storage.Id))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(storage => storage.LifecycleState, StorageObjectLifecycleStates.DeleteRequested)
                .SetProperty(storage => storage.UpdatedAt, utcNow), cancellationToken);
    }
}
