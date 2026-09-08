// ABOUTME: Claims and settles outbound provider-submission write effects with database fencing.
// ABOUTME: Loads the post-claim delivery graph without persisting provider payloads or raw answers in the effect row.

using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class RegistrationProviderSubmissionWriteEffectRepository(ExploreDbContext dbContext)
    : IRegistrationProviderSubmissionWriteEffectRepository
{
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
            foreach (RegistrationProviderSubmissionWriteEffect row in rows)
            {
                if (row.Status == OutboxMessageStatus.Processing)
                {
                    row.RecoverExpiredClaim(claimedAt);
                }

                Guid leaseToken = Guid.CreateVersion7();
                row.Claim(leaseOwner, leaseToken, claimedAt.Add(leaseDuration), claimedAt);
                claims.Add(new(row.Id, row.TenantId, row.RegistrationSubmissionId, row.RegistrationAttemptId,
                    row.RegistrationProviderBindingId, leaseToken, row.ProcessingFence, row.AttemptCount));
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
        RegistrationAttempt? attempt = await dbContext.RegistrationAttempts
            .AsNoTracking()
            .IgnoreTenantFilter(TenantFilterBypassReasons.RegistrationProviderSubmissionWriteWorkerCrossTenantQueue)
            .SingleOrDefaultAsync(value => value.TenantId == claim.TenantId && value.Id == claim.RegistrationAttemptId, cancellationToken);
        RegistrationSubmission? submission = await dbContext.RegistrationSubmissions
            .AsNoTracking()
            .IgnoreTenantFilter(TenantFilterBypassReasons.RegistrationProviderSubmissionWriteWorkerCrossTenantQueue)
            .SingleOrDefaultAsync(value => value.TenantId == claim.TenantId && value.Id == claim.RegistrationSubmissionId, cancellationToken);
        RegistrationProviderBinding? binding = await dbContext.RegistrationProviderBindings
            .AsNoTracking()
            .IgnoreTenantFilter(TenantFilterBypassReasons.RegistrationProviderSubmissionWriteWorkerCrossTenantQueue)
            .Include(value => value.Connection)
            .Include(value => value.FieldMappings)
            .Include(value => value.OptionMappings)
            .Include(value => value.Capabilities)
            .SingleOrDefaultAsync(value => value.TenantId == claim.TenantId && value.Id == claim.RegistrationProviderBindingId, cancellationToken);
        if (attempt is null || submission is null || binding?.Connection is null ||
            submission.RegistrationAttemptId != attempt.Id ||
            submission.RegistrationOrderId != attempt.RegistrationOrderId ||
            submission.EventId != attempt.EventId ||
            attempt.RegistrationProviderBindingId != binding.Id ||
            submission.RegistrationProviderBindingId != binding.Id ||
            attempt.RegistrationFormVersionId != binding.RegistrationFormVersionId ||
            submission.RegistrationFormVersionId != binding.RegistrationFormVersionId ||
            attempt.ProviderMappingRevisionHash?.Value != binding.PublishedMappingRevisionHash?.Value ||
            submission.ProviderMappingRevisionHash?.Value != binding.PublishedMappingRevisionHash?.Value)
        {
            return null;
        }

        RegistrationOrder? order = await dbContext.RegistrationOrders
            .AsNoTracking()
            .IgnoreTenantFilter(TenantFilterBypassReasons.RegistrationProviderSubmissionWriteWorkerCrossTenantQueue)
            .SingleOrDefaultAsync(value => value.TenantId == claim.TenantId &&
                value.Id == submission.RegistrationOrderId && value.EventId == submission.EventId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        IReadOnlyList<RegistrationAnswer> answers = await dbContext.RegistrationAnswers
            .AsNoTracking()
            .IgnoreTenantFilter(TenantFilterBypassReasons.RegistrationProviderSubmissionWriteWorkerCrossTenantQueue)
            .Include(value => value.SensitiveAnswerValue)
            .Where(value => value.TenantId == claim.TenantId && value.RegistrationSubmissionId == submission.Id &&
                value.RegistrationOrderId == order.Id && value.RegistrationAttemptId == attempt.Id)
            .OrderBy(value => value.RegistrationFormFieldId)
            .ThenBy(value => value.Ordinal)
            .ToListAsync(cancellationToken);
        IReadOnlyList<RegistrationFormField> fields = await dbContext.RegistrationFormFields
            .AsNoTracking()
            .IgnoreTenantFilter(TenantFilterBypassReasons.RegistrationProviderSubmissionWriteWorkerCrossTenantQueue)
            .Include(value => value.Options)
            .Where(value => value.TenantId == claim.TenantId && value.RegistrationFormVersionId == submission.RegistrationFormVersionId)
            .ToListAsync(cancellationToken);
        return new(order, attempt, submission, binding, answers, fields);
    }

    public async Task<bool> CompleteAsync(RegistrationProviderSubmissionWriteClaim claim, DateTime completedAt, CancellationToken cancellationToken) =>
        completedAt.Kind == DateTimeKind.Utc && await ActiveClaim(claim, completedAt)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.Status, OutboxMessageStatus.Completed)
                .SetProperty(value => value.CompletedAt, completedAt)
                .SetProperty(value => value.FailureCode, (string?)null)
                .SetProperty(value => value.NextAttemptAt, (DateTime?)null)
                .SetProperty(value => value.ProcessingLeaseOwner, (string?)null)
                .SetProperty(value => value.ProcessingLeaseToken, (Guid?)null)
                .SetProperty(value => value.ProcessingLeaseExpiresAt, (DateTime?)null)
                .SetProperty(value => value.UpdatedAt, completedAt), cancellationToken) == 1;

    public async Task<bool> RetryAsync(RegistrationProviderSubmissionWriteClaim claim, string failureCode, DateTime nextAttemptAt, DateTime failedAt, CancellationToken cancellationToken) =>
        failedAt.Kind == DateTimeKind.Utc && nextAttemptAt.Kind == DateTimeKind.Utc && nextAttemptAt > failedAt &&
        await ActiveClaim(claim, failedAt).ExecuteUpdateAsync(setters => setters
            .SetProperty(value => value.Status, OutboxMessageStatus.Failed)
            .SetProperty(value => value.NextAttemptAt, nextAttemptAt)
            .SetProperty(value => value.FailureCode, failureCode)
            .SetProperty(value => value.ProcessingLeaseOwner, (string?)null)
            .SetProperty(value => value.ProcessingLeaseToken, (Guid?)null)
            .SetProperty(value => value.ProcessingLeaseExpiresAt, (DateTime?)null)
            .SetProperty(value => value.UpdatedAt, failedAt), cancellationToken) == 1;

    public async Task<bool> DeadLetterAsync(RegistrationProviderSubmissionWriteClaim claim, string failureCode, DateTime deadLetteredAt, CancellationToken cancellationToken) =>
        deadLetteredAt.Kind == DateTimeKind.Utc && await ActiveClaim(claim, deadLetteredAt)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.Status, OutboxMessageStatus.DeadLettered)
                .SetProperty(value => value.DeadLetteredAt, deadLetteredAt)
                .SetProperty(value => value.FailureCode, failureCode)
                .SetProperty(value => value.NextAttemptAt, (DateTime?)null)
                .SetProperty(value => value.ProcessingLeaseOwner, (string?)null)
                .SetProperty(value => value.ProcessingLeaseToken, (Guid?)null)
                .SetProperty(value => value.ProcessingLeaseExpiresAt, (DateTime?)null)
                .SetProperty(value => value.UpdatedAt, deadLetteredAt), cancellationToken) == 1;

    public async Task<bool> ParkAmbiguousAsync(RegistrationProviderSubmissionWriteClaim claim, string failureCode, DateTime parkedAt, CancellationToken cancellationToken) =>
        parkedAt.Kind == DateTimeKind.Utc && await ActiveClaim(claim, parkedAt)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.Status, OutboxMessageStatus.DeadLettered)
                .SetProperty(value => value.ParkedAt, parkedAt)
                .SetProperty(value => value.FailureCode, failureCode)
                .SetProperty(value => value.NextAttemptAt, (DateTime?)null)
                .SetProperty(value => value.ProcessingLeaseOwner, (string?)null)
                .SetProperty(value => value.ProcessingLeaseToken, (Guid?)null)
                .SetProperty(value => value.ProcessingLeaseExpiresAt, (DateTime?)null)
                .SetProperty(value => value.UpdatedAt, parkedAt), cancellationToken) == 1;

    private IQueryable<RegistrationProviderSubmissionWriteEffect> ActiveClaim(
        RegistrationProviderSubmissionWriteClaim claim,
        DateTime observedAt) => dbContext.RegistrationProviderSubmissionWriteEffects
        .IgnoreTenantFilter(TenantFilterBypassReasons.RegistrationProviderSubmissionWriteWorkerCrossTenantQueue)
        .Where(value => value.Id == claim.EffectId &&
            value.TenantId == claim.TenantId &&
            value.Status == OutboxMessageStatus.Processing &&
            value.ProcessingLeaseToken == claim.LeaseToken &&
            value.ProcessingFence == claim.ProcessingFence &&
            value.ProcessingLeaseExpiresAt > observedAt);
}
