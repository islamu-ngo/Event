using Explore.Application.Contracts.Admissions;
using Explore.Application.Notifications;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services.Registration;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class AdmissionIssuanceRepository(
    ExploreDbContext dbContext,
    IParticipantAdmissionReadinessAuthority readiness,
    TimeProvider? timeProvider = null) :
    IAdmissionIssuanceRepository
{
    public AdmissionIssuanceRepository(
        ExploreDbContext dbContext) : this(
        dbContext,
        new ParticipantAdmissionEligibilityRepository(dbContext))
    {
    }

    public Task<AdmissionIssuanceContext?> LoadAsync(
        AdmissionIssuanceRequest request,
        CancellationToken cancellationToken) =>
        LoadCoreAsync(request, acquireAuthorityFences: true, cancellationToken);

    private async Task<AdmissionIssuanceContext?> LoadCoreAsync(
        AdmissionIssuanceRequest request,
        bool acquireAuthorityFences,
        CancellationToken cancellationToken)
    {
        if (acquireAuthorityFences)
        {
            await RelationalEntityRowFence.AcquireAsync<RegistrationFinalizationEffect>(
                dbContext,
                request.TenantId,
                effect => effect.Id,
                request.FinalizationEffectId,
                cancellationToken);
        }
        IQueryable<RegistrationFinalizationEffect> effects =
            dbContext.RegistrationFinalizationEffects;
        RegistrationFinalizationEffect? effect = await effects.SingleOrDefaultAsync(value =>
                value.TenantId == request.TenantId &&
                value.Id == request.FinalizationEffectId &&
                value.RegistrationOrderId == request.RegistrationOrderId,
                cancellationToken);
        if (effect is null)
        {
            return null;
        }

        if (acquireAuthorityFences)
        {
            await RelationalEntityRowFence.AcquireAsync<RegistrationOrder>(
                dbContext,
                request.TenantId,
                order => order.Id,
                request.RegistrationOrderId,
                cancellationToken);
        }
        // The fence excludes other writers, but a tracking query can still reuse
        // an order retained before cancellation. Refresh only that authority root.
        RegistrationOrder? trackedOrder = dbContext.RegistrationOrders.Local.SingleOrDefault(value =>
            value.TenantId == request.TenantId && value.Id == request.RegistrationOrderId);
        if (trackedOrder is not null)
        {
            await dbContext.Entry(trackedOrder).ReloadAsync(cancellationToken);
        }
        IQueryable<RegistrationOrder> orders = dbContext.RegistrationOrders;
        RegistrationOrder? order = await orders
            .Include(value => value.Pii)
            .Include(value => value.Lines)
                .ThenInclude(value => value.Assignments)
            .Include(value => value.Participants)
            .SingleOrDefaultAsync(value => value.TenantId == request.TenantId && value.Id == request.RegistrationOrderId,
                cancellationToken);
        if (order is null)
        {
            return null;
        }

        if (acquireAuthorityFences)
        {
            await RelationalEntityRowFence.AcquireAsync<Event>(
                dbContext,
                request.TenantId,
                eventEntity => eventEntity.Id,
                order.EventId,
                cancellationToken);
        }
        IQueryable<Event> events = dbContext.Events.AsNoTracking();
        Event? admissionEvent = await events.SingleOrDefaultAsync(value =>
                value.TenantId == request.TenantId &&
                value.Id == order.EventId,
            cancellationToken);

        EventTicketCatalogVersion catalog = await dbContext.EventTicketCatalogVersions
            .Include(value => value.TicketTypes)
            .SingleAsync(value => value.TenantId == request.TenantId && value.Id == order.TicketCatalogVersionId,
                cancellationToken);
        List<AdmissionTicket> existing = await dbContext.AdmissionTickets
            .Include(value => value.Credentials)
            .Where(value => value.TenantId == request.TenantId && value.RegistrationOrderId == request.RegistrationOrderId)
            .ToListAsync(cancellationToken);
        List<AdmissionDeliveryIntent> existingDeliveryIntents = await dbContext.AdmissionDeliveryIntents
            .Where(value => value.TenantId == request.TenantId && value.FinalizationEffectId == effect.Id)
            .ToListAsync(cancellationToken);
        Dictionary<Guid, RegistrationParticipant> participants =
            order.Participants.ToDictionary(value => value.Id);
        Dictionary<Guid, EventTicketType> ticketTypes =
            catalog.TicketTypes.ToDictionary(value => value.Id);
        HashSet<Guid> fullyRefundedLineIds = order.TotalDueMinorSnapshot > 0
            ? await GetFullyRefundedLineIdsAsync(order, cancellationToken)
            : [];
        bool confirmed = order.RegistrationOrderStatusId == (int)RegistrationOrderStatusEnum.Confirmed &&
            order.ConfirmedAt is not null &&
            admissionEvent is not null &&
            admissionEvent.EventStatusId != (int)EventStatusEnum.Cancelled;
        bool paymentReconciled = order.TotalDueMinorSnapshot > 0 &&
            await HasExactReconciledPaymentAsync(order, cancellationToken);
        bool paymentSatisfied =
            order.TotalDueMinorSnapshot == 0
            || paymentReconciled;
        var assignments =
            new List<AdmissionAssignmentFact>();
        foreach (RegistrationOrderLine line in order.Lines)
        {
            foreach (
                RegistrationTicketAssignment assignment
                in line.Assignments)
            {
                bool isAdmissionLine =
                    !fullyRefundedLineIds.Contains(line.Id);
                ParticipantAdmissionReadinessDecision?
                    readinessDecision = isAdmissionLine
                    ? await readiness.EvaluateForUpdateAsync(
                        order.TenantId,
                        assignment.Id,
                        confirmed,
                        paymentSatisfied,
                        cancellationToken)
                    : null;
                assignments.Add(
                    new AdmissionAssignmentFact(
                        line,
                        assignment,
                        participants[
                            assignment.ParticipantId
                                .GetValueOrDefault()],
                        ticketTypes[line.TicketTypeId],
                        line.PostDiscountLineSubtotalMinorSnapshot
                        / line.Quantity,
                        line.PostDiscountLineSubtotalMinorSnapshot,
                        isAdmissionLine,
                        readinessDecision));
            }
        }
        bool canDisclose = AnonymousRegistrationRetentionPolicy.CanDisclose(
            order, order.Pii?.RetentionUntil, (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime);
        string? deliveryAddress = canDisclose ? order.Pii?.Email : null;
        Guid? deliveryAccountUserId = null;
        if (canDisclose && string.IsNullOrWhiteSpace(deliveryAddress) && order.AccountUserId.HasValue)
        {
            User? account = await dbContext.Users.AsNoTracking().Include(value => value.Pii)
                .SingleOrDefaultAsync(value => value.Id == order.AccountUserId.Value, cancellationToken);
            deliveryAddress = RecipientEmailAddressResolver.Resolve(account, order.AccountUserId.Value).Email;
            if (!string.IsNullOrWhiteSpace(deliveryAddress)) deliveryAccountUserId = order.AccountUserId;
        }
        if (!deliveryAccountUserId.HasValue && !AnonymousRegistrationRetentionPolicy.CanDisclose(
                order, order.Pii?.RetentionUntil, (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime))
            deliveryAddress = null;
        return new AdmissionIssuanceContext(
            order.TenantId,
            order.EventId,
            order.Id,
            effect.Id,
            order.TotalDueMinorSnapshot == 0
                ? AdmissionIssuanceAuthority.ConfirmedFreeOrder
                : paymentReconciled
                    ? AdmissionIssuanceAuthority.ReconciledPaidFinalization
                    : "PaymentSucceeded",
            paymentReconciled,
            confirmed,
            order,
            catalog,
            assignments.ToArray(),
            existing,
            deliveryAddress ?? string.Empty,
            existingDeliveryIntents)
        {
            DeliveryAccountUserId = deliveryAccountUserId,
            DeliveryDisclosureUntilUtc = !deliveryAccountUserId.HasValue && !string.IsNullOrWhiteSpace(deliveryAddress)
                && AnonymousRegistrationRetentionPolicy.GetDisclosureDeadline(order, order.Pii?.RetentionUntil) is { } deadline
                ? DateTime.SpecifyKind(deadline, DateTimeKind.Utc) : null
        };
    }

    private async Task<HashSet<Guid>> GetFullyRefundedLineIdsAsync(
        RegistrationOrder order,
        CancellationToken cancellationToken)
    {
        Dictionary<Guid, long> refundedByLine = await (
                from allocation in dbContext.RefundLineAllocations.AsNoTracking()
                join attempt in dbContext.RefundAttempts.AsNoTracking()
                    on new { allocation.TenantId, Id = allocation.RefundAttemptId }
                    equals new { attempt.TenantId, Id = attempt.Id }
                where attempt.TenantId == order.TenantId &&
                      attempt.RegistrationOrderId == order.Id &&
                      attempt.BuyerRefundSucceededAt != null
                group allocation by allocation.OrderLineId
                into line
                select new
                {
                    OrderLineId = line.Key,
                    RefundedMinor = line.Sum(value => value.OrganizerAmountMinor)
                })
            .ToDictionaryAsync(
                value => value.OrderLineId,
                value => value.RefundedMinor,
                cancellationToken);

        return order.Lines
            .Where(line =>
                line.PostDiscountLineSubtotalMinorSnapshot > 0 &&
                refundedByLine.TryGetValue(line.Id, out long refundedMinor) &&
                refundedMinor == line.PostDiscountLineSubtotalMinorSnapshot)
            .Select(line => line.Id)
            .ToHashSet();
    }

    private async Task<bool> HasExactReconciledPaymentAsync(
        RegistrationOrder order,
        CancellationToken cancellationToken)
    {
        PaymentSucceededObservation[] observations = await dbContext.PaymentSucceededObservations
            .AsNoTracking()
            .Where(value => value.TenantId == order.TenantId &&
                            value.RegistrationOrderId == order.Id)
            .OrderBy(value => value.Id)
            .Take(2)
            .ToArrayAsync(cancellationToken);
        if (observations.Length != 1)
        {
            return false;
        }

        PaymentSucceededObservation observation = observations[0];
        PaymentAttempt? attempt = await dbContext.PaymentAttempts
            .AsNoTracking()
            .SingleOrDefaultAsync(value =>
                value.TenantId == order.TenantId &&
                value.RegistrationOrderId == order.Id &&
                value.Id == observation.PaymentAttemptId,
                cancellationToken);
        return attempt is not null &&
            attempt.PaymentAttemptStatusId == (int)PaymentAttemptStatusEnum.Succeeded &&
            string.Equals(attempt.CurrencyCode, order.CurrencyCode, StringComparison.Ordinal) &&
            attempt.OrganizerAmountMinor == order.OrganizerDirectedTotalMinorSnapshot &&
            attempt.PlatformFeeMinor == order.PlatformFeeTotalMinorSnapshot &&
            attempt.PlatformContributionMinor == order.PlatformContributionTotalMinorSnapshot &&
            attempt.TotalMinor == order.TotalDueMinorSnapshot &&
            observation.TenantId == order.TenantId &&
            observation.RegistrationOrderId == order.Id &&
            string.Equals(
                observation.ProviderCheckoutSessionId,
                attempt.ProviderCheckoutSessionId,
                StringComparison.Ordinal) &&
            string.Equals(
                observation.ProviderPaymentId,
                attempt.ProviderPaymentId,
                StringComparison.Ordinal);
    }

    public Task<AdmissionIssuanceContext?> ReloadCommittedAsync(
        AdmissionIssuanceRequest request,
        CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.Clear();
        return LoadCoreAsync(request, acquireAuthorityFences: false, cancellationToken);
    }

    public async Task<AdmissionIssuanceResult> IssueAndScheduleDeliveryAsync(
        AdmissionIssuancePersistenceRequest request,
        CancellationToken cancellationToken)
    {
        List<AdmissionTicket> existing = await dbContext.AdmissionTickets
            .Include(ticket => ticket.Credentials)
            .Where(ticket => ticket.TenantId == request.TenantId &&
                request.Tickets.Select(candidate => candidate.RegistrationTicketAssignmentId)
                    .Contains(ticket.RegistrationTicketAssignmentId))
            .ToListAsync(cancellationToken);
        HashSet<Guid> existingAssignments = existing
            .Select(ticket => ticket.RegistrationTicketAssignmentId)
            .ToHashSet();
        AdmissionTicket[] issued = request.Tickets
            .Where(ticket => !existingAssignments.Contains(ticket.RegistrationTicketAssignmentId))
            .ToArray();
        AdmissionDeliveryIntent[] intents = request.DeliveryIntents
            .Where(intent => issued.Any(ticket => ticket.Id == intent.AdmissionTicketId))
            .ToArray();
        if (issued.Length > 0)
        {
            await dbContext.AdmissionTickets.AddRangeAsync(issued, cancellationToken);
            await dbContext.AdmissionDeliveryIntents.AddRangeAsync(intents, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        AdmissionTicket[] all = existing.Concat(issued).ToArray();
        return new AdmissionIssuanceResult(
            issued.Length == 0 ? AdmissionIssuanceOutcome.AlreadyIssued : AdmissionIssuanceOutcome.Issued,
            issued.Select(ticket => ticket.Id).ToArray(),
            existing.Select(ticket => ticket.Id).ToArray(),
            all,
            intents.Select(intent => intent.Id).ToArray());
    }
}
