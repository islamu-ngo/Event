using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class RegistrationFinalizationRepository(ExploreDbContext dbContext)
    : IRegistrationFinalizationRepository
{
    public async Task<IReadOnlyList<RegistrationRequirementFulfillment>> GetFulfillmentsAsync(
        Guid tenantId,
        Guid registrationOrderId,
        Guid registrationRequirementId,
        CancellationToken cancellationToken) => await dbContext.RegistrationRequirementFulfillments
        .AsNoTracking()
        .Where(value => value.TenantId == tenantId &&
            value.RegistrationOrderId == registrationOrderId &&
            value.RegistrationRequirementId == registrationRequirementId)
        .OrderBy(value => value.SubjectTypeId)
        .ThenBy(value => value.SubjectId)
        .ToListAsync(cancellationToken);

    public async Task<bool> RecordFulfillmentAsync(
        RegistrationRequirementFulfillment fulfillment,
        DateTime recordedAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fulfillment);
        var strategy = dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            await using IAsyncDisposable finalizationLock =
                await RelationalNamedLock.AcquireTransactionAsync(
                    dbContext,
                    $"registration-finalization:{fulfillment.TenantId:D}:{fulfillment.RegistrationOrderId:D}",
                    cancellationToken);

            bool exists = await dbContext.RegistrationRequirementFulfillments.AnyAsync(value =>
                value.TenantId == fulfillment.TenantId &&
                value.RegistrationOrderId == fulfillment.RegistrationOrderId &&
                value.RegistrationRequirementId == fulfillment.RegistrationRequirementId &&
                value.SubjectTypeId == fulfillment.SubjectTypeId &&
                value.SubjectId == fulfillment.SubjectId &&
                value.IsSkipped == fulfillment.IsSkipped,
                cancellationToken);
            if (!exists)
            {
                await dbContext.RegistrationRequirementFulfillments.AddAsync(fulfillment, cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            bool ready = await AreMandatoryRequirementsFulfilledCoreAsync(
                dbContext, fulfillment.TenantId, fulfillment.RegistrationOrderId, cancellationToken);
            if (ready && !await dbContext.RegistrationFinalizationEffects.AnyAsync(value =>
                    value.TenantId == fulfillment.TenantId &&
                    value.RegistrationOrderId == fulfillment.RegistrationOrderId,
                    cancellationToken))
            {
                RegistrationOrder order = await dbContext.RegistrationOrders.SingleAsync(value =>
                    value.TenantId == fulfillment.TenantId && value.Id == fulfillment.RegistrationOrderId,
                    cancellationToken);
                await dbContext.RegistrationFinalizationEffects.AddAsync(
                    RegistrationFinalizationEffect.Create(order, recordedAt), cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return ready;
        });
    }

    public async Task<bool> TryRecordSkippedFulfillmentsAndConsumeAttemptAsync(
        RegistrationAttempt attempt,
        Guid expectedAttemptConcurrencyStamp,
        IReadOnlyCollection<RegistrationRequirementFulfillment> fulfillments,
        DateTime recordedAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentNullException.ThrowIfNull(fulfillments);
        if (expectedAttemptConcurrencyStamp == Guid.Empty || fulfillments.Count == 0 ||
            fulfillments.Any(value => !value.IsSkipped || value.TenantId != attempt.TenantId ||
                value.EventId != attempt.EventId || value.RegistrationOrderId != attempt.RegistrationOrderId ||
                value.RegistrationWorkflowId != attempt.RegistrationWorkflowId ||
                value.RegistrationRequirementId != attempt.RegistrationRequirementId))
        {
            throw new ArgumentException("Skipped fulfillment state must match the consumed attempt.", nameof(fulfillments));
        }

        var strategy = dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            await using IAsyncDisposable finalizationLock =
                await RelationalNamedLock.AcquireTransactionAsync(
                    dbContext,
                    $"registration-finalization:{attempt.TenantId:D}:{attempt.RegistrationOrderId:D}",
                    cancellationToken);

            RegistrationAttempt? trackedAttempt = await dbContext.RegistrationAttempts.SingleOrDefaultAsync(value =>
                value.TenantId == attempt.TenantId && value.Id == attempt.Id,
                cancellationToken);
            if (trackedAttempt is null || trackedAttempt.ConcurrencyStamp != expectedAttemptConcurrencyStamp)
            {
                await transaction.RollbackAsync(cancellationToken);
                dbContext.ChangeTracker.Clear();
                return false;
            }

            trackedAttempt.Consume(recordedAt);
            foreach (RegistrationRequirementFulfillment fulfillment in fulfillments)
            {
                bool exists = await dbContext.RegistrationRequirementFulfillments.AnyAsync(value =>
                    value.TenantId == fulfillment.TenantId &&
                    value.RegistrationOrderId == fulfillment.RegistrationOrderId &&
                    value.RegistrationRequirementId == fulfillment.RegistrationRequirementId &&
                    value.SubjectTypeId == fulfillment.SubjectTypeId &&
                    value.SubjectId == fulfillment.SubjectId &&
                    value.IsSkipped,
                    cancellationToken);
                if (!exists)
                {
                    await dbContext.RegistrationRequirementFulfillments.AddAsync(fulfillment, cancellationToken);
                }
            }

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                await transaction.RollbackAsync(cancellationToken);
                dbContext.ChangeTracker.Clear();
                return false;
            }

            bool ready = await AreMandatoryRequirementsFulfilledCoreAsync(
                dbContext, attempt.TenantId, attempt.RegistrationOrderId, cancellationToken);
            if (ready && !await dbContext.RegistrationFinalizationEffects.AnyAsync(value =>
                    value.TenantId == attempt.TenantId &&
                    value.RegistrationOrderId == attempt.RegistrationOrderId,
                    cancellationToken))
            {
                RegistrationOrder order = await dbContext.RegistrationOrders.SingleAsync(value =>
                    value.TenantId == attempt.TenantId && value.Id == attempt.RegistrationOrderId,
                    cancellationToken);
                await dbContext.RegistrationFinalizationEffects.AddAsync(
                    RegistrationFinalizationEffect.Create(order, recordedAt), cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return true;
        });
    }

    public Task<bool> AreMandatoryRequirementsFulfilledAsync(
        Guid tenantId,
        Guid registrationOrderId,
        CancellationToken cancellationToken) =>
        AreMandatoryRequirementsFulfilledCoreAsync(dbContext, tenantId, registrationOrderId, cancellationToken);

    public async Task<SucceededPaymentLookupResult> GetSucceededPaymentAsync(
        Guid tenantId,
        Guid registrationOrderId,
        CancellationToken cancellationToken)
    {
        List<PaymentSucceededObservation> observations = await dbContext.PaymentSucceededObservations
            .AsNoTracking()
            .Where(value => value.TenantId == tenantId && value.RegistrationOrderId == registrationOrderId)
            .OrderBy(value => value.Id)
            .Take(2)
            .ToListAsync(cancellationToken);
        if (observations.Count == 0)
        {
            return SucceededPaymentLookupResult.Missing();
        }

        if (observations.Count > 1)
        {
            return SucceededPaymentLookupResult.Conflict();
        }

        PaymentSucceededObservation observation = observations[0];
        PaymentAttempt? attempt = await dbContext.PaymentAttempts
            .AsNoTracking()
            .SingleOrDefaultAsync(value => value.TenantId == tenantId &&
                                           value.RegistrationOrderId == registrationOrderId &&
                                           value.Id == observation.PaymentAttemptId,
                cancellationToken);
        return attempt is null
            ? SucceededPaymentLookupResult.Missing()
            : SucceededPaymentLookupResult.Found(attempt, observation);
    }

    public async Task RequestAsync(
        RegistrationOrder order,
        DateTime requestedAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(order);
        RegistrationFinalizationEffect? effect = await dbContext.RegistrationFinalizationEffects
            .SingleOrDefaultAsync(value => value.TenantId == order.TenantId &&
                                           value.RegistrationOrderId == order.Id,
                cancellationToken);
        if (effect is null)
        {
            await dbContext.RegistrationFinalizationEffects.AddAsync(
                RegistrationFinalizationEffect.Create(order, requestedAt), cancellationToken);
        }
        else
        {
            effect.Request(requestedAt);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RegistrationFinalizationClaim>> ClaimDueAsync(
        string leaseOwner,
        int batchSize,
        DateTime claimedAt,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(leaseOwner) || leaseOwner.Trim().Length > RegistrationFinalizationEffect.MaxLeaseOwnerLength ||
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
                    "registration-finalization-claim",
                    cancellationToken);

            List<RegistrationFinalizationEffect> effects = await dbContext.RegistrationFinalizationEffects
                .IgnoreTenantFilter(TenantFilterBypassReasons.RegistrationFinalizationWorkerCrossTenantQueue)
                .Where(value =>
                    ((value.Status == OutboxMessageStatus.Pending || value.Status == OutboxMessageStatus.Failed) &&
                     (value.NextAttemptAt == null || value.NextAttemptAt <= claimedAt)) ||
                    (value.Status == OutboxMessageStatus.Processing && value.ProcessingLeaseExpiresAt <= claimedAt))
                .OrderBy(value => value.NextAttemptAt ?? value.CreatedAt)
                .ThenBy(value => value.Id)
                .Take(batchSize)
                .ToListAsync(cancellationToken);
            List<RegistrationFinalizationClaim> claims = new(effects.Count);
            foreach (RegistrationFinalizationEffect effect in effects)
            {
                if (effect.Status == OutboxMessageStatus.Processing)
                {
                    effect.RecoverExpiredClaim(claimedAt);
                }

                Guid leaseToken = Guid.CreateVersion7();
                effect.Claim(leaseOwner, leaseToken, claimedAt.Add(leaseDuration), claimedAt);
                claims.Add(new(effect.Id, effect.TenantId, effect.RegistrationOrderId, leaseToken, effect.ProcessingFence));
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return claims;
        });
    }

    public async Task<bool> CompleteAsync(
        RegistrationFinalizationClaim claim,
        DateTime completedAt,
        CancellationToken cancellationToken)
    {
        if (completedAt.Kind != DateTimeKind.Utc)
        {
            return false;
        }

        return await ActiveClaim(claim, completedAt)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.Status, OutboxMessageStatus.Completed)
                .SetProperty(value => value.CompletedAt, completedAt)
                .SetProperty(value => value.ProcessingLeaseOwner, (string?)null)
                .SetProperty(value => value.ProcessingLeaseToken, (Guid?)null)
                .SetProperty(value => value.ProcessingLeaseExpiresAt, (DateTime?)null)
                .SetProperty(value => value.UpdatedAt, completedAt), cancellationToken) == 1;
    }

    public async Task<bool> RetryAsync(
        RegistrationFinalizationClaim claim,
        DateTime nextAttemptAt,
        DateTime failedAt,
        CancellationToken cancellationToken)
    {
        if (failedAt.Kind != DateTimeKind.Utc || nextAttemptAt.Kind != DateTimeKind.Utc || nextAttemptAt <= failedAt)
        {
            return false;
        }

        return await ActiveClaim(claim, failedAt)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.Status, OutboxMessageStatus.Failed)
                .SetProperty(value => value.NextAttemptAt, nextAttemptAt)
                .SetProperty(value => value.ProcessingLeaseOwner, (string?)null)
                .SetProperty(value => value.ProcessingLeaseToken, (Guid?)null)
                .SetProperty(value => value.ProcessingLeaseExpiresAt, (DateTime?)null)
                .SetProperty(value => value.UpdatedAt, failedAt), cancellationToken) == 1;
    }

    internal static async Task<bool> AreMandatoryRequirementsFulfilledCoreAsync(
        ExploreDbContext dbContext,
        Guid tenantId,
        Guid registrationOrderId,
        CancellationToken cancellationToken)
    {
        var order = await dbContext.RegistrationOrders
        .Where(order => order.TenantId == tenantId && order.Id == registrationOrderId)
        .Select(value => new { value.Id, value.RegistrationWorkflowVersionId })
        .SingleOrDefaultAsync(cancellationToken);
        if (order is null)
        {
            return false;
        }

        if (order.RegistrationWorkflowVersionId is not Guid workflowId)
        {
            return true;
        }

        var requirements = await dbContext.RegistrationRequirements
            .Where(requirement =>
                requirement.TenantId == tenantId &&
                requirement.RegistrationWorkflowId == workflowId &&
                requirement.CriticalityId == (int)RegistrationRequirementCriticalityEnum.Required)
            .Select(requirement => new
            {
                requirement.Id,
                requirement.AppliesToSubjectTypeId,
                requirement.AppliesToSubjectId
            })
            .ToListAsync(cancellationToken);
        if (requirements.Count == 0)
        {
            return true;
        }

        HashSet<(Guid RequirementId, int SubjectTypeId, Guid SubjectId)> fulfilled = (await dbContext
                .RegistrationRequirementFulfillments
                .Where(value =>
                    value.TenantId == tenantId &&
                    value.RegistrationOrderId == registrationOrderId &&
                    !value.IsSkipped)
                .Select(value => new { value.RegistrationRequirementId, value.SubjectTypeId, value.SubjectId })
                .ToListAsync(cancellationToken))
            .Select(value => (value.RegistrationRequirementId, value.SubjectTypeId, value.SubjectId))
            .ToHashSet();
        var participants = await dbContext.RegistrationParticipants
            .Where(participant =>
                participant.TenantId == tenantId && participant.RegistrationOrderId == registrationOrderId)
            .Select(participant => new { participant.Id, participant.ParticipantTypeId })
            .ToListAsync(cancellationToken);
        var ticketAssignments = await (
                from assignment in dbContext.RegistrationTicketAssignments
                join line in dbContext.RegistrationOrderLines
                    on new { assignment.TenantId, assignment.RegistrationOrderLineId }
                    equals new { line.TenantId, RegistrationOrderLineId = line.Id }
                where assignment.TenantId == tenantId && assignment.RegistrationOrderId == registrationOrderId
                select new { assignment.Id, line.TicketTypeId })
            .ToListAsync(cancellationToken);

        bool Has(Guid requirementId, RegistrationAnswerSubjectTypeEnum subjectType, Guid subjectId) =>
            fulfilled.Contains((requirementId, (int)subjectType, subjectId));

        return requirements.All(requirement =>
            (RegistrationRequirementSubjectTypeEnum)requirement.AppliesToSubjectTypeId switch
            {
                RegistrationRequirementSubjectTypeEnum.AllOrders =>
                    Has(requirement.Id, RegistrationAnswerSubjectTypeEnum.RegistrationOrder, order.Id) ||
                    Has(requirement.Id, RegistrationAnswerSubjectTypeEnum.Purchaser, order.Id),
                RegistrationRequirementSubjectTypeEnum.SpecificTicketType =>
                    requirement.AppliesToSubjectId is Guid ticketTypeId &&
                    ticketAssignments.Where(value => value.TicketTypeId == ticketTypeId)
                        .All(value => Has(requirement.Id, RegistrationAnswerSubjectTypeEnum.TicketAssignment, value.Id)),
                RegistrationRequirementSubjectTypeEnum.EveryParticipant =>
                    participants.All(value => Has(
                        requirement.Id, RegistrationAnswerSubjectTypeEnum.Participant, value.Id)),
                RegistrationRequirementSubjectTypeEnum.LeadBookerOnly =>
                    Has(requirement.Id, RegistrationAnswerSubjectTypeEnum.Purchaser, order.Id),
                RegistrationRequirementSubjectTypeEnum.ChildParticipants =>
                    participants.Where(value => value.ParticipantTypeId == (int)ParticipantTypeEnum.Child)
                        .All(value => Has(requirement.Id, RegistrationAnswerSubjectTypeEnum.Participant, value.Id)),
                RegistrationRequirementSubjectTypeEnum.SpecificSessionSelection =>
                    requirement.AppliesToSubjectId is Guid sessionSelectionId &&
                    Has(requirement.Id, RegistrationAnswerSubjectTypeEnum.SessionSelection, sessionSelectionId),
                _ => false
            });
    }

    private IQueryable<RegistrationFinalizationEffect> ActiveClaim(
        RegistrationFinalizationClaim claim,
        DateTime observedAt) => dbContext.RegistrationFinalizationEffects
        .IgnoreTenantFilter(TenantFilterBypassReasons.RegistrationFinalizationWorkerCrossTenantQueue)
        .Where(value =>
            value.TenantId == claim.TenantId && value.Id == claim.EffectId &&
            value.RegistrationOrderId == claim.RegistrationOrderId &&
            value.Status == OutboxMessageStatus.Processing &&
            value.ProcessingLeaseToken == claim.LeaseToken &&
            value.ProcessingFence == claim.ProcessingFence &&
            value.ProcessingLeaseExpiresAt > observedAt);
}
