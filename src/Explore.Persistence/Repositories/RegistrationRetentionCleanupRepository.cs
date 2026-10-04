using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
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
            // Cleanup and worker claims must agree on whether a provider handoff is still possible.
            await using IAsyncDisposable claimLock = await RelationalNamedLock.AcquireTransactionAsync(
                dbContext, "registration-provider-submission-write-claim", token);
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
            List<RegistrationProviderSubmissionWriteEffect> effects =
                await GetUnclaimedExpiredProviderWritesAsync(tenantId, utcNow, answerIds, token);
            Guid[] csvIds = await GetExpiredCsvStorageIdsAsync(tenantId, utcNow, batchSize, token);
            Guid[] submissionIds = effects.Select(effect => effect.RegistrationSubmissionId).ToArray();
            Guid[] deliveryStorageIds = await dbContext.StorageObjects.AsNoTracking()
                .IgnoreQueryFilters([QueryFilterNames.Tenant, QueryFilterNames.SoftDelete])
                .Where(storage => storage.TenantId == tenantId &&
                    storage.OwningResourceKind == "registration_submission_sink" &&
                    storage.OwningResourceId != null && submissionIds.Contains(storage.OwningResourceId.Value))
                .Select(storage => storage.Id).ToArrayAsync(token);
            var references = new StorageObjectReferenceRepository(dbContext);
            await references.FenceAsync(csvIds.Concat(deliveryStorageIds).Distinct().ToArray(), token);
            await SettleUnclaimedExpiredProviderWritesAsync(effects, utcNow, token);
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
            await RetireExpiredCsvAsync(tenantId, utcNow, csvIds, token);

            return new RegistrationRetentionCleanupResult(
                answersDeleted, sensitiveValuesDeleted, orderPiiDeleted, participantPiiDeleted);
        }, cancellationToken);
    }

    private async Task<List<RegistrationProviderSubmissionWriteEffect>> GetUnclaimedExpiredProviderWritesAsync(
        Guid tenantId, DateTime utcNow, Guid[] answerIds, CancellationToken cancellationToken)
    {
        var mappedAnswers = from answer in dbContext.RegistrationAnswers
                .IgnoreQueryFilters([QueryFilterNames.Tenant])
                            join field in dbContext.RegistrationFormFields.IgnoreQueryFilters([QueryFilterNames.Tenant])
                                on new { answer.TenantId, Id = answer.RegistrationFormFieldId }
                                equals new { field.TenantId, field.Id }
                            join mapping in dbContext.RegistrationProviderFieldMappings.IgnoreQueryFilters([QueryFilterNames.Tenant])
                                on new { field.TenantId, PlatformFieldKey = field.Namespace + "." + field.Key }
                                equals new { mapping.TenantId, mapping.PlatformFieldKey }
                            where answer.TenantId == tenantId && field.IsProviderTransferAllowed && !mapping.IsDeleted
                            select new
                            {
                                answer.Id,
                                answer.RegistrationSubmissionId,
                                answer.RegistrationAttemptId,
                                answer.RegistrationOrderId,
                                mapping.RegistrationProviderBindingId,
                                answer.RetentionUntil,
                                SensitiveRetentionUntil = answer.SensitiveAnswerValue == null
                                    ? (DateTime?)null : answer.SensitiveAnswerValue.RetentionUntil
                            };

        return await (from effect in dbContext.RegistrationProviderSubmissionWriteEffects
                .IgnoreQueryFilters([QueryFilterNames.Tenant])
                      join order in dbContext.RegistrationOrders.IgnoreQueryFilters([QueryFilterNames.Tenant])
                          on new { effect.TenantId, effect.EventId, Id = effect.RegistrationOrderId }
                          equals new { order.TenantId, order.EventId, order.Id }
                      where effect.TenantId == tenantId && order.AnonymousPiiRetentionUntilUtc != null &&
                          effect.Status == OutboxMessageStatus.Pending && effect.AttemptCount == 0 &&
                          mappedAnswers.Any(answer => answer.RegistrationSubmissionId == effect.RegistrationSubmissionId &&
                              answer.RegistrationAttemptId == effect.RegistrationAttemptId &&
                              answer.RegistrationOrderId == effect.RegistrationOrderId &&
                              answer.RegistrationProviderBindingId == effect.RegistrationProviderBindingId && answerIds.Contains(answer.Id)) &&
                          !mappedAnswers.Any(answer => answer.RegistrationSubmissionId == effect.RegistrationSubmissionId &&
                              answer.RegistrationAttemptId == effect.RegistrationAttemptId &&
                              answer.RegistrationOrderId == effect.RegistrationOrderId &&
                              answer.RegistrationProviderBindingId == effect.RegistrationProviderBindingId &&
                              (answer.RetentionUntil == null || answer.RetentionUntil > utcNow) &&
                              (answer.SensitiveRetentionUntil == null || answer.SensitiveRetentionUntil > utcNow))
                      select effect).ToListAsync(cancellationToken);
    }

    private async Task SettleUnclaimedExpiredProviderWritesAsync(
        List<RegistrationProviderSubmissionWriteEffect> effects, DateTime utcNow, CancellationToken cancellationToken)
    {
        // Processing or previously attempted work may already have crossed the external boundary.
        // The temporary claim and terminal outcome are persisted in the cleanup transaction.
        foreach (RegistrationProviderSubmissionWriteEffect effect in effects)
        {
            Guid leaseToken = Guid.CreateVersion7();
            effect.Claim("registration-retention-cleanup", leaseToken, utcNow.AddMinutes(1), utcNow);
            effect.DeadLetter(leaseToken, effect.ProcessingFence, "registration_data_retention_expired", utcNow);
        }

        if (effects.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<Guid[]> GetExpiredCsvStorageIdsAsync(
        Guid tenantId, DateTime utcNow, int batchSize, CancellationToken cancellationToken)
    {
        // The immutable published form version remains authority after source answers are swept.
        // Any held, unknown, missing or draft authority preserves the physical artifact.
        return await (from storage in dbContext.StorageObjects
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
    }

    private async Task RetireExpiredCsvAsync(
        Guid tenantId, DateTime utcNow, Guid[] storageIds, CancellationToken cancellationToken)
    {
        var lifecycle = new EventResourceStorageLifecycleRepository(dbContext);
        foreach (Guid id in storageIds)
        {
            StorageRetirementAdmission admission = await lifecycle.TryQueueRetirementAsync(
                tenantId, id, utcNow, cancellationToken);
            if (admission is StorageRetirementAdmission.NotFound or StorageRetirementAdmission.InvalidTarget)
                throw new InvalidOperationException("Expired registration storage requires exact retained custody.");
        }
    }
}
