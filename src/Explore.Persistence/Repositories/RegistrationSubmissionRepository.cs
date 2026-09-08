using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class RegistrationSubmissionRepository(ExploreDbContext dbContext)
    : IRegistrationSubmissionRepository
{
    public async Task PersistAttemptAsync(
        RegistrationAttempt attempt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        await dbContext.RegistrationAttempts.AddAsync(attempt, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> PersistReplacementAttemptAsync(
        RegistrationAttempt attempt,
        Guid supersededAttemptId,
        string supersessionReason,
        DateTime supersededAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        if (supersededAttemptId == Guid.Empty)
        {
            throw new ArgumentException("Superseded attempt identity is required.", nameof(supersededAttemptId));
        }

        var strategy = dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            RegistrationAttempt? superseded = await dbContext.RegistrationAttempts.SingleOrDefaultAsync(candidate =>
                candidate.TenantId == attempt.TenantId &&
                candidate.Id == supersededAttemptId &&
                candidate.EventId == attempt.EventId &&
                candidate.RegistrationOrderId == attempt.RegistrationOrderId &&
                candidate.RegistrationWorkflowId == attempt.RegistrationWorkflowId &&
                candidate.RegistrationRequirementId == attempt.RegistrationRequirementId &&
                candidate.StatusId == (int)RegistrationAttemptStatusEnum.Active,
                cancellationToken);
            if (superseded is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }

            superseded.Supersede(attempt.Id, supersededAt, supersessionReason);
            await dbContext.RegistrationAttempts.AddAsync(attempt, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        });
    }

    public Task<RegistrationSubmissionPersistenceResult> PersistAcceptedAsync(
        RegistrationAttempt attempt,
        RegistrationSubmission submission,
        Guid expectedAttemptConcurrencyStamp,
        CancellationToken cancellationToken) => PersistAcceptedWithNormalizationAsync(
        attempt, submission, expectedAttemptConcurrencyStamp, [], [], [], [], cancellationToken);

    public async Task<RegistrationSubmissionPersistenceResult> PersistAcceptedWithNormalizationAsync(
        RegistrationAttempt attempt,
        RegistrationSubmission submission,
        Guid expectedAttemptConcurrencyStamp,
        IReadOnlyCollection<RegistrationAnswer> answers,
        IReadOnlyCollection<RegistrationConsentRecord> consentRecords,
        IReadOnlyCollection<RegistrationSubmissionIssue> issues,
        IReadOnlyCollection<RegistrationRequirementFulfillment> fulfillments,
        CancellationToken cancellationToken,
        RegistrationProviderSubmissionWriteEffect? providerWriteEffect = null)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentNullException.ThrowIfNull(answers);
        ArgumentNullException.ThrowIfNull(consentRecords);
        ArgumentNullException.ThrowIfNull(issues);
        ArgumentNullException.ThrowIfNull(fulfillments);
        if (expectedAttemptConcurrencyStamp == Guid.Empty ||
            submission.AttemptConsumptionClaimId is null ||
            submission.AttemptConsumptionClaimId != attempt.SubmissionConsumptionClaimId ||
            !submission.IsFinalizable ||
            submission.StatusId != (int)RegistrationSubmissionStatusEnum.Received)
        {
            throw new ArgumentException("Accepted submission state is incomplete.", nameof(submission));
        }

        ValidateNormalizationGraph(submission, answers, consentRecords, issues);
        if (fulfillments.Any(fulfillment =>
                fulfillment.TenantId != submission.TenantId ||
                fulfillment.EventId != submission.EventId ||
                fulfillment.RegistrationOrderId != submission.RegistrationOrderId ||
                fulfillment.RegistrationWorkflowId != submission.RegistrationWorkflowId ||
                fulfillment.RegistrationRequirementId != submission.RegistrationRequirementId ||
                fulfillment.SourceRegistrationSubmissionId != submission.Id ||
                fulfillment.IsSkipped))
        {
            throw new ArgumentException("Fulfillment graph must belong to the accepted submission.", nameof(fulfillments));
        }

        RegistrationSubmission? existing = await FindExistingAsync(submission, cancellationToken);
        if (existing is not null)
        {
            return ClassifyAcceptedReplay(submission, existing);
        }

        var strategy = dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync<RegistrationSubmissionPersistenceResult>(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                int claimed = await dbContext.RegistrationAttempts
                    .Where(candidate =>
                        candidate.TenantId == attempt.TenantId &&
                        candidate.Id == attempt.Id &&
                        candidate.StatusId == (int)RegistrationAttemptStatusEnum.Active &&
                        candidate.ConcurrencyStamp == expectedAttemptConcurrencyStamp &&
                        candidate.ExpiresAt > submission.ReceivedAt &&
                        candidate.SupersededAt == null &&
                        candidate.SubmissionConsumptionClaimId == null)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(candidate => candidate.StatusId, attempt.StatusId)
                        .SetProperty(candidate => candidate.ConsumedAt, attempt.ConsumedAt)
                        .SetProperty(candidate => candidate.SubmissionConsumptionClaimId, attempt.SubmissionConsumptionClaimId)
                        .SetProperty(candidate => candidate.ConcurrencyStamp, attempt.ConcurrencyStamp)
                        .SetProperty(candidate => candidate.UpdatedAt, submission.ReceivedAt), cancellationToken);

                if (claimed == 0)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    dbContext.ChangeTracker.Clear();
                    RegistrationSubmission? raced = await FindExistingAsync(submission, cancellationToken);
                    return raced is null
                        ? new(RegistrationSubmissionPersistenceOutcome.AttemptUnavailable, null)
                        : ClassifyAcceptedReplay(submission, raced);
                }

                await dbContext.RegistrationSubmissions.AddAsync(submission, cancellationToken);
                if (answers.Count > 0)
                {
                    await dbContext.RegistrationAnswers.AddRangeAsync(answers, cancellationToken);
                }

                if (consentRecords.Count > 0)
                {
                    await dbContext.RegistrationConsentRecords.AddRangeAsync(consentRecords, cancellationToken);
                }

                if (issues.Count > 0)
                {
                    await dbContext.RegistrationSubmissionIssues.AddRangeAsync(issues, cancellationToken);
                }

                if (fulfillments.Count > 0)
                {
                    await dbContext.RegistrationRequirementFulfillments.AddRangeAsync(fulfillments, cancellationToken);
                }

                if (providerWriteEffect is not null)
                {
                    await dbContext.RegistrationProviderSubmissionWriteEffects.AddAsync(providerWriteEffect, cancellationToken);
                }

                await dbContext.SaveChangesAsync(cancellationToken);
                bool ready = fulfillments.Count > 0 &&
                    await RegistrationFinalizationRepository.AreMandatoryRequirementsFulfilledCoreAsync(
                        dbContext, submission.TenantId, submission.RegistrationOrderId, cancellationToken);
                if (ready && !await dbContext.RegistrationFinalizationEffects.AnyAsync(value =>
                        value.TenantId == submission.TenantId &&
                        value.RegistrationOrderId == submission.RegistrationOrderId,
                        cancellationToken))
                {
                    RegistrationOrder order = await dbContext.RegistrationOrders.SingleAsync(value =>
                        value.TenantId == submission.TenantId && value.Id == submission.RegistrationOrderId,
                        cancellationToken);
                    await dbContext.RegistrationFinalizationEffects.AddAsync(
                        RegistrationFinalizationEffect.Create(order, submission.ReceivedAt), cancellationToken);
                    await dbContext.SaveChangesAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
                return new(RegistrationSubmissionPersistenceOutcome.Inserted, submission);
            }
            catch (DbUpdateException exception) when (IsSubmissionIdentityUniqueViolation(dbContext, exception))
            {
                await transaction.RollbackAsync(cancellationToken);
                dbContext.ChangeTracker.Clear();
                RegistrationSubmission? raced = await FindExistingAsync(submission, cancellationToken);
                return raced is null
                    ? new(RegistrationSubmissionPersistenceOutcome.AttemptUnavailable, null)
                    : ClassifyAcceptedReplay(submission, raced);
            }
        });
    }

    public async Task<RegistrationSubmissionPersistenceResult> PersistEvidenceOnlyAsync(
        RegistrationSubmission submission,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(submission);
        if (submission.StatusId != (int)RegistrationSubmissionStatusEnum.EvidenceOnly || submission.IsFinalizable)
        {
            throw new ArgumentException("Only non-finalizable evidence can use this persistence path.", nameof(submission));
        }

        RegistrationSubmission? existing = await FindExistingAsync(submission, cancellationToken);
        if (existing is not null)
        {
            return new(RegistrationSubmissionPersistenceOutcome.EvidenceOnlyConflict, existing);
        }

        try
        {
            await dbContext.RegistrationSubmissions.AddAsync(submission, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            return new(RegistrationSubmissionPersistenceOutcome.Inserted, submission);
        }
        catch (DbUpdateException exception) when (IsSubmissionIdentityUniqueViolation(dbContext, exception))
        {
            dbContext.ChangeTracker.Clear();
            RegistrationSubmission? raced = await FindExistingAsync(submission, cancellationToken);
            return raced is null
                ? new(RegistrationSubmissionPersistenceOutcome.AttemptUnavailable, null)
                : new(RegistrationSubmissionPersistenceOutcome.EvidenceOnlyConflict, raced);
        }
    }

    public Task<RegistrationAttempt?> GetAttemptAsync(
        Guid tenantId,
        Guid attemptId,
        CancellationToken cancellationToken) => dbContext.RegistrationAttempts
        .AsNoTracking()
        .SingleOrDefaultAsync(attempt => attempt.TenantId == tenantId && attempt.Id == attemptId, cancellationToken);

    public Task<RegistrationSubmission?> GetSubmissionAsync(
        Guid tenantId,
        Guid submissionId,
        CancellationToken cancellationToken) => dbContext.RegistrationSubmissions
        .AsNoTracking()
        .Include(submission => submission.Revisions)
        .SingleOrDefaultAsync(submission => submission.TenantId == tenantId && submission.Id == submissionId, cancellationToken);

    public Task<RegistrationRequirement?> GetRequirementAsync(
        Guid tenantId,
        Guid requirementId,
        CancellationToken cancellationToken) => dbContext.RegistrationRequirements
        .AsNoTracking()
        .SingleOrDefaultAsync(requirement => requirement.TenantId == tenantId && requirement.Id == requirementId, cancellationToken);

    public async Task PersistNormalizationAsync(
        IReadOnlyCollection<RegistrationAnswer> answers,
        IReadOnlyCollection<RegistrationConsentRecord> consentRecords,
        IReadOnlyCollection<RegistrationSubmissionIssue> issues,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (answers.Count > 0)
        {
            await dbContext.RegistrationAnswers.AddRangeAsync(answers, cancellationToken);
        }

        if (consentRecords.Count > 0)
        {
            await dbContext.RegistrationConsentRecords.AddRangeAsync(consentRecords, cancellationToken);
        }

        if (issues.Count > 0)
        {
            await dbContext.RegistrationSubmissionIssues.AddRangeAsync(issues, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<bool> PersistRevisionAsync(
        RegistrationSubmission submission,
        RegistrationSubmissionRevision revision,
        Guid expectedSubmissionConcurrencyStamp,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentNullException.ThrowIfNull(revision);
        var strategy = dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            bool? committedRevision = await ClassifyCommittedRevisionAsync(revision, cancellationToken);
            if (committedRevision.HasValue)
            {
                return committedRevision.Value;
            }

            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            int updated = await dbContext.RegistrationSubmissions
                .Where(candidate =>
                    candidate.TenantId == submission.TenantId &&
                    candidate.Id == submission.Id &&
                    candidate.StatusId == (int)RegistrationSubmissionStatusEnum.Received &&
                    candidate.ConcurrencyStamp == expectedSubmissionConcurrencyStamp)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(candidate => candidate.ConcurrencyStamp, submission.ConcurrencyStamp)
                    .SetProperty(candidate => candidate.UpdatedAt, revision.ReceivedAt), cancellationToken);
            if (updated == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                dbContext.ChangeTracker.Clear();
                return await ClassifyCommittedRevisionAsync(revision, cancellationToken) == true;
            }

            try
            {
                await dbContext.RegistrationSubmissionRevisions.AddAsync(revision, cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return true;
            }
            catch (DbUpdateException exception) when (IsRevisionIdentityUniqueViolation(dbContext, exception))
            {
                await transaction.RollbackAsync(cancellationToken);
                dbContext.ChangeTracker.Clear();
                return await ClassifyCommittedRevisionAsync(revision, cancellationToken) == true;
            }
        });
    }

    public async Task<bool> PersistFinalizationAsync(
        RegistrationSubmission submission,
        Guid expectedSubmissionConcurrencyStamp,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(submission);
        if (submission.StatusId != (int)RegistrationSubmissionStatusEnum.Finalized || submission.FinalizedAt is null)
        {
            throw new ArgumentException("Submission must be finalized before persistence.", nameof(submission));
        }

        int updated = await dbContext.RegistrationSubmissions
            .Where(candidate =>
                candidate.TenantId == submission.TenantId &&
                candidate.Id == submission.Id &&
                candidate.StatusId == (int)RegistrationSubmissionStatusEnum.Received &&
                candidate.AttemptConsumptionClaimId == submission.AttemptConsumptionClaimId &&
                candidate.ConcurrencyStamp == expectedSubmissionConcurrencyStamp)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(candidate => candidate.StatusId, submission.StatusId)
                .SetProperty(candidate => candidate.FinalizedAt, submission.FinalizedAt)
                .SetProperty(candidate => candidate.ConcurrencyStamp, submission.ConcurrencyStamp)
                .SetProperty(candidate => candidate.UpdatedAt, submission.FinalizedAt), cancellationToken);
        return updated == 1;
    }

    private Task<RegistrationSubmission?> FindExistingAsync(
        RegistrationSubmission submission,
        CancellationToken cancellationToken)
    {
        IQueryable<RegistrationSubmission> query = dbContext.RegistrationSubmissions
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .AsNoTracking()
            .Where(candidate => candidate.TenantId == submission.TenantId);
        query = submission.RegistrationProviderBindingId is null
            ? query.Where(candidate =>
                candidate.RegistrationProviderBindingId == null &&
                candidate.RegistrationAttemptId == submission.RegistrationAttemptId &&
                candidate.BusinessDeduplicationKey == submission.BusinessDeduplicationKey)
            : query.Where(candidate =>
                candidate.RegistrationProviderBindingId == submission.RegistrationProviderBindingId &&
                candidate.ProviderSubmissionId == submission.ProviderSubmissionId &&
                candidate.ProviderResponseRevision == submission.ProviderResponseRevision);
        return query.SingleOrDefaultAsync(cancellationToken);
    }

    private static void ValidateNormalizationGraph(
        RegistrationSubmission submission,
        IReadOnlyCollection<RegistrationAnswer> answers,
        IReadOnlyCollection<RegistrationConsentRecord> consentRecords,
        IReadOnlyCollection<RegistrationSubmissionIssue> issues)
    {
        bool mismatched = answers.Any(answer => answer.TenantId != submission.TenantId ||
                                                answer.RegistrationSubmissionId != submission.Id) ||
                          consentRecords.Any(record => record.TenantId != submission.TenantId ||
                                                       record.RegistrationSubmissionId != submission.Id) ||
                          issues.Any(issue => issue.TenantId != submission.TenantId ||
                                              issue.RegistrationSubmissionId != submission.Id);
        if (mismatched)
        {
            throw new ArgumentException("Normalization evidence must belong to the accepted submission.");
        }
    }

    private Task<bool?> ClassifyCommittedRevisionAsync(
        RegistrationSubmissionRevision revision,
        CancellationToken cancellationToken) => dbContext.RegistrationSubmissionRevisions
        .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
        .AsNoTracking()
        .Where(candidate =>
            candidate.TenantId == revision.TenantId &&
            candidate.RegistrationSubmissionId == revision.RegistrationSubmissionId &&
            candidate.RevisionNumber == revision.RevisionNumber)
        .Select(candidate => (bool?)(
            !candidate.IsDeleted &&
            candidate.Id == revision.Id &&
            candidate.EventId == revision.EventId &&
            candidate.ReceivedEvidenceHash == revision.ReceivedEvidenceHash &&
            candidate.ProviderRevisionId == revision.ProviderRevisionId &&
            candidate.ReceivedAt == revision.ReceivedAt &&
            candidate.CreatedAt == revision.CreatedAt))
        .SingleOrDefaultAsync(cancellationToken);

    private static RegistrationSubmissionPersistenceResult ClassifyAcceptedReplay(
        RegistrationSubmission requested,
        RegistrationSubmission existing) =>
        !existing.IsDeleted &&
        existing.IsFinalizable &&
        existing.StatusId is (int)RegistrationSubmissionStatusEnum.Received or (int)RegistrationSubmissionStatusEnum.Finalized &&
        existing.AttemptConsumptionClaimId == requested.AttemptConsumptionClaimId
            ? new(RegistrationSubmissionPersistenceOutcome.Existing, existing)
            : new(RegistrationSubmissionPersistenceOutcome.EvidenceOnlyConflict, existing);

    internal static bool IsSubmissionIdentityUniqueViolation(
        ExploreDbContext context,
        DbUpdateException exception) =>
        RegistrationUniqueConflictClassifier.IsSubmissionIdentityConflict(context, exception);

    internal static bool IsRevisionIdentityUniqueViolation(
        ExploreDbContext context,
        DbUpdateException exception) =>
        RegistrationUniqueConflictClassifier.IsRevisionIdentityConflict(context, exception);
}
