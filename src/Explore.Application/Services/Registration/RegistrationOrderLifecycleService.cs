using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Scheduling;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services.Registration;

namespace Explore.Application.Services.Registration;

public sealed partial class RegistrationOrderLifecycleService(
    IRegistrationInventoryRepository inventory,
    IPromotionRedemptionRepository promotions,
    IRegistrationParticipantRepository participants,
    IEventTicketCatalogRepository catalogs,
    IPlatformContributionSettingRepository contributionSettings,
    IEventSessionRepository eventSessions,
    IOutboxRepository outbox,
    IUnitOfWork unitOfWork,
    IRegistrationFinalizationRepository finalization,
    IRegistrationPaymentAttemptRepository paymentAttempts,
    IScheduledDeadlineDispatcher deadlines,
    TimeProvider timeProvider,
    IPaidOrderAcceptanceService paidAcceptance,
    IRegistrationOrderTransitionCoordinator transitions) : IRegistrationOrderLifecycleService
{
    private readonly RegistrationOrderReadService reads = new(
        inventory,
        contributionSettings,
        paidAcceptance,
        timeProvider);

    public Task<RegistrationOrderLifecycleResponseDto> SubmitAsync(
        Guid orderId,
        Guid tenantId,
        CancellationToken cancellationToken) => SubmitAsync(orderId, tenantId, null, cancellationToken);

    // Every public transition routes through WithdrawHoldDeadlineWhenTerminalAsync so that withdrawing a
    // finished order's hold-expiry wake-up is a property of the service rather than a step each transition
    // has to remember. Several transitions can end an order — reject, cancel, free finalization — and more
    // will exist; hooking each one individually is exactly how one gets missed and leaves dead triggers
    // accumulating in the scheduler tables.
    public Task<RegistrationOrderLifecycleResponseDto> SubmitAsync(
        Guid orderId,
        Guid tenantId,
        int? platformContributionBasisPoints,
        CancellationToken cancellationToken) => WithdrawHoldDeadlineWhenTerminalAsync(
            SubmitCoreAsync(orderId, tenantId, platformContributionBasisPoints, cancellationToken),
            cancellationToken);

    public Task<RegistrationOrderLifecycleResponseDto> ReadyForCheckoutAsync(
        Guid orderId,
        Guid tenantId,
        CancellationToken cancellationToken) => WithdrawHoldDeadlineWhenTerminalAsync(
            ReadyForCheckoutCoreAsync(orderId, tenantId, cancellationToken),
            cancellationToken);

    public Task<RegistrationOrderLifecycleResponseDto> ApproveAsync(
        Guid orderId,
        Guid tenantId,
        CancellationToken cancellationToken) => WithdrawHoldDeadlineWhenTerminalAsync(
            ApproveCoreAsync(orderId, tenantId, cancellationToken),
            cancellationToken);

    public Task<RegistrationOrderLifecycleResponseDto> RejectAsync(
        Guid orderId,
        Guid tenantId,
        CancellationToken cancellationToken) => WithdrawHoldDeadlineWhenTerminalAsync(
            RejectCoreAsync(orderId, tenantId, cancellationToken),
            cancellationToken);

    public Task<RegistrationOrderLifecycleResponseDto> CancelAsync(
        Guid orderId,
        Guid tenantId,
        CancellationToken cancellationToken) => WithdrawHoldDeadlineWhenTerminalAsync(
            CancelCoreAsync(orderId, tenantId, cancellationToken),
            cancellationToken);

    public Task<RegistrationOrderLifecycleResponseDto> FinalizeFreeAsync(
        Guid orderId,
        Guid tenantId,
        CancellationToken cancellationToken) => WithdrawHoldDeadlineWhenTerminalAsync(
            FinalizeCoreAsync(orderId, tenantId, paid: false, cancellationToken),
            cancellationToken);


    public Task<RegistrationOrderLifecycleResponseDto> RecoverExpiredHoldAsync(
        Guid orderId,
        Guid tenantId,
        CancellationToken cancellationToken) => WithdrawHoldDeadlineWhenTerminalAsync(
            RecoverExpiredHoldCoreAsync(orderId, tenantId, cancellationToken),
            cancellationToken);


    /// <summary>
    /// Removes an order's pending hold-expiry deadline once the order reaches a state that can never need
    /// it again. Failing to withdraw one is not a correctness problem — an orphaned deadline fires once,
    /// finds no due hold, and stops — so this never disturbs the transition it follows.
    /// </summary>

    private async Task<RegistrationOrderLifecycleResponseDto> SubmitCoreAsync(
        Guid orderId,
        Guid tenantId,
        int? platformContributionBasisPoints,
        CancellationToken cancellationToken)
    {
        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        return await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            RegistrationOrder? order = platformContributionBasisPoints.HasValue
                ? await inventory.GetOrderForUpdateWithLinesAsync(orderId, tenantId, token)
                : await inventory.GetOrderWithLinesAsync(orderId, tenantId, token);
            if (order is null)
            {
                return Missing(orderId);
            }

            RegistrationOrderStatusEnum status = (RegistrationOrderStatusEnum)order.RegistrationOrderStatusId;
            if (status != RegistrationOrderStatusEnum.AwaitingParticipantDetails)
            {
                return Success(order, status, "Registration order is already submitted.");
            }

            if (platformContributionBasisPoints.HasValue)
            {
                if (platformContributionBasisPoints is < 0 or > 10_000)
                {
                    return Failure(orderId, order, "Platform contribution percentage is invalid.");
                }

                RegistrationOrderPlatformContribution? contribution = null;
                if (platformContributionBasisPoints > 0)
                {
                    if (order.OrganizerDirectedTotalMinorSnapshot == 0)
                    {
                        return Failure(orderId, order, "Platform contributions require an existing payable order total.");
                    }

                    PlatformContributionSetting? setting = await contributionSettings.GetActiveAsync(token);
                    if (setting is null || !setting.IsEnabled)
                    {
                        return Failure(orderId, order, "Platform contributions are not enabled.");
                    }

                    if (setting.Options.All(option => option.ContributionBasisPoints != platformContributionBasisPoints.Value))
                    {
                        return Failure(orderId, order, "Platform contribution percentage is invalid.");
                    }

                    contribution = RegistrationOrderPlatformContribution.CreateOrNull(
                        order.Id,
                        order.TenantId,
                        setting,
                        platformContributionBasisPoints.Value,
                        order.OrganizerDirectedTotalMinorSnapshot,
                        order.CurrencyCode);
                }

                order.SetPlatformContribution(contribution);
                order.ApplyTotals(RegistrationOrderTotalsSnapshot.Create(
                    order.CurrencyCode,
                    order.OrganizerDirectedTotalMinorSnapshot,
                    order.PlatformFeeTotalMinorSnapshot,
                    order.OrganizerEarningsTotalMinorSnapshot,
                    contribution?.AmountMinor ?? 0));
                await inventory.SaveChangesAsync(token);
            }

            bool transitioned = await transitions.PersistAsync(
                order.Id,
                tenantId,
                status,
                RegistrationOrderStatusEnum.AwaitingRequirements,
                now,
                token);
            return transitioned
                ? Success(order, RegistrationOrderStatusEnum.AwaitingRequirements, "Registration order submitted.")
                : await CurrentOrConflictAsync(orderId, tenantId, "Registration order changed while it was submitted.", token);
        }, cancellationToken);
    }

    private async Task<RegistrationOrderLifecycleResponseDto> ReadyForCheckoutCoreAsync(Guid orderId, Guid tenantId, CancellationToken cancellationToken)
    {
        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        RegistrationOrder? initialOrder = await inventory.GetOrderWithLinesAsync(orderId, tenantId, cancellationToken);
        if (initialOrder is null)
        {
            return Missing(orderId);
        }

        RegistrationOrderStatusEnum initialStatus = (RegistrationOrderStatusEnum)initialOrder.RegistrationOrderStatusId;
        if (initialStatus != RegistrationOrderStatusEnum.AwaitingRequirements)
        {
            return Success(initialOrder, initialStatus, "Registration order is already routed for checkout.");
        }

        if (initialOrder.RegistrationWorkflowVersionId.HasValue &&
            !await finalization.AreMandatoryRequirementsFulfilledAsync(tenantId, orderId, cancellationToken))
        {
            return Failure(orderId, initialOrder, "Registration order still has mandatory requirements.");
        }

        CapacityReservationPlan plan = await PrepareCapacityReservationPlanAsync(initialOrder, cancellationToken);
        bool requiresApproval = await RequiresApprovalAsync(initialOrder, plan.TicketTypes, cancellationToken);
        try
        {
            return await unitOfWork.ExecuteSerializableAsync(async token =>
            {
                RegistrationOrder? order = await inventory.GetOrderForUpdateWithLinesAsync(orderId, tenantId, token);
                if (order is null)
                {
                    return Missing(orderId);
                }

                RegistrationOrderStatusEnum status = (RegistrationOrderStatusEnum)order.RegistrationOrderStatusId;
                if (status != RegistrationOrderStatusEnum.AwaitingRequirements)
                {
                    return Success(order, status, "Registration order is already routed for checkout.");
                }

                if (order.RegistrationWorkflowVersionId.HasValue &&
                    !await finalization.AreMandatoryRequirementsFulfilledAsync(tenantId, orderId, token))
                {
                    return Failure(orderId, order, "Registration order still has mandatory requirements.");
                }

                await LoadActivePromotionForUpdateAsync(order, token);

                if (requiresApproval)
                {
                    return await TransitionForApprovalAsync(order, now, token);
                }

                RegistrationInventoryReservationResult reservation = await ReserveCapacityAsync(
                    order,
                    plan,
                    approvalGranted: false,
                    now,
                    token);
                if (reservation.RequiresApproval)
                {
                    return await TransitionForApprovalAsync(order, now, token);
                }

                if (!reservation.Reserved)
                {
                    if (!reservation.ShouldWaitlist)
                    {
                        throw new CapacityUnavailableException();
                    }

                    await ReleaseActiveHoldsForWaitlistAsync(order, now, token);
                    if (!await transitions.PersistAsync(
                            order.Id,
                            tenantId,
                            RegistrationOrderStatusEnum.AwaitingRequirements,
                            RegistrationOrderStatusEnum.ReadyForCheckout,
                            now,
                            token))
                    {
                        throw new LifecycleRaceException();
                    }

                    if (!await transitions.PersistAsync(
                            order.Id,
                            tenantId,
                            RegistrationOrderStatusEnum.ReadyForCheckout,
                            RegistrationOrderStatusEnum.Waitlisted,
                            now,
                            token))
                    {
                        throw new LifecycleRaceException();
                    }

                    await inventory.SaveChangesAsync(token);
                    return Success(order, RegistrationOrderStatusEnum.Waitlisted, "Registration order is waitlisted while capacity is unavailable.");
                }

                if (!await transitions.PersistAsync(
                        order.Id,
                        tenantId,
                        RegistrationOrderStatusEnum.AwaitingRequirements,
                        RegistrationOrderStatusEnum.ReadyForCheckout,
                        now,
                        token))
                {
                    throw new LifecycleRaceException();
                }

                return order.TotalDueMinorSnapshot == 0
                    ? Success(order, RegistrationOrderStatusEnum.ReadyForCheckout, "Registration order is ready for free finalization.")
                    : await RouteToPaymentAsync(order, now, token);
            }, cancellationToken);
        }
        catch (CapacityUnavailableException)
        {
            return Failure(orderId, initialOrder, "Registration capacity is unavailable.");
        }
        catch (LifecycleRaceException)
        {
            return await CurrentOrConflictAsync(orderId, tenantId, "Registration order changed while it was prepared for checkout.", cancellationToken);
        }
    }

    private async Task<RegistrationOrderLifecycleResponseDto> ApproveCoreAsync(Guid orderId, Guid tenantId, CancellationToken cancellationToken)
    {
        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        RegistrationOrder? initialOrder = await inventory.GetOrderWithLinesAsync(orderId, tenantId, cancellationToken);
        if (initialOrder is null)
        {
            return Missing(orderId);
        }

        RegistrationOrderStatusEnum initialStatus = (RegistrationOrderStatusEnum)initialOrder.RegistrationOrderStatusId;
        if (initialStatus != RegistrationOrderStatusEnum.AwaitingApproval)
        {
            return Success(initialOrder, initialStatus, "Registration order approval was already resolved.");
        }

        CapacityReservationPlan plan = await PrepareCapacityReservationPlanAsync(initialOrder, cancellationToken);
        try
        {
            return await unitOfWork.ExecuteSerializableAsync(async token =>
            {
                RegistrationOrder? order = await inventory.GetOrderForUpdateWithLinesAsync(orderId, tenantId, token);
                if (order is null)
                {
                    return Missing(orderId);
                }

                RegistrationOrderStatusEnum status = (RegistrationOrderStatusEnum)order.RegistrationOrderStatusId;
                if (status != RegistrationOrderStatusEnum.AwaitingApproval)
                {
                    return Success(order, status, "Registration order approval was already resolved.");
                }

                await LoadActivePromotionForUpdateAsync(order, token);

                RegistrationInventoryReservationResult reservation = await ReserveCapacityAsync(
                    order,
                    plan,
                    approvalGranted: true,
                    now,
                    token);
                if (reservation.RequiresApproval)
                {
                    throw new InvalidOperationException("Registration order approval did not satisfy capacity policy.");
                }

                if (!reservation.Reserved)
                {
                    if (!reservation.ShouldWaitlist)
                    {
                        throw new CapacityUnavailableException();
                    }

                    await ReleaseActiveHoldsForWaitlistAsync(order, now, token);
                    if (!await transitions.PersistAsync(
                            order.Id,
                            tenantId,
                            RegistrationOrderStatusEnum.AwaitingApproval,
                            RegistrationOrderStatusEnum.Waitlisted,
                            now,
                            token))
                    {
                        throw new LifecycleRaceException();
                    }

                    await inventory.SaveChangesAsync(token);
                    return Success(order, RegistrationOrderStatusEnum.Waitlisted, "Registration order is waitlisted while capacity is unavailable.");
                }

                if (!await transitions.PersistAsync(
                        order.Id,
                        tenantId,
                        RegistrationOrderStatusEnum.AwaitingApproval,
                        RegistrationOrderStatusEnum.ReadyForCheckout,
                        now,
                        token))
                {
                    throw new LifecycleRaceException();
                }

                return order.TotalDueMinorSnapshot == 0
                    ? Success(order, RegistrationOrderStatusEnum.ReadyForCheckout, "Registration order is ready for free finalization.")
                    : await RouteToPaymentAsync(order, now, token);
            }, cancellationToken);
        }
        catch (CapacityUnavailableException)
        {
            return Failure(orderId, initialOrder, "Registration capacity is unavailable.");
        }
        catch (LifecycleRaceException)
        {
            return await CurrentOrConflictAsync(orderId, tenantId, "Registration order changed while it was approved.", cancellationToken);
        }
    }

    private Task<RegistrationOrderLifecycleResponseDto> RejectCoreAsync(Guid orderId, Guid tenantId, CancellationToken cancellationToken) =>
        EndAsync(
            orderId,
            tenantId,
            RegistrationOrderStatusEnum.AwaitingApproval,
            RegistrationOrderStatusEnum.Rejected,
            RegistrationInventoryHoldStatusEnum.Released,
            "Registration order rejected.",
            cancellationToken);


    private async Task<RegistrationOrderLifecycleResponseDto> FinalizeCoreAsync(
        Guid orderId,
        Guid tenantId,
        bool paid,
        CancellationToken cancellationToken)
    {
        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        RegistrationOrder? initialOrder = await inventory.GetOrderWithLinesAsync(orderId, tenantId, cancellationToken);
        if (initialOrder is null)
        {
            return Missing(orderId);
        }

        RegistrationOrderStatusEnum initialStatus = (RegistrationOrderStatusEnum)initialOrder.RegistrationOrderStatusId;
        if (initialStatus == RegistrationOrderStatusEnum.Confirmed)
        {
            if (paid && await GetPaymentEvidenceStateAsync(initialOrder, cancellationToken) == PaymentEvidenceState.Duplicate)
            {
                return Success(initialOrder, initialStatus, SucceededPaymentLookupResult.DuplicateCode);
            }

            return Success(initialOrder, initialStatus, "Registration order is already confirmed.");
        }

        if (RegistrationOrderRules.IsTerminal(initialStatus))
        {
            return Success(initialOrder, initialStatus, "Registration order is terminal and cannot be finalized.");
        }

        bool eligibleStatus = paid
            ? initialStatus is RegistrationOrderStatusEnum.AwaitingPayment or RegistrationOrderStatusEnum.NeedsReconciliation
            : initialStatus == RegistrationOrderStatusEnum.ReadyForCheckout;
        if (!eligibleStatus || paid == (initialOrder.TotalDueMinorSnapshot == 0))
        {
            return Failure(orderId, initialOrder, paid
                ? "Registration order is not eligible for paid finalization."
                : "Registration order is not eligible for free finalization.");
        }

        PaymentEvidenceState initialPayment = paid
            ? await GetPaymentEvidenceStateAsync(initialOrder, cancellationToken)
            : PaymentEvidenceState.Missing;
        if (paid && initialPayment == PaymentEvidenceState.Missing)
        {
            return Failure(orderId, initialOrder, "Registration order has no exact reconciled successful payment.");
        }

        if (paid && initialPayment is PaymentEvidenceState.Mismatch or PaymentEvidenceState.Duplicate)
        {
            string code = initialPayment == PaymentEvidenceState.Duplicate
                ? SucceededPaymentLookupResult.DuplicateCode
                : "payment_composition_mismatch";
            return await ParkPaidIssueAsync(initialOrder, now, code, cancellationToken);
        }

        RegistrationOrderStatusEnum expectedStatus = initialStatus;

        if (initialOrder.RegistrationWorkflowVersionId.HasValue &&
            !await finalization.AreMandatoryRequirementsFulfilledAsync(tenantId, orderId, cancellationToken))
        {
            return Failure(orderId, initialOrder, "Registration order still has mandatory requirements.");
        }

        FinalizationPlan plan;
        try
        {
            plan = await PrepareFinalizationAsync(initialOrder, now, cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            return Failure(orderId, initialOrder, exception.Message);
        }

        try
        {
            return await unitOfWork.ExecuteSerializableAsync(async token =>
            {
                RegistrationOrder? order = await inventory.GetOrderForUpdateWithLinesAsync(orderId, tenantId, token);
                if (order is null)
                {
                    return Missing(orderId);
                }

                RegistrationOrderStatusEnum status = (RegistrationOrderStatusEnum)order.RegistrationOrderStatusId;
                if (status == RegistrationOrderStatusEnum.Confirmed)
                {
                    if (paid && await GetPaymentEvidenceStateAsync(order, token) == PaymentEvidenceState.Duplicate)
                    {
                        return Success(order, status, SucceededPaymentLookupResult.DuplicateCode);
                    }

                    return Success(order, status, "Registration order is already confirmed.");
                }

                if (status != expectedStatus || order.ConcurrencyStamp != plan.ConcurrencyStamp)
                {
                    throw new LifecycleRaceException();
                }

                PaymentEvidenceState payment = paid
                    ? await GetPaymentEvidenceStateAsync(order, token)
                    : PaymentEvidenceState.Missing;
                if (paid && payment == PaymentEvidenceState.Missing)
                {
                    return Failure(orderId, order, "Registration order has no exact reconciled successful payment.");
                }

                if (paid && payment is PaymentEvidenceState.Mismatch or PaymentEvidenceState.Duplicate)
                {
                    if (status == RegistrationOrderStatusEnum.AwaitingPayment &&
                        !await transitions.PersistAsync(
                            order.Id,
                            tenantId,
                            RegistrationOrderStatusEnum.AwaitingPayment,
                            RegistrationOrderStatusEnum.NeedsReconciliation,
                            now,
                            token))
                    {
                        throw new LifecycleRaceException();
                    }

                    await inventory.SaveChangesAsync(token);
                    string code = payment == PaymentEvidenceState.Duplicate
                        ? SucceededPaymentLookupResult.DuplicateCode
                        : "payment_composition_mismatch";
                    return Success(order, RegistrationOrderStatusEnum.NeedsReconciliation, code);
                }

                if (order.RegistrationWorkflowVersionId.HasValue &&
                    !await finalization.AreMandatoryRequirementsFulfilledAsync(tenantId, orderId, token))
                {
                    return Failure(orderId, order, "Registration order still has mandatory requirements.");
                }

                if (order.PurchaserActorId.HasValue)
                {
                    EventTicketCatalogVersion catalog = await GetPinnedCatalogAsync(order, tenantId, token);
                    EventTicketType[] ticketTypes = ResolveTicketTypes(order, catalog);
                    IReadOnlyDictionary<Guid, RegistrationTicketLimitUsage> usage = await inventory.GetTicketLimitUsageAsync(
                        order.EventId,
                        order.TenantId,
                        accountUserId: null,
                        verifiedContactNormalizedEmail: null,
                        order.PurchaserActorId,
                        ticketTypes.Select(ticket => ticket.Id).ToArray(),
                        token);
                    if (ticketTypes.Any(ticket => ticket.PerBookingPartyLimit is int limit &&
                            usage.GetValueOrDefault(ticket.Id)?.BookingPartyQuantity > limit))
                    {
                        return Failure(order.Id, order, "Registration order exceeds its booking-party ticket limit.");
                    }
                }

                PromotionReservation? activePromotion = await LoadActivePromotionForUpdateAsync(order, token);
                IReadOnlyList<RegistrationInventoryHold> holds = await inventory.GetHoldsByOrderAsync(order.Id, tenantId, token);
                RegistrationInventoryHold[] activeHolds = holds
                    .Where(hold => hold.RegistrationInventoryHoldStatusId == (int)RegistrationInventoryHoldStatusEnum.Active)
                    .ToArray();
                if (paid && !HasValidActiveHolds(activeHolds, plan.CapacityReservations, now))
                {
                    RegistrationInventoryReservationResult reservation = await inventory.ReserveRecoveredHoldsAsync(
                        order.EventId,
                        tenantId,
                        plan.CapacityReservations.Reservations,
                        now,
                        token);
                    if (!reservation.Reserved)
                    {
                        if (!await transitions.PersistAsync(
                                order.Id,
                                tenantId,
                                expectedStatus,
                                RegistrationOrderStatusEnum.NeedsReconciliation,
                                now,
                                token))
                        {
                            throw new LifecycleRaceException();
                        }

                        await inventory.SaveChangesAsync(token);
                        return Success(order, RegistrationOrderStatusEnum.NeedsReconciliation,
                            "Captured payment needs operator reconciliation because capacity is unavailable.");
                    }

                    holds = await inventory.GetHoldsByOrderAsync(order.Id, tenantId, token);
                    activeHolds = holds
                        .Where(hold => hold.RegistrationInventoryHoldStatusId == (int)RegistrationInventoryHoldStatusEnum.Active)
                        .ToArray();
                }

                if (!await transitions.PersistAsync(
                        order.Id,
                        tenantId,
                        expectedStatus,
                        RegistrationOrderStatusEnum.NeedsReconciliation,
                        now,
                        token))
                {
                    throw new LifecycleRaceException();
                }

                ConsumeActivePromotion(activePromotion, now);
                await LockCapacityPoolsAsync(order, activeHolds, token);
                if (!HasValidActiveHolds(activeHolds, plan.CapacityReservations, now)
                    || await inventory.TryConsumeActiveHoldsForOrderAsync(order.Id, tenantId, now, token) != activeHolds.Length)
                {
                    throw new LifecycleRaceException();
                }

                await participants.AddParticipantsAsync(plan.Placeholders, token);
                await inventory.AddEventRegistrationsAsync(plan.Admissions, token);
                await outbox.Create(plan.OutboxMessage);
                if (!await transitions.PersistAsync(
                        order.Id,
                        tenantId,
                        RegistrationOrderStatusEnum.NeedsReconciliation,
                        RegistrationOrderStatusEnum.Confirmed,
                        now,
                        token))
                {
                    throw new LifecycleRaceException();
                }

                await inventory.SaveChangesAsync(token);

                return Success(order, RegistrationOrderStatusEnum.Confirmed, "Registration order confirmed.");
            }, cancellationToken);
        }
        catch (LifecycleRaceException)
        {
            RegistrationOrder? current = await inventory.GetOrderWithLinesAsync(orderId, tenantId, cancellationToken);
            if (current is not null &&
                (RegistrationOrderStatusEnum)current.RegistrationOrderStatusId == RegistrationOrderStatusEnum.Confirmed)
            {
                return paid && await GetPaymentEvidenceStateAsync(current, cancellationToken) == PaymentEvidenceState.Duplicate
                    ? Success(current, RegistrationOrderStatusEnum.Confirmed, SucceededPaymentLookupResult.DuplicateCode)
                    : Success(current, RegistrationOrderStatusEnum.Confirmed, "Registration order is already confirmed.");
            }

            return Failure(orderId, current, "Registration order finalization could not reserve its held inventory.");
        }
    }


    private async Task<RegistrationOrderLifecycleResponseDto> RecoverExpiredHoldCoreAsync(
        Guid orderId,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        RegistrationOrder? initialOrder = await inventory.GetOrderWithLinesAsync(orderId, tenantId, cancellationToken);
        if (initialOrder is null)
        {
            return Missing(orderId);
        }

        if ((RegistrationOrderStatusEnum)initialOrder.RegistrationOrderStatusId != RegistrationOrderStatusEnum.NeedsReconciliation)
        {
            return Success(initialOrder, (RegistrationOrderStatusEnum)initialOrder.RegistrationOrderStatusId, "Registration order hold recovery was already resolved.");
        }

        CapacityReservationPlan plan = await PrepareCapacityReservationPlanAsync(initialOrder, cancellationToken);
        try
        {
            return await unitOfWork.ExecuteSerializableAsync(async token =>
            {
                RegistrationOrder? order = await inventory.GetOrderForUpdateWithLinesAsync(orderId, tenantId, token);
                if (order is null)
                {
                    return Missing(orderId);
                }

                RegistrationOrderStatusEnum status = (RegistrationOrderStatusEnum)order.RegistrationOrderStatusId;
                if (status != RegistrationOrderStatusEnum.NeedsReconciliation)
                {
                    return Success(order, status, "Registration order hold recovery was already resolved.");
                }

                PaymentEvidenceState payment = await GetPaymentEvidenceStateAsync(order, token);
                if (payment != PaymentEvidenceState.Missing)
                {
                    await LoadActivePromotionForUpdateAsync(order, token);
                    if (payment is PaymentEvidenceState.Mismatch or PaymentEvidenceState.Duplicate)
                    {
                        string code = payment == PaymentEvidenceState.Duplicate
                            ? SucceededPaymentLookupResult.DuplicateCode
                            : "payment_composition_mismatch";
                        return Success(order, RegistrationOrderStatusEnum.NeedsReconciliation, code);
                    }

                    IReadOnlyList<RegistrationInventoryHold> paidHolds = await inventory.GetHoldsByOrderAsync(
                        order.Id, order.TenantId, token);
                    RegistrationInventoryHold[] paidActiveHolds = paidHolds
                        .Where(hold => hold.RegistrationInventoryHoldStatusId == (int)RegistrationInventoryHoldStatusEnum.Active)
                        .ToArray();
                    if (!HasValidActiveHolds(paidActiveHolds, plan, now))
                    {
                        RegistrationInventoryReservationResult paidReservation = await inventory.ReserveRecoveredHoldsAsync(
                            order.EventId,
                            order.TenantId,
                            plan.Reservations,
                            now,
                            token);
                        if (!paidReservation.Reserved || paidReservation.RequiresApproval)
                        {
                            return Success(order, RegistrationOrderStatusEnum.NeedsReconciliation,
                                "Captured payment remains parked while capacity or approval is unavailable.");
                        }
                    }

                    await finalization.RequestAsync(order, now, token);
                    await inventory.SaveChangesAsync(token);
                    return Success(order, RegistrationOrderStatusEnum.NeedsReconciliation,
                        "Captured payment capacity was recovered and paid finalization was requeued.");
                }

                await ReleaseActivePromotionAsync(order, now, token);

                RegistrationInventoryReservationResult reservation = await inventory.ReserveRecoveredHoldsAsync(
                    order.EventId,
                    order.TenantId,
                    plan.Reservations,
                    now,
                    token);
                if (reservation.RequiresApproval)
                {
                    throw new InvalidOperationException("Registration order hold recovery cannot bypass approval.");
                }

                RegistrationOrderStatusEnum destination = reservation.Reserved
                    ? RegistrationOrderStatusEnum.ReadyForCheckout
                    : reservation.ShouldWaitlist
                        ? RegistrationOrderStatusEnum.Waitlisted
                        : throw new CapacityUnavailableException();
                if (destination == RegistrationOrderStatusEnum.Waitlisted)
                {
                    await ReleaseActiveHoldsForWaitlistAsync(order, now, token, releasePromotion: false);
                }

                if (!await transitions.PersistAsync(
                        order.Id,
                        tenantId,
                        RegistrationOrderStatusEnum.NeedsReconciliation,
                        destination,
                        now,
                        token))
                {
                    throw new LifecycleRaceException();
                }

                await inventory.SaveChangesAsync(token);

                return Success(
                    order,
                    destination,
                    destination == RegistrationOrderStatusEnum.Waitlisted
                        ? "Registration order is waitlisted while recovered capacity is unavailable."
                        : "Registration order hold recovery re-reserved capacity.");
            }, cancellationToken);
        }
        catch (CapacityUnavailableException)
        {
            return Failure(orderId, initialOrder, "Registration capacity is unavailable for hold recovery.");
        }
        catch (LifecycleRaceException)
        {
            return await CurrentRecoveryOrConflictAsync(orderId, tenantId, cancellationToken);
        }
    }

    public Task<RegistrationOrderDto?> GetAsync(
        Guid orderId,
        Guid tenantId,
        CancellationToken cancellationToken) => reads.GetAsync(orderId, tenantId, cancellationToken);

    public Task<IReadOnlyList<RegistrationOrderDto>> GetByEventAsync(
        Guid eventId,
        Guid tenantId,
        CancellationToken cancellationToken) => reads.GetByEventAsync(eventId, tenantId, cancellationToken);

    private async Task<RegistrationOrderLifecycleResponseDto> EndAsync(
        Guid orderId,
        Guid tenantId,
        RegistrationOrderStatusEnum expectedStatus,
        RegistrationOrderStatusEnum desiredStatus,
        RegistrationInventoryHoldStatusEnum holdOutcome,
        string message,
        CancellationToken cancellationToken)
    {
        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        Guid outboxMessageId = Guid.CreateVersion7();
        return await unitOfWork.ExecuteSerializableAsync(async token =>
        {
            RegistrationOrder? order = await inventory.GetOrderForUpdateWithLinesAsync(orderId, tenantId, token);
            if (order is null)
            {
                return Missing(orderId);
            }

            RegistrationOrderStatusEnum status = (RegistrationOrderStatusEnum)order.RegistrationOrderStatusId;
            if (status == desiredStatus)
            {
                return Success(order, status, message);
            }

            if (status != expectedStatus || !await transitions.PersistAsync(order.Id, tenantId, expectedStatus, desiredStatus, now, token))
            {
                return await CurrentOrConflictAsync(orderId, tenantId, "Registration order changed before its decision was recorded.", token);
            }

            await ReleaseActivePromotionAsync(order, now, token);
            await LockActiveHoldCapacityPoolsAsync(order, token);
            await inventory.TryReleaseActiveHoldsForOrderAsync(order.Id, tenantId, holdOutcome, now, token);
            await outbox.Create(RegistrationOrderOutboxMessageFactory.Create(outboxMessageId, order, desiredStatus, now));
            await inventory.SaveChangesAsync(token);
            return Success(order, desiredStatus, message);
        }, cancellationToken);
    }

    private Task<PromotionReservation?> LoadActivePromotionForUpdateAsync(
        RegistrationOrder order,
        CancellationToken cancellationToken) =>
        promotions.GetActiveReservationForUpdateAsync(order.TenantId, order.Id, cancellationToken);

    private async Task<CapacityReservationPlan> PrepareCapacityReservationPlanAsync(
        RegistrationOrder order,
        CancellationToken cancellationToken)
    {
        EventTicketCatalogVersion? catalog = await GetPinnedCatalogAsync(order, order.TenantId, cancellationToken);
        EventTicketType[] ticketTypes = ResolveTicketTypes(order, catalog);
        return CreateCapacityReservationPlan(order, ticketTypes);
    }

    private async Task<bool> RequiresApprovalAsync(
        RegistrationOrder order,
        IReadOnlyCollection<EventTicketType> ticketTypes,
        CancellationToken cancellationToken)
    {
        if (ticketTypes.Any(ticketType => ticketType.RequiresApproval))
        {
            return true;
        }

        List<EventSession> sessions = await eventSessions.GetSessionsByEvent(order.EventId);
        return ResolveEntitledSessions(ticketTypes, sessions).Any(session => session.RegistrationModeId == (int)RegistrationModeEnum.ApprovalRequired);
    }

    private async Task<RegistrationOrderLifecycleResponseDto> TransitionForApprovalAsync(
        RegistrationOrder order,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (!await transitions.PersistAsync(
                order.Id,
                order.TenantId,
                RegistrationOrderStatusEnum.AwaitingRequirements,
                RegistrationOrderStatusEnum.AwaitingApproval,
                now,
                cancellationToken))
        {
            throw new LifecycleRaceException();
        }

        return Success(order, RegistrationOrderStatusEnum.AwaitingApproval, "Registration order submitted for approval.");
    }

    private async Task<RegistrationOrderLifecycleResponseDto> RouteToPaymentAsync(
        RegistrationOrder order,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (!await transitions.PersistAsync(
                order.Id,
                order.TenantId,
                RegistrationOrderStatusEnum.ReadyForCheckout,
                RegistrationOrderStatusEnum.AwaitingPayment,
                now,
                cancellationToken))
        {
            throw new LifecycleRaceException();
        }

        return Success(order, RegistrationOrderStatusEnum.AwaitingPayment, "Registration order is awaiting payment.");
    }

    private Task<RegistrationInventoryReservationResult> ReserveCapacityAsync(
        RegistrationOrder order,
        CapacityReservationPlan plan,
        bool approvalGranted,
        DateTime now,
        CancellationToken cancellationToken) =>
        plan.Reservations.Count == 0
            ? Task.FromResult(new RegistrationInventoryReservationResult(Reserved: true, RequiresApproval: false, ShouldWaitlist: false))
            : inventory.ReserveNonTimedHoldsAsync(
                order.EventId,
                order.TenantId,
                plan.Reservations,
                approvalGranted,
                now,
                cancellationToken);

    private async Task ReleaseActiveHoldsForWaitlistAsync(
        RegistrationOrder order,
        DateTime now,
        CancellationToken cancellationToken,
        bool releasePromotion = true)
    {
        if (releasePromotion)
        {
            await ReleaseActivePromotionAsync(order, now, cancellationToken);
        }

        IReadOnlyList<RegistrationInventoryHold> holds = await inventory.GetHoldsByOrderAsync(
            order.Id,
            order.TenantId,
            cancellationToken);
        int activeHoldCount = holds.Count(hold =>
            hold.RegistrationInventoryHoldStatusId == (int)RegistrationInventoryHoldStatusEnum.Active);
        if (activeHoldCount == 0)
        {
            return;
        }

        await LockCapacityPoolsAsync(order, holds, cancellationToken);
        int releasedHoldCount = await inventory.TryReleaseActiveHoldsForOrderAsync(
            order.Id,
            order.TenantId,
            RegistrationInventoryHoldStatusEnum.Released,
            now,
            cancellationToken);
        if (releasedHoldCount != activeHoldCount)
        {
            throw new LifecycleRaceException();
        }
    }

    private async Task LockActiveHoldCapacityPoolsAsync(
        RegistrationOrder order,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<RegistrationInventoryHold> holds = await inventory.GetHoldsByOrderAsync(
            order.Id,
            order.TenantId,
            cancellationToken);
        await LockCapacityPoolsAsync(order, holds, cancellationToken);
    }

    private async Task LockCapacityPoolsAsync(
        RegistrationOrder order,
        IEnumerable<RegistrationInventoryHold> holds,
        CancellationToken cancellationToken)
    {
        Guid[] capacityPoolIds = holds
            .Where(hold => hold.RegistrationInventoryHoldStatusId == (int)RegistrationInventoryHoldStatusEnum.Active)
            .Select(hold => hold.CapacityPoolId)
            .ToArray();
        await inventory.GetPoolsForUpdateAsync(capacityPoolIds, order.EventId, order.TenantId, cancellationToken);
    }

    private static void ConsumeActivePromotion(PromotionReservation? reservation, DateTime now)
    {
        if (reservation is not null && !reservation.TryConsume(now))
        {
            throw new LifecycleRaceException();
        }
    }

    private async Task ReleaseActivePromotionAsync(
        RegistrationOrder order,
        DateTime now,
        CancellationToken cancellationToken)
    {
        PromotionReservation? reservation = await promotions.GetActiveReservationForUpdateAsync(
            order.TenantId,
            order.Id,
            cancellationToken);
        if (reservation is not null && !reservation.TryRelease(now))
        {
            throw new LifecycleRaceException();
        }
    }

    private static CapacityReservationPlan CreateCapacityReservationPlan(
        RegistrationOrder order,
        IEnumerable<EventTicketType> ticketTypes)
    {
        var ticketsById = ticketTypes.ToDictionary(ticketType => ticketType.Id);
        RegistrationInventoryReservation[] reservations = order.Lines
            .OrderBy(line => line.Id)
            .Where(line => ticketsById[line.TicketTypeId].CapacityPoolId.HasValue)
            .Select(line => new RegistrationInventoryReservation(
                Guid.CreateVersion7(),
                order.Id,
                ticketsById[line.TicketTypeId].CapacityPoolId!.Value,
                line.TicketTypeId,
                line.Quantity))
            .ToArray();
        return new CapacityReservationPlan(ticketsById.Values.ToArray(), reservations);
    }

    private static bool HasValidActiveHolds(
        IReadOnlyList<RegistrationInventoryHold> holds,
        CapacityReservationPlan plan,
        DateTime now)
    {
        if (plan.Reservations.Count == 0)
        {
            return holds.Count == 0;
        }

        if (holds.Count != plan.Reservations.Count || holds.Any(hold =>
                hold.RegistrationInventoryHoldStatusId != (int)RegistrationInventoryHoldStatusEnum.Active ||
                hold.ExpiresAt <= now) ||
            holds.GroupBy(hold => hold.TicketTypeId).Any(group => group.Count() != 1))
        {
            return false;
        }

        var holdsByTicketTypeId = holds.ToDictionary(hold => hold.TicketTypeId);
        return plan.Reservations.All(reservation =>
            holdsByTicketTypeId.TryGetValue(reservation.TicketTypeId, out RegistrationInventoryHold? hold) &&
            hold.CapacityPoolId == reservation.CapacityPoolId &&
            hold.Quantity == reservation.Quantity);
    }

    private async Task<FinalizationPlan> PrepareFinalizationAsync(
        RegistrationOrder order,
        DateTime now,
        CancellationToken cancellationToken)
    {
        EventTicketCatalogVersion? catalog = await GetPinnedCatalogAsync(order, order.TenantId, cancellationToken);
        EventTicketType[] ticketTypes = ResolveTicketTypes(order, catalog);
        IReadOnlyList<RegistrationTicketAssignment> assignments =
            await participants.GetAssignmentsWithParticipantsByOrderAsync(order.Id, order.TenantId, cancellationToken);
        List<EventSession> sessions = await eventSessions.GetSessionsByEvent(order.EventId);
        List<EventSession> entitledSessions = ResolveEntitledSessions(ticketTypes, sessions);
        if (entitledSessions.Count == 0)
        {
            throw new InvalidOperationException("Registration order has no materializable session admissions.");
        }

        if (entitledSessions.Any(session => session.RegistrationModeId is not (int)RegistrationModeEnum.Open and not (int)RegistrationModeEnum.ApprovalRequired))
        {
            throw new InvalidOperationException("Registration order includes a session that is not accepting admissions.");
        }

        var ticketTypesById = ticketTypes.ToDictionary(ticketType => ticketType.Id);
        var sessionsById = sessions.ToDictionary(session => session.Id);
        var orderLinesById = order.Lines.ToDictionary(line => line.Id);
        if (assignments.Any(assignment => assignment.TenantId != order.TenantId ||
                assignment.RegistrationOrderId != order.Id ||
                !orderLinesById.ContainsKey(assignment.RegistrationOrderLineId)))
        {
            throw new InvalidOperationException("Ticket assignments do not belong to this registration order.");
        }

        var placeholders = new List<RegistrationParticipant>();
        var admissions = new List<EventRegistration>();
        foreach (RegistrationOrderLine line in order.Lines.OrderBy(line => line.Id))
        {
            EventTicketType ticketType = ticketTypesById[line.TicketTypeId];
            RegistrationTicketAssignment[] lineAssignments = assignments
                .Where(assignment => assignment.RegistrationOrderLineId == line.Id)
                .OrderBy(assignment => assignment.Ordinal)
                .ToArray();
            DateTime? assignmentDeadline = ResolveCommonAssignmentDeadline(lineAssignments);
            try
            {
                if (!RegistrationOrderRules.CanConfirmParticipantAssignments(
                        (ParticipantDataCollectionModeEnum)ticketType.ParticipantDataCollectionModeId,
                        line.Quantity,
                        lineAssignments,
                        assignmentDeadline,
                        now))
                {
                    throw new InvalidOperationException("Ticket participant assignments are incomplete.");
                }
            }
            catch (ArgumentException exception)
            {
                throw new InvalidOperationException(exception.Message, exception);
            }

            var assignmentsByOrdinal = lineAssignments.ToDictionary(assignment => assignment.Ordinal);
            for (int unitOrdinal = 1; unitOrdinal <= line.Quantity; unitOrdinal++)
            {
                RegistrationParticipant? participant = ResolveUnitParticipant(
                    order,
                    ticketType,
                    assignmentsByOrdinal.GetValueOrDefault(unitOrdinal),
                    placeholders);
                if (participant is null)
                {
                    continue;
                }

                foreach ((TicketTypeEntitlement entitlement, EventSession session) in
                         RegistrationAdmissionMaterializer.Expand(ticketType, sessions))
                {
                    admissions.Add(RegistrationAdmissionMaterializer.Create(
                        Guid.CreateVersion7(),
                        Guid.CreateVersion7(),
                        order,
                        line,
                        entitlement,
                        session,
                        participant,
                        unitOrdinal,
                        now));
                }
            }
        }

        CapacityReservationPlan capacityReservations = CreateCapacityReservationPlan(order, ticketTypes);
        return new FinalizationPlan(
            order.ConcurrencyStamp,
            placeholders,
            admissions,
            capacityReservations,
            RegistrationOrderOutboxMessageFactory.Create(
                Guid.CreateVersion7(),
                order,
                RegistrationOrderStatusEnum.Confirmed,
                now,
                admissions.Count));
    }

    private static DateTime? ResolveCommonAssignmentDeadline(
        IReadOnlyCollection<RegistrationTicketAssignment> assignments)
    {
        DateTime[] deadlines = assignments
            .Where(assignment => assignment.AssignmentStatusId == (int)AssignmentStatusEnum.Deferred)
            .Select(assignment => assignment.AssignmentDeadline ?? DateTime.MinValue)
            .Distinct()
            .ToArray();
        return deadlines.Length == 1 ? deadlines[0] : null;
    }

    private async Task<EventTicketCatalogVersion> GetPinnedCatalogAsync(
        RegistrationOrder order,
        Guid tenantId,
        CancellationToken cancellationToken) =>
        await catalogs.GetOrderCatalogAsync(order.TicketCatalogVersionId, order.EventId, tenantId, cancellationToken)
        ?? throw new InvalidOperationException("Registration order ticket catalog was not found.");

    private static EventTicketType[] ResolveTicketTypes(RegistrationOrder order, EventTicketCatalogVersion catalog)
    {
        var ticketTypes = catalog.TicketTypes
            .Where(ticketType => !ticketType.IsDeleted)
            .ToDictionary(ticketType => ticketType.Id);
        if (order.Lines.Any(line => !ticketTypes.ContainsKey(line.TicketTypeId)))
        {
            throw new InvalidOperationException("Registration order ticket selections no longer match its pinned catalog.");
        }

        return order.Lines.Select(line => ticketTypes[line.TicketTypeId]).ToArray();
    }

    private static List<EventSession> ResolveEntitledSessions(
        IEnumerable<EventTicketType> ticketTypes,
        IReadOnlyCollection<EventSession> sessions) => ticketTypes
        .SelectMany(ticketType => ticketType.Entitlements)
        .SelectMany(entitlement => ResolveEntitlementSessions(entitlement, sessions))
        .DistinctBy(session => session.Id)
        .ToList();

    private static IEnumerable<EventSession> ResolveEntitlementSessions(
        TicketTypeEntitlement entitlement,
        IEnumerable<EventSession> sessions)
    {
        if ((EntitlementSelectionRuleEnum)entitlement.EntitlementSelectionRuleId is EntitlementSelectionRuleEnum.ChooseOne or EntitlementSelectionRuleEnum.ChooseUpToN)
        {
            throw new InvalidOperationException("Registration order requires a session selection before finalization.");
        }

        return (EntitlementScopeTypeEnum)entitlement.EntitlementScopeTypeId switch
        {
            EntitlementScopeTypeEnum.Event => sessions.Where(session => session.EventId == entitlement.TargetEventId),
            EntitlementScopeTypeEnum.EventDay => sessions.Where(session => session.EventDayId == entitlement.EventDayId),
            EntitlementScopeTypeEnum.EventSession => sessions.Where(session => session.Id == entitlement.EventSessionId),
            _ => throw new InvalidOperationException("Registration order entitlement scope is invalid.")
        };
    }

    private async Task<RegistrationOrderLifecycleResponseDto> CurrentOrConflictAsync(
        Guid orderId,
        Guid tenantId,
        string message,
        CancellationToken cancellationToken)
    {
        RegistrationOrder? current = await inventory.GetOrderWithLinesAsync(orderId, tenantId, cancellationToken);
        return current is null ? Missing(orderId) : Failure(orderId, current, message);
    }

    private async Task<RegistrationOrderLifecycleResponseDto> CurrentRecoveryOrConflictAsync(
        Guid orderId,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        RegistrationOrder? current = await inventory.GetOrderWithLinesAsync(orderId, tenantId, cancellationToken);
        return current is null
            ? Missing(orderId)
            : (RegistrationOrderStatusEnum)current.RegistrationOrderStatusId == RegistrationOrderStatusEnum.NeedsReconciliation
                ? Failure(orderId, current, "Registration order changed while hold recovery was recorded.")
                : Success(current, (RegistrationOrderStatusEnum)current.RegistrationOrderStatusId, "Registration order hold recovery was already resolved.");
    }

    private static RegistrationOrderLifecycleResponseDto Success(
        RegistrationOrder order,
        RegistrationOrderStatusEnum status,
        string message) => RegistrationOrderLifecycleResponseDto.Success(
            order.Id, message, RegistrationOrderDto.From(order, status));

    private static RegistrationOrderLifecycleResponseDto Missing(Guid orderId) =>
        RegistrationOrderLifecycleResponseDto.Failure(BaseCommandResponse.NotFound(
            "Registration order was not found.", orderId));

    private static RegistrationOrderLifecycleResponseDto Failure(
        Guid orderId,
        RegistrationOrder? order,
        string error)
    {
        BaseCommandResponse<Guid> failure = BaseCommandResponse.Validation(
            [error], "Registration order lifecycle change failed.", orderId);
        return new RegistrationOrderLifecycleResponseDto(
            failure.Id,
            failure.IsSuccess,
            failure.Message,
            failure.Errors,
            failure.FailureCode,
            failure.QuotaExceeded,
            order is null ? null : RegistrationOrderDto.From(order));
    }

    private sealed record FinalizationPlan(
        Guid ConcurrencyStamp,
        IReadOnlyList<RegistrationParticipant> Placeholders,
        IReadOnlyList<EventRegistration> Admissions,
        CapacityReservationPlan CapacityReservations,
        OutboxMessage OutboxMessage);

    private sealed record CapacityReservationPlan(
        IReadOnlyCollection<EventTicketType> TicketTypes,
        IReadOnlyList<RegistrationInventoryReservation> Reservations);


    private sealed class LifecycleRaceException : Exception;

    private sealed class CapacityUnavailableException : Exception;
}
