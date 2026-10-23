using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class RegistrationProviderSubmissionWriteEffectRepository(ExploreDbContext dbContext, TimeProvider? timeProvider = null)
    : IRegistrationProviderSubmissionWriteEffectRepository
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<IReadOnlyList<RegistrationProviderSubmissionWriteClaim>> ClaimDueAsync(
        string leaseOwner,
        int batchSize,
        DateTime claimedAt,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(leaseOwner) || leaseOwner.Trim().Length > RegistrationProviderSubmissionWriteEffect.MaxLeaseOwnerLength ||
            batchSize is < 1 or > 1000 || leaseDuration <= TimeSpan.Zero || claimedAt.Kind != DateTimeKind.Utc)
        {
            return [];
        }
        var native = await RegistrationProviderSubmissionNativeClaim.TryClaimAsync(
            dbContext, leaseOwner, batchSize, claimedAt, leaseDuration, cancellationToken);
        if (native is not null) return native;

        var strategy = dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            await using IAsyncDisposable claimLock =
                await RelationalNamedLock.AcquireTransactionAsync(
                    dbContext,
                    "registration-provider-submission-write-claim",
                    cancellationToken);

            List<RegistrationProviderSubmissionWriteEffect> rows = await dbContext.RegistrationProviderSubmissionWriteEffects
                .IgnoreTenantFilter(TenantFilterBypassReasons.RegistrationProviderSubmissionWriteWorkerCrossTenantQueue)
                .Where(value =>
                    ((value.Status == OutboxMessageStatus.Pending || value.Status == OutboxMessageStatus.Failed) &&
                     (value.NextAttemptAt == null || value.NextAttemptAt <= claimedAt)) ||
                    (value.Status == OutboxMessageStatus.Processing && value.ProcessingLeaseExpiresAt <= claimedAt))
                .OrderBy(value => value.NextAttemptAt ?? value.CreatedAt)
                .ThenBy(value => value.Id)
                .Take(batchSize)
                .ToListAsync(cancellationToken);
            List<RegistrationProviderSubmissionWriteClaim> claims = new(rows.Count);
            Guid[] effectIds = rows.Select(row => row.Id).ToArray();
            await FenceDeliveryArtifactsAsync(effectIds, cancellationToken);
            foreach (RegistrationProviderSubmissionWriteEffect row in rows)
            {
                if (row.Status == OutboxMessageStatus.Processing)
                {
                    row.RecoverExpiredClaim(claimedAt);
                }

                Guid leaseToken = Guid.CreateVersion7();
                row.Claim(leaseOwner, leaseToken, claimedAt.Add(leaseDuration), claimedAt);
                claims.Add(new(row.Id, row.TenantId, row.RegistrationSubmissionId, row.RegistrationAttemptId,
                    row.RegistrationProviderBindingId, leaseToken, row.ProcessingFence, row.AttemptCount,
                    row.ProcessingLeaseExpiresAt!.Value));
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return claims;
        });
    }

    public async Task<RegistrationProviderSubmissionWriteDelivery?> GetDeliveryAsync(
        RegistrationProviderSubmissionWriteClaim claim,
        CancellationToken cancellationToken)
    {
        var source = await GetDeliverySourceAsync(claim, cancellationToken);
        if (source is null)
            return null;

        DateTime observedAt = _timeProvider.GetUtcNow().UtcDateTime;
        var activeClaim = ActiveClaim(claim, observedAt);
        // The sensitive query itself requires live custody, not just an earlier graph lookup.
        IReadOnlyList<RegistrationAnswer> answers = await dbContext.RegistrationAnswers
            .AsNoTracking()
            .Include(value => value.SensitiveAnswerValue)
            .Where(value => value.TenantId == source.Submission.TenantId &&
                value.RegistrationSubmissionId == source.Submission.Id &&
                value.RegistrationOrderId == source.Order.Id && value.RegistrationAttemptId == source.Attempt.Id &&
                value.EventId == source.Submission.EventId &&
                value.RegistrationFormVersionId == source.Submission.RegistrationFormVersionId &&
                activeClaim.Any())
            .OrderBy(value => value.RegistrationFormFieldId)
            .ThenBy(value => value.Ordinal)
            .ToListAsync(cancellationToken);
        IReadOnlyList<RegistrationFormField> fields = await dbContext.RegistrationFormFields
            .AsNoTracking()
            .Include(value => value.Options)
            .Where(value => value.TenantId == source.Submission.TenantId &&
                value.RegistrationFormVersionId == source.Submission.RegistrationFormVersionId)
            .ToListAsync(cancellationToken);
        var delivery = source with { Answers = answers, Fields = fields };
        return await IsDeliveryCurrentAsync(claim, delivery, cancellationToken) ? delivery : null;
    }

    private async Task<RegistrationProviderSubmissionWriteDelivery?> GetDeliverySourceAsync(
        RegistrationProviderSubmissionWriteClaim claim,
        CancellationToken cancellationToken)
    {
        if (dbContext.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null)
            return null;

        RegistrationProviderSubmissionWriteEffect? effect = await ActiveClaim(claim, _timeProvider.GetUtcNow().UtcDateTime)
            .AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (effect is null)
            return null;

        RegistrationAttempt? attempt = await dbContext.RegistrationAttempts
            .AsNoTracking()
            .SingleOrDefaultAsync(value => value.TenantId == effect.TenantId && value.Id == effect.RegistrationAttemptId, cancellationToken);
        RegistrationSubmission? submission = await dbContext.RegistrationSubmissions
            .AsNoTracking()
            .SingleOrDefaultAsync(value => value.TenantId == effect.TenantId && value.Id == effect.RegistrationSubmissionId, cancellationToken);
        RegistrationProviderBinding? binding = await dbContext.RegistrationProviderBindings
            .AsNoTracking()
            .Include(value => value.Connection)
            .Include(value => value.FieldMappings)
            .Include(value => value.OptionMappings)
            .Include(value => value.Capabilities)
            .SingleOrDefaultAsync(value => value.TenantId == effect.TenantId && value.Id == effect.RegistrationProviderBindingId, cancellationToken);
        if (attempt is null || submission is null || binding?.Connection is null ||
            binding.Connection.TenantId != effect.TenantId ||
            submission.RegistrationOrderId != effect.RegistrationOrderId ||
            submission.EventId != effect.EventId ||
            submission.RegistrationAttemptId != attempt.Id ||
            submission.RegistrationOrderId != attempt.RegistrationOrderId ||
            submission.EventId != attempt.EventId ||
            attempt.RegistrationProviderBindingId != binding.Id ||
            submission.RegistrationProviderBindingId != binding.Id ||
            attempt.RegistrationFormId != binding.RegistrationFormId ||
            submission.RegistrationFormId != binding.RegistrationFormId ||
            attempt.RegistrationFormVersionId != binding.RegistrationFormVersionId ||
            submission.RegistrationFormVersionId != binding.RegistrationFormVersionId ||
            attempt.ProviderMappingRevisionHash?.Value != binding.PublishedMappingRevisionHash?.Value ||
            submission.ProviderMappingRevisionHash?.Value != binding.PublishedMappingRevisionHash?.Value)
        {
            return null;
        }

        RegistrationOrder? order = await dbContext.RegistrationOrders
            .AsNoTracking()
            .SingleOrDefaultAsync(value => value.TenantId == effect.TenantId &&
                value.Id == effect.RegistrationOrderId && value.EventId == effect.EventId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        return new(order, attempt, submission, binding, [], []);
    }

    public async Task<bool> IsDeliveryCurrentAsync(
        RegistrationProviderSubmissionWriteClaim claim,
        RegistrationProviderSubmissionWriteDelivery delivery,
        CancellationToken cancellationToken)
    {
        var source = await GetDeliverySourceAsync(claim, cancellationToken);
        if (source is null ||
            source.Order.Id != delivery.Order.Id || source.Order.EventId != delivery.Order.EventId ||
            source.Order.AnonymousPiiRetentionUntilUtc != delivery.Order.AnonymousPiiRetentionUntilUtc ||
            source.Attempt.Id != delivery.Attempt.Id || source.Submission.Id != delivery.Submission.Id ||
            source.Submission.RegistrationFormId != delivery.Submission.RegistrationFormId ||
            source.Submission.RegistrationFormVersionId != delivery.Submission.RegistrationFormVersionId ||
            source.Binding.Id != delivery.Binding.Id || source.Binding.Connection!.Id != delivery.Binding.Connection?.Id ||
            source.Binding.ConcurrencyStamp != delivery.Binding.ConcurrencyStamp ||
            source.Binding.Connection.ConcurrencyStamp != delivery.Binding.Connection.ConcurrencyStamp ||
            source.Binding.PublishedMappingRevisionHash?.Value != delivery.Binding.PublishedMappingRevisionHash?.Value)
            return false;

        // Cleanup or a source replacement during preparation must not disclose the retained copy.
        Guid[] answerIds = delivery.Answers.Select(answer => answer.Id).ToArray();
        var presentAnswers = await dbContext.RegistrationAnswers.AsNoTracking()
            .Where(value => value.TenantId == claim.TenantId && answerIds.Contains(value.Id) &&
                value.RegistrationSubmissionId == source.Submission.Id &&
                value.RegistrationOrderId == source.Order.Id && value.RegistrationAttemptId == source.Attempt.Id &&
                value.EventId == source.Submission.EventId &&
                value.RegistrationFormVersionId == source.Submission.RegistrationFormVersionId)
            .Select(value => new
            {
                value.Id,
                value.RegistrationFormFieldId,
                value.SensitiveAnswerValueId,
                value.RetentionUntil,
                LiveSensitiveId = value.SensitiveAnswerValue == null ? (Guid?)null : value.SensitiveAnswerValue.Id,
                SensitiveRetentionUntil = value.SensitiveAnswerValue == null ? (DateTime?)null : value.SensitiveAnswerValue.RetentionUntil
            }).ToListAsync(cancellationToken);
        return presentAnswers.Count == answerIds.Length &&
            delivery.Answers.All(answer => presentAnswers.Any(current => current.Id == answer.Id &&
                current.RegistrationFormFieldId == answer.RegistrationFormFieldId &&
                current.SensitiveAnswerValueId == answer.SensitiveAnswerValueId &&
                current.LiveSensitiveId == answer.SensitiveAnswerValue?.Id &&
                current.RetentionUntil == answer.RetentionUntil &&
                current.SensitiveRetentionUntil == answer.SensitiveAnswerValue?.RetentionUntil)) &&
            await ActiveClaim(claim, _timeProvider.GetUtcNow().UtcDateTime).AnyAsync(cancellationToken);
    }

    public async Task<bool> CompleteAsync(RegistrationProviderSubmissionWriteClaim claim, DateTime completedAt, CancellationToken cancellationToken) =>
        completedAt.Kind == DateTimeKind.Utc && await MutateFencedAsync(claim, async () => await ActiveClaim(claim, completedAt)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.Status, OutboxMessageStatus.Completed)
                .SetProperty(value => value.CompletedAt, completedAt)
                .SetProperty(value => value.FailureCode, (string?)null)
                .SetProperty(value => value.NextAttemptAt, (DateTime?)null)
                .SetProperty(value => value.ProcessingLeaseOwner, (string?)null)
                .SetProperty(value => value.ProcessingLeaseToken, (Guid?)null)
                .SetProperty(value => value.ProcessingLeaseExpiresAt, (DateTime?)null)
                .SetProperty(value => value.UpdatedAt, completedAt), cancellationToken) == 1, cancellationToken);

    public async Task<bool> RetryAsync(RegistrationProviderSubmissionWriteClaim claim, string failureCode, DateTime nextAttemptAt, DateTime failedAt, CancellationToken cancellationToken) =>
        failedAt.Kind == DateTimeKind.Utc && nextAttemptAt.Kind == DateTimeKind.Utc && nextAttemptAt > failedAt &&
        await MutateFencedAsync(claim, async () => await ActiveClaim(claim, failedAt).ExecuteUpdateAsync(setters => setters
            .SetProperty(value => value.Status, OutboxMessageStatus.Failed)
            .SetProperty(value => value.NextAttemptAt, nextAttemptAt)
            .SetProperty(value => value.FailureCode, failureCode)
            .SetProperty(value => value.ProcessingLeaseOwner, (string?)null)
            .SetProperty(value => value.ProcessingLeaseToken, (Guid?)null)
            .SetProperty(value => value.ProcessingLeaseExpiresAt, (DateTime?)null)
            .SetProperty(value => value.UpdatedAt, failedAt), cancellationToken) == 1, cancellationToken);

    public async Task<bool> DeadLetterAsync(RegistrationProviderSubmissionWriteClaim claim, string failureCode, DateTime deadLetteredAt, CancellationToken cancellationToken) =>
        deadLetteredAt.Kind == DateTimeKind.Utc && await MutateFencedAsync(claim, async () => await ActiveClaim(claim, deadLetteredAt)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.Status, OutboxMessageStatus.DeadLettered)
                .SetProperty(value => value.DeadLetteredAt, deadLetteredAt)
                .SetProperty(value => value.FailureCode, failureCode)
                .SetProperty(value => value.NextAttemptAt, (DateTime?)null)
                .SetProperty(value => value.ProcessingLeaseOwner, (string?)null)
                .SetProperty(value => value.ProcessingLeaseToken, (Guid?)null)
                .SetProperty(value => value.ProcessingLeaseExpiresAt, (DateTime?)null)
                .SetProperty(value => value.UpdatedAt, deadLetteredAt), cancellationToken) == 1, cancellationToken);

    public async Task<bool> ParkAmbiguousAsync(RegistrationProviderSubmissionWriteClaim claim, string failureCode, DateTime parkedAt, CancellationToken cancellationToken) =>
        parkedAt.Kind == DateTimeKind.Utc && await MutateFencedAsync(claim, async () => await ActiveClaim(claim, parkedAt)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.Status, OutboxMessageStatus.DeadLettered)
                .SetProperty(value => value.ParkedAt, parkedAt)
                .SetProperty(value => value.FailureCode, failureCode)
                .SetProperty(value => value.NextAttemptAt, (DateTime?)null)
                .SetProperty(value => value.ProcessingLeaseOwner, (string?)null)
                .SetProperty(value => value.ProcessingLeaseToken, (Guid?)null)
                .SetProperty(value => value.ProcessingLeaseExpiresAt, (DateTime?)null)
                .SetProperty(value => value.UpdatedAt, parkedAt), cancellationToken) == 1, cancellationToken);

    private async Task<bool> MutateFencedAsync(
        RegistrationProviderSubmissionWriteClaim claim, Func<Task<bool>> mutate, CancellationToken cancellationToken)
    {
        if (dbContext.Database.CurrentTransaction is not null)
        {
            await FenceDeliveryArtifactsAsync([claim.EffectId], cancellationToken, claim.TenantId);
            return await mutate();
        }

        return await dbContext.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            await FenceDeliveryArtifactsAsync([claim.EffectId], cancellationToken, claim.TenantId);
            bool changed = await mutate();
            await transaction.CommitAsync(cancellationToken);
            return changed;
        });
    }

    private async Task FenceDeliveryArtifactsAsync(
        Guid[] effectIds, CancellationToken cancellationToken, Guid? tenantId = null)
    {
        // Resolve lineage from persisted effects, never from a caller's claim
        // submission ID. Settlements release or preserve indirect CSV holds.
        Guid[] storageIds = await (from effect in dbContext.RegistrationProviderSubmissionWriteEffects.AsNoTracking()
                .IgnoreTenantFilter(TenantFilterBypassReasons.RegistrationProviderSubmissionWriteWorkerCrossTenantQueue)
                                   join storage in dbContext.StorageObjects.AsNoTracking()
                                       .IgnoreQueryFilters([QueryFilterNames.Tenant, QueryFilterNames.SoftDelete])
                                       on new { effect.TenantId, Id = (Guid?)effect.RegistrationSubmissionId }
                                       equals new { storage.TenantId, Id = storage.OwningResourceId }
                                   where effectIds.Contains(effect.Id) && (tenantId == null || effect.TenantId == tenantId)
                                       && storage.OwningResourceKind == "registration_submission_sink"
                                   select storage.Id).Distinct().ToArrayAsync(cancellationToken);
        await new StorageObjectReferenceRepository(dbContext).FenceAsync(storageIds, cancellationToken);
    }

    private IQueryable<RegistrationProviderSubmissionWriteEffect> ActiveClaim(
        RegistrationProviderSubmissionWriteClaim claim,
        DateTime observedAt)
    {
        bool selectedTenant = claim.TenantId != Guid.Empty && claim.LeaseToken != Guid.Empty &&
            claim.ProcessingFence > 0 && claim.LeaseExpiresAt.Kind == DateTimeKind.Utc &&
            !dbContext.IsTenantFilterBypassed && dbContext.BindTenantLease() == claim.TenantId;
        return dbContext.RegistrationProviderSubmissionWriteEffects
        .Where(value => selectedTenant && value.Id == claim.EffectId &&
            value.TenantId == claim.TenantId &&
            value.RegistrationSubmissionId == claim.RegistrationSubmissionId &&
            value.RegistrationAttemptId == claim.RegistrationAttemptId &&
            value.RegistrationProviderBindingId == claim.RegistrationProviderBindingId &&
            value.Status == OutboxMessageStatus.Processing &&
            value.ProcessingLeaseToken == claim.LeaseToken &&
            value.ProcessingFence == claim.ProcessingFence &&
            value.AttemptCount == claim.AttemptCount &&
            value.ProcessingLeaseExpiresAt == claim.LeaseExpiresAt &&
            value.ProcessingLeaseExpiresAt > observedAt);
    }
}
