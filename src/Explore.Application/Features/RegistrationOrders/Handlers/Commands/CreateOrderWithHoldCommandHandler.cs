// ABOUTME: Creates one order and its capacity holds under a serializable transaction with retry-stable identities.
// ABOUTME: Reserves capacity before PII, converts unavailable capacity to a waitlist, and performs no external I/O.

using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Scheduling;
using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.Services.Registration;
using Explore.Application.Features.RegistrationOrders.Requests.Commands;
using Explore.Application.Features.RegistrationOrders.Validators;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Application.Services;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.RegistrationOrders.Handlers.Commands;

public sealed class CreateOrderWithHoldCommandHandler(
    IEventRepository events,
    IEventTicketCatalogRepository catalogs,
    IRegistrationInventoryRepository inventory,
    IPlatformFeePolicyRepository feePolicies,
    IPlatformContributionSettingRepository contributionSettings,
    ITenantContext tenant,
    IOrganizerEarningsCalculator earningsCalculator,
    TimeProvider timeProvider,
    IScheduledDeadlineDispatcher deadlines,
    ILogger<CreateOrderWithHoldCommandHandler> logger,
    IUnitOfWork unitOfWork,
    ISettingMutationLock mutationLock,
    IVisitorAccessCapabilityResolver visitorCapabilities) :
    IRequestHandler<CreateRegistrationOrderWithHoldCommand, BaseCommandResponse<Guid>>,
    IRegistrationOrderStarter
{
    public Task<BaseCommandResponse<Guid>> Handle(
        CreateRegistrationOrderWithHoldCommand request,
        CancellationToken cancellationToken) => StartAsync(request, cancellationToken);

    public async Task<BaseCommandResponse<Guid>> StartAsync(
        CreateRegistrationOrderWithHoldCommand request,
        CancellationToken cancellationToken)
    {
        var authority = request.ChallengeAuthority;
        if ((request.GuestAccessTokenHash is not null || authority is not null) &&
            (authority is null || !authority.Matches(request) || !AllowsRecovery(authority)))
        {
            return ChallengeInvalid(request.EventId);
        }

        // A verified original request can recover a committed allocation even after publication or
        // visitor policy changes. This read never reacquires capacity or refreshes a deadline.
        if (authority is not null &&
            await ReadCommittedGuestAsync(authority, cancellationToken) is { } committed)
        {
            return committed;
        }

        if (authority is not null && !authority.IsFresh(timeProvider.GetUtcNow()))
        {
            return ChallengeExpired(request.EventId);
        }

        var validation = await new CreateRegistrationOrderWithHoldCommandValidator()
            .ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Invalid(request.EventId, validation.Errors.Select(error => error.ErrorMessage));
        }

        Guid orderId = authority?.OrderId ?? Guid.CreateVersion7();
        DateTime createdAt = timeProvider.GetUtcNow().UtcDateTime;
        IReadOnlyDictionary<Guid, StableLineIds> stableLineIds = request.Lines
            .OrderBy(line => line.TicketTypeId)
            .ToDictionary(
                line => line.TicketTypeId,
                _ => new StableLineIds(Guid.CreateVersion7(), Guid.CreateVersion7()));

        // Captured inside the transaction, acted on only after it commits: scheduling a wake-up for an
        // order that a rollback then erased would leave a trigger pointing at nothing.
        DateTime? earliestHoldExpiry = null;

        try
        {
            BaseCommandResponse<Guid> response = await mutationLock.ExecuteOrderedGroupsAsync(
                [VisitorAccessCapabilityResolver.AuthoritySettingKeys],
                outerToken => unitOfWork.ExecuteSerializableAsync(async token =>
            {
                earliestHoldExpiry = null;
                if (authority is not null)
                {
                    if (!AllowsRecovery(authority))
                    {
                        return ChallengeInvalid(request.EventId);
                    }

                    // Another owner may have committed while this request waited for the ordered lease.
                    if (await ReadCommittedGuestAsync(authority, token) is { } recovered)
                    {
                        return recovered;
                    }

                    if (!authority.IsFresh(timeProvider.GetUtcNow()))
                    {
                        return ChallengeExpired(request.EventId);
                    }
                }

                Event? eventTarget = request.GuestAccessTokenHash is not null
                    ? await events.GetRegistrationStatusEventForUpdateAsync(request.EventId, tenant.TenantId, token)
                    : await events.GetAuthorizationTargetByIdAsync(request.EventId, token);
                if (eventTarget is null || eventTarget.TenantId != tenant.TenantId ||
                    eventTarget.ParticipationConfiguration is not
                    {
                        ParticipationHandlingModeId: (int)ParticipationHandlingModeEnum.PlatformManaged
                    } participationConfiguration)
                {
                    return Missing(request.EventId);
                }

                if (authority is not null &&
                    !await events.IsPubliclyEligibleAsync(tenant.TenantId, request.EventId, token))
                {
                    return Missing(request.EventId);
                }

                var capability = await visitorCapabilities.ResolveAsync(tenant.TenantId, token);
                if (!capability.AllowsNewNativeAllocation)
                {
                    return BaseCommandResponse.Failure<Guid>("registration_order_visitor_allocation_disabled",
                        "New native registration allocations are disabled.", id: request.EventId);
                }

                if (!IsIdentityAccessAllowed(participationConfiguration.IdentityAccessModeId, request))
                {
                    return IdentityRequired(request.EventId);
                }

                RegistrationParticipationSnapshot participation = RegistrationParticipationSnapshot.From(participationConfiguration);
                RegistrationOrder? existing = await inventory.GetOrderByIdAsync(orderId, tenant.TenantId, token);
                if (existing is not null)
                {
                    return authority is null
                        ? Success(existing.Id, "Registration order already created.")
                        : ChallengeInvalid(request.EventId);
                }

                EventTicketCatalogVersion? catalog = await catalogs.GetPublishedCatalogAsync(
                    request.EventId,
                    tenant.TenantId,
                    token);
                if (catalog is null || catalog.Id != request.TicketCatalogVersionId)
                {
                    return Missing(request.EventId);
                }

                Dictionary<Guid, EventTicketType> ticketTypes = catalog.TicketTypes
                    .Where(ticketType => !ticketType.IsDeleted)
                    .ToDictionary(ticketType => ticketType.Id);
                if (request.Lines.Any(line => !ticketTypes.ContainsKey(line.TicketTypeId)))
                {
                    return Missing(request.EventId);
                }

                PlatformFeePolicy? feePolicy = await feePolicies.GetActiveAsync(token);
                var preparedLines = request.Lines
                    .OrderBy(line => line.TicketTypeId)
                    .Select(line => new PreparedLine(line, ticketTypes[line.TicketTypeId], stableLineIds[line.TicketTypeId]))
                    .ToArray();
                if (preparedLines.Any(line => line.Selection.Quantity > line.Ticket.PerOrderLimit))
                {
                    return Invalid(request.EventId, "A ticket quantity exceeds its per-order limit.");
                }

                IReadOnlyDictionary<Guid, RegistrationTicketLimitUsage> usageByTicket = await inventory.GetTicketLimitUsageAsync(
                    request.EventId,
                    tenant.TenantId,
                    request.AccountUserId,
                    NormalizeVerifiedContact(request.VerifiedContactNormalizedEmail),
                    request.PurchaserActorId,
                    preparedLines.Select(line => line.Ticket.Id).ToArray(),
                    token);
                if (TryGetLimitViolation(request, preparedLines, usageByTicket, out string? limitError))
                {
                    return LimitExceeded(request.EventId, limitError!);
                }

                Guid[] poolIds = preparedLines
                    .Where(line => line.Ticket.CapacityPoolId.HasValue)
                    .Select(line => line.Ticket.CapacityPoolId!.Value)
                    .Distinct()
                    .Order()
                    .ToArray();
                IReadOnlyList<EventCapacityPool> pools = await inventory.GetPoolsForUpdateAsync(
                    poolIds,
                    request.EventId,
                    tenant.TenantId,
                    token);
                Dictionary<Guid, EventCapacityPool> poolsById = pools.ToDictionary(pool => pool.Id);
                if (poolsById.Count != poolIds.Length || poolsById.Values.Any(pool => !pool.IsActive))
                {
                    return Missing(request.EventId);
                }

                if (!TryGetHoldPolicies(poolsById, out Dictionary<Guid, CapacityHoldPolicyEnum> holdPolicies))
                {
                    return Invalid(request.EventId, "Capacity pool hold policy is invalid.");
                }

                bool reservesCapacityOnSelection = holdPolicies.Values.Any(ReservesCapacityOnSelection);
                IReadOnlySet<Guid> fullPoolIds = await GetFullPoolIdsAsync(preparedLines, poolsById, holdPolicies, token);
                if (fullPoolIds.Any(poolId => holdPolicies[poolId] == CapacityHoldPolicyEnum.TimedHoldOnSelection))
                {
                    return CapacityUnavailable(request.EventId);
                }

                bool isWaitlisted = fullPoolIds.Any(poolId => holdPolicies[poolId] == CapacityHoldPolicyEnum.WaitlistWhenFull);
                bool submitsForApproval = holdPolicies.Values.Any(policy => policy == CapacityHoldPolicyEnum.ApprovalNoHold);
                if (isWaitlisted && submitsForApproval)
                {
                    return IncompatiblePolicy(request.EventId);
                }

                // Pool row fences and catalog reads may wait. A historical proof never starts new effects.
                if (authority is not null && !authority.IsFresh(timeProvider.GetUtcNow()))
                {
                    return ChallengeExpired(request.EventId);
                }

                DateTime? expiresAt = !isWaitlisted && reservesCapacityOnSelection
                    ? poolsById.Values
                        .Where(pool => holdPolicies[pool.Id] == CapacityHoldPolicyEnum.TimedHoldOnSelection)
                        .Min(pool => createdAt.AddSeconds(pool.HoldDurationSeconds))
                    : null;
                RegistrationOrder order = RegistrationOrder.Create(
                    orderId,
                    tenant.TenantId,
                    request.EventId,
                    request.AccountUserId,
                    request.PurchaserActorId,
                    request.BookingPartyType,
                    catalog.Id,
                    participation,
                    registrationWorkflowVersionId: null,
                    request.GuestAccessTokenHash,
                    catalog.CurrencyCode,
                    createdAt,
                    expiresAt);

                if (request.GuestAccessTokenHash is not null &&
                    !order.TryEstablishGuestStatusPromise(eventTarget.LastSessionEndUtc, timeProvider.GetUtcNow().UtcDateTime))
                {
                    return BaseCommandResponse.Failure<Guid>("registration_order_finite_status_window_required",
                        "Guest registration requires a finite event status window.", id: request.EventId);
                }

                foreach (PreparedLine preparedLine in preparedLines)
                {
                    order.AddLine(RegistrationOrderLine.Create(
                        preparedLine.Ids.LineId,
                        catalog,
                        preparedLine.Ticket,
                        order.Id,
                        preparedLine.Selection.Quantity,
                        preparedLine.Selection.ChosenUnitPriceMinor,
                        feePolicy));
                }

                long lineTotal = order.Lines.Sum(line => line.LineSubtotalSnapshot);
                if (request.PlatformContributionBasisPoints is > 0)
                {
                    if (lineTotal == 0)
                    {
                        return Invalid(request.EventId, "Platform contributions require an existing payable order total.");
                    }

                    PlatformContributionSetting? contributionSetting = await contributionSettings.GetActiveAsync(token);
                    if (contributionSetting is null || !contributionSetting.IsEnabled)
                    {
                        return Invalid(request.EventId, "Platform contributions are not enabled.");
                    }

                    order.SetPlatformContribution(RegistrationOrderPlatformContribution.CreateOrNull(
                        order.Id,
                        order.TenantId,
                        contributionSetting,
                        request.PlatformContributionBasisPoints.Value,
                        lineTotal,
                        catalog.CurrencyCode));
                }

                OrganizerEarnings earnings = earningsCalculator.Calculate(catalog.CurrencyCode, lineTotal, feePolicy);
                order.ApplyTotals(RegistrationOrderTotalsSnapshot.Create(
                    catalog.CurrencyCode,
                    earnings.OrganizerDirectedTotalMinor,
                    earnings.PlatformFeeMinor,
                    earnings.OrganizerEarningsMinor,
                    order.PlatformContribution?.AmountMinor ?? 0));

                RegistrationInventoryHold[] holds = isWaitlisted || !reservesCapacityOnSelection
                    ? []
                    : CreateHolds(order, preparedLines, poolsById, holdPolicies, createdAt);
                TransitionForCreation(
                    order,
                    isWaitlisted,
                    submitsForApproval,
                    createdAt);
                if (authority is not null && !authority.IsFresh(timeProvider.GetUtcNow()))
                {
                    return ChallengeExpired(request.EventId);
                }

                await inventory.AddOrderWithHoldsAsync(order, holds, token);
                await inventory.SaveChangesAsync(token);
                earliestHoldExpiry = holds.Length == 0 ? null : holds.Min(hold => hold.ExpiresAt);
                return Success(order.Id, isWaitlisted ? "Registration order waitlisted." : "Registration order created.");
            }, outerToken), cancellationToken);

            if (response.IsSuccess && earliestHoldExpiry is not null)
            {
                await RegisterHoldExpiryDeadlineAsync(orderId, earliestHoldExpiry.Value, cancellationToken);
            }

            return authority is not null && response.IsSuccess && !AllowsRecovery(authority)
                ? ChallengeInvalid(request.EventId)
                : response;
        }
        catch (ArgumentException exception)
        {
            return Invalid(request.EventId, exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return Invalid(request.EventId, exception.Message);
        }
    }

    public async Task<BaseCommandResponse<Guid>?> TryRecoverCommittedGuestAsync(
        StartGuestRegistrationOrderCommand request,
        CancellationToken cancellationToken)
    {
        var authority = request.ChallengeAuthority;
        if (authority is null || !authority.Matches(request) || !AllowsRecovery(authority))
        {
            return ChallengeInvalid(request.EventId);
        }

        return await ReadCommittedGuestAsync(authority, cancellationToken);
    }

    private bool AllowsRecovery(AnonymousRegistrationChallengeAuthority authority) =>
        authority.TenantId == tenant.TenantId && timeProvider.GetUtcNow() < authority.RecoverUntil;

    private async Task<BaseCommandResponse<Guid>?> ReadCommittedGuestAsync(
        AnonymousRegistrationChallengeAuthority authority,
        CancellationToken cancellationToken)
    {
        RegistrationOrder? order = await inventory.GetExactGuestOrderAsync(
            authority.OrderId, tenant.TenantId, authority.EventId, authority.GuestAccessTokenHash, cancellationToken);
        if (!AllowsRecovery(authority))
        {
            return ChallengeInvalid(authority.EventId);
        }

        return order is null ? null : Success(order.Id, "Registration order already created.");
    }

    private static BaseCommandResponse<Guid> ChallengeInvalid(Guid eventId) => BaseCommandResponse.Failure<Guid>(
        "registration_order_challenge_invalid", "Registration challenge is invalid.", id: eventId);

    private static BaseCommandResponse<Guid> ChallengeExpired(Guid eventId) => BaseCommandResponse.Failure<Guid>(
        "registration_order_challenge_expired", "Registration challenge has expired.", id: eventId);

    /// <summary>
    /// Asks the scheduler to wake at the order's earliest hold expiry so held capacity returns to sale at
    /// its deadline instead of on the next sweep.
    /// <para>
    /// This is an optimization and is treated as one. The order is already committed by the time this runs,
    /// so a scheduler outage must not surface as a failed order creation on the ticketing path — and it does
    /// not need to, because <c>inventory-hold-expiry-reconciliation</c> still finds the order. A failure
    /// costs latency, never work.
    /// </para>
    /// </summary>
    private async Task RegisterHoldExpiryDeadlineAsync(
        Guid orderId,
        DateTime earliestHoldExpiry,
        CancellationToken cancellationToken)
    {
        ScheduledDeadlineResult result = await deadlines.ScheduleAsync(
            new ScheduledDeadline(
                ScheduledJobNames.InventoryHoldExpiry,
                InventoryHoldDeadline.KeyFor(orderId),
                new DateTimeOffset(DateTime.SpecifyKind(earliestHoldExpiry, DateTimeKind.Utc)),
                InventoryHoldDeadline.PointerFor(tenant.TenantId, orderId)),
            cancellationToken);

        if (!result.Scheduled)
        {
            logger.LogInformation(
                "Hold-expiry deadline was not scheduled for order {OrderId}; the reconciliation sweep covers it. FailureCategory={FailureCategory}",
                orderId,
                result.FailureCategory);
        }
    }

    private async Task<IReadOnlySet<Guid>> GetFullPoolIdsAsync(
        IReadOnlyCollection<PreparedLine> lines,
        Dictionary<Guid, EventCapacityPool> poolsById,
        IReadOnlyDictionary<Guid, CapacityHoldPolicyEnum> holdPolicies,
        CancellationToken cancellationToken)
    {
        var fullPoolIds = new HashSet<Guid>();
        foreach (IGrouping<Guid, PreparedLine> group in lines
            .Where(line => line.Ticket.CapacityPoolId.HasValue)
            .Where(line => ChecksCapacityOnSelection(holdPolicies[line.Ticket.CapacityPoolId!.Value]))
            .GroupBy(line => line.Ticket.CapacityPoolId!.Value)
            .OrderBy(group => group.Key))
        {
            EventCapacityPool pool = poolsById[group.Key];
            if (pool.MaximumQuantity is null ||
                pool.CapacityOversellPolicyId == (int)CapacityOversellPolicyEnum.Allow)
            {
                continue;
            }

            int allocated = await inventory.GetAllocatedQuantityAsync(pool.Id, tenant.TenantId, cancellationToken);
            int requested = checked(group.Sum(line => line.Selection.Quantity));
            if (allocated > pool.MaximumQuantity.Value - requested)
            {
                fullPoolIds.Add(pool.Id);
            }
        }

        return fullPoolIds;
    }

    private static RegistrationInventoryHold[] CreateHolds(
        RegistrationOrder order,
        IEnumerable<PreparedLine> lines,
        Dictionary<Guid, EventCapacityPool> poolsById,
        IReadOnlyDictionary<Guid, CapacityHoldPolicyEnum> holdPolicies,
        DateTime createdAt) => lines
        .Where(line => line.Ticket.CapacityPoolId.HasValue)
        .Where(line => ReservesCapacityOnSelection(holdPolicies[line.Ticket.CapacityPoolId!.Value]))
        .Select(line =>
        {
            EventCapacityPool pool = poolsById[line.Ticket.CapacityPoolId!.Value];
            return RegistrationInventoryHold.Create(
                line.Ids.HoldId,
                order.Id,
                pool.Id,
                line.Ticket.Id,
                order.TenantId,
                line.Selection.Quantity,
                createdAt,
                createdAt.AddSeconds(pool.HoldDurationSeconds));
        })
        .ToArray();

    private static void TransitionForCreation(
        RegistrationOrder order,
        bool isFull,
        bool submitsForApproval,
        DateTime createdAt)
    {
        order.TransitionTo(RegistrationOrderStatusEnum.AwaitingParticipantDetails, createdAt);
        if (submitsForApproval)
        {
            order.TransitionTo(RegistrationOrderStatusEnum.AwaitingRequirements, createdAt);
            order.TransitionTo(RegistrationOrderStatusEnum.AwaitingApproval, createdAt);
            return;
        }

        if (!isFull)
        {
            return;
        }

        order.TransitionTo(RegistrationOrderStatusEnum.AwaitingRequirements, createdAt);
        order.TransitionTo(RegistrationOrderStatusEnum.ReadyForCheckout, createdAt);
        order.TransitionTo(RegistrationOrderStatusEnum.Waitlisted, createdAt);
    }

    private static bool TryGetHoldPolicies(
        IReadOnlyDictionary<Guid, EventCapacityPool> poolsById,
        out Dictionary<Guid, CapacityHoldPolicyEnum> holdPolicies)
    {
        holdPolicies = poolsById.ToDictionary(pool => pool.Key, pool => (CapacityHoldPolicyEnum)pool.Value.CapacityHoldPolicyId);
        return holdPolicies.Values.All(policy => Enum.IsDefined(policy));
    }

    private static bool ChecksCapacityOnSelection(CapacityHoldPolicyEnum policy) => policy is
        CapacityHoldPolicyEnum.TimedHoldOnSelection or CapacityHoldPolicyEnum.WaitlistWhenFull;

    private static bool ReservesCapacityOnSelection(CapacityHoldPolicyEnum policy) =>
        policy == CapacityHoldPolicyEnum.TimedHoldOnSelection;

    private static bool TryGetLimitViolation(
        CreateRegistrationOrderWithHoldCommand request,
        IEnumerable<PreparedLine> lines,
        IReadOnlyDictionary<Guid, RegistrationTicketLimitUsage> usageByTicket,
        out string? error)
    {
        foreach (PreparedLine line in lines)
        {
            usageByTicket.TryGetValue(line.Ticket.Id, out RegistrationTicketLimitUsage? usage);
            int accountQuantity = usage?.AccountQuantity ?? 0;
            int verifiedContactQuantity = usage?.VerifiedContactQuantity ?? 0;
            int bookingPartyQuantity = usage?.BookingPartyQuantity ?? 0;

            if ((request.AccountUserId.HasValue && line.Ticket.PerAccountLimit is int accountLimit &&
                 accountQuantity > accountLimit - line.Selection.Quantity) ||
                (request.VerifiedContactNormalizedEmail is not null && line.Ticket.PerVerifiedContactLimit is int contactLimit &&
                 verifiedContactQuantity > contactLimit - line.Selection.Quantity) ||
                (request.PurchaserActorId.HasValue && line.Ticket.PerBookingPartyLimit is int bookingPartyLimit &&
                 bookingPartyQuantity > bookingPartyLimit - line.Selection.Quantity))
            {
                error = "A ticket quantity exceeds an account, verified-contact, or booking-party limit.";
                return true;
            }
        }

        error = null;
        return false;
    }

    private static string? NormalizeVerifiedContact(string? verifiedContactNormalizedEmail) =>
        string.IsNullOrWhiteSpace(verifiedContactNormalizedEmail)
            ? null
            : verifiedContactNormalizedEmail.Trim().ToUpperInvariant();

    private static bool IsIdentityAccessAllowed(
        int? identityAccessModeId,
        CreateRegistrationOrderWithHoldCommand request)
    {
        bool hasAccount = request.AccountUserId.HasValue;
        bool hasGuestCapability = request.GuestAccessTokenHash is not null;
        return (IdentityAccessModeEnum?)identityAccessModeId switch
        {
            IdentityAccessModeEnum.AccountRequired => hasAccount && !hasGuestCapability,
            IdentityAccessModeEnum.GuestAllowed or IdentityAccessModeEnum.CapabilityTokenAllowed =>
                hasAccount != hasGuestCapability,
            _ => false
        };
    }

    private static BaseCommandResponse<Guid> Success(Guid orderId, string message) => BaseCommandResponse.Success(orderId, message);

    private static BaseCommandResponse<Guid> Missing(Guid id) => BaseCommandResponse.Failure<Guid>(
        "registration_order_not_found", "Registration order configuration was not found.",
        ["Registration order configuration was not found."], id);

    private static BaseCommandResponse<Guid> Invalid(Guid id, string error) => Invalid(id, [error]);

    private static BaseCommandResponse<Guid> Invalid(Guid id, IEnumerable<string> errors) => BaseCommandResponse.Failure<Guid>(
        "registration_order_validation_failed", "Registration order configuration is invalid.", errors, id);

    private static BaseCommandResponse<Guid> IdentityRequired(Guid id) => BaseCommandResponse.Failure<Guid>(
        "registration_order_identity_required", "Registration order requires an authenticated account.",
        ["Registration order requires an authenticated account."], id);

    private static BaseCommandResponse<Guid> LimitExceeded(Guid id, string error) => BaseCommandResponse.Failure<Guid>(
        "registration_order_limit_exceeded", "Registration order quantity limit was exceeded.", [error], id);

    private static BaseCommandResponse<Guid> CapacityUnavailable(Guid id) => BaseCommandResponse.Failure<Guid>(
        "registration_order_capacity_unavailable", "Registration capacity is unavailable.",
        ["Registration capacity is unavailable."], id);

    private static BaseCommandResponse<Guid> IncompatiblePolicy(Guid id) => BaseCommandResponse.Failure<Guid>(
        "registration_order_policy_incompatible",
        "Registration capacity policies cannot produce one consistent order state.",
        ["A waitlisted order cannot also await approval."], id);

    private sealed record StableLineIds(Guid LineId, Guid HoldId);

    private sealed record PreparedLine(
        RegistrationOrderLineSelection Selection,
        EventTicketType Ticket,
        StableLineIds Ids);
}
