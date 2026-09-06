// ABOUTME: Proves PostgreSQL capacity locking prevents two ticket types from taking one shared last seat.
// ABOUTME: Uses independent DbContexts and serializable transactions against the real registration-hold repository.

using System.Diagnostics;
using System.Text.Json;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Payments;
using Explore.Application.Contracts.Scheduling;
using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.Services.Registration;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Services.Registration;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Npgsql;
using TUnit.Assertions;
using TUnit.Core;
using DomainEvent = Explore.Domain.Event;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class RegistrationInventoryHoldConcurrencyTests(PostgreSqlContainerFixture fixture)
{
    private static readonly DateTime UtcNow = new(2026, 7, 30, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task PostgreSqlCatalog_ContainsCapacityHoldPolicySeedsAndCapacityPoolForeignKey()
    {
        await fixture.ResetAsync();

        await Assert.That(await ScalarAsync(
            "SELECT COUNT(*) FROM capacity_hold_policies WHERE (id, master_code) IN ((1, 'NO_HOLD_UNTIL_READY'), (2, 'TIMED_HOLD_ON_SELECTION'), (3, 'APPROVAL_NO_HOLD'), (4, 'WAITLIST_WHEN_FULL'))"))
            .IsEqualTo(4L);
        await using ExploreDbContext context = fixture.CreateDbContext();
        string constraintName = context.Model.FindEntityType(typeof(EventCapacityPool))!
            .GetForeignKeys()
            .Single(foreignKey =>
                foreignKey.PrincipalEntityType.ClrType == typeof(CapacityHoldPolicy))
            .GetConstraintName()!;
        await Assert.That(await ScalarAsync(
            "SELECT COUNT(*) FROM pg_constraint WHERE conname = @constraint_name AND contype = 'f'",
            ("constraint_name", constraintName)))
            .IsEqualTo(1L);
    }

    [Test]
    public async Task SharedPoolLastSeat_TwoDifferentTicketTypesCreateAtMostOneActiveHold()
    {
        (Guid tenantId, Guid eventId, Guid poolId, Guid firstTicketTypeId, Guid secondTicketTypeId) = await SeedAsync();
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));

        Task<bool> firstReservation = ReserveAsync(tenantId, eventId, poolId, firstTicketTypeId, timeout.Token);
        Task<bool> secondReservation = ReserveAsync(tenantId, eventId, poolId, secondTicketTypeId, timeout.Token);
        bool[] reservations = await Task.WhenAll(firstReservation, secondReservation);

        await Assert.That(reservations.Count(result => result)).IsEqualTo(1);
        await using ExploreDbContext verificationContext = fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenantId));
        var verificationRepository = new RegistrationInventoryRepository(verificationContext);
        await Assert.That(await verificationRepository.GetAllocatedQuantityAsync(poolId, tenantId, timeout.Token)).IsEqualTo(1);
        await Assert.That(await verificationContext.RegistrationInventoryHolds
            .Where(hold => hold.CapacityPoolId == poolId)
            .Select(hold => hold.TicketTypeId)
            .Distinct()
            .CountAsync(timeout.Token)).IsEqualTo(1);
    }

    [Test]
    public async Task NoHoldUntilReadyLastSeat_TwoDifferentTicketTypesReserveAtMostOneFinalizationHold()
    {
        (Guid tenantId, Guid eventId, Guid poolId, Guid firstTicketTypeId, Guid secondTicketTypeId) = await SeedAsync(
            holdPolicy: CapacityHoldPolicyEnum.NoHoldUntilReady);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));

        Task<bool> firstReservation = ReserveNonTimedAtReadyAsync(tenantId, eventId, poolId, firstTicketTypeId, timeout.Token);
        Task<bool> secondReservation = ReserveNonTimedAtReadyAsync(tenantId, eventId, poolId, secondTicketTypeId, timeout.Token);
        bool[] reservations = await Task.WhenAll(firstReservation, secondReservation);

        await Assert.That(reservations.Count(result => result)).IsEqualTo(1);
        await using ExploreDbContext verificationContext = fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenantId));
        var verificationRepository = new RegistrationInventoryRepository(verificationContext);
        await Assert.That(await verificationRepository.GetAllocatedQuantityAsync(poolId, tenantId, timeout.Token)).IsEqualTo(1);
        await Assert.That(await verificationContext.RegistrationInventoryHolds
            .Where(hold => hold.CapacityPoolId == poolId && hold.RegistrationInventoryHoldStatusId == (int)RegistrationInventoryHoldStatusEnum.Active)
            .CountAsync(timeout.Token)).IsEqualTo(1);
    }

    [Test]
    public async Task SucceededPaidFinalizersAndDuplicateWorkersRaceLastSeatWithoutOversellOrDuplicateEffects()
    {
        (Guid tenantId, Guid eventId, Guid poolId, Guid firstTicketTypeId, Guid secondTicketTypeId) =
            await SeedAsync(holdPolicy: CapacityHoldPolicyEnum.NoHoldUntilReady, paidTickets: true);
        (Guid firstOrderId, Guid secondOrderId) = await SeedPaidOrdersAsync(
            tenantId, eventId, firstTicketTypeId, secondTicketTypeId);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(60));

        RegistrationOrderLifecycleResponseDto[] results = await Task.WhenAll(
            FinalizePaidAsync(tenantId, firstOrderId, timeout.Token),
            FinalizePaidAsync(tenantId, firstOrderId, timeout.Token),
            FinalizePaidAsync(tenantId, secondOrderId, timeout.Token),
            FinalizePaidAsync(tenantId, secondOrderId, timeout.Token));

        await using ExploreDbContext verification = fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenantId));
        RegistrationOrder[] orders = await verification.RegistrationOrders
            .Where(order => order.Id == firstOrderId || order.Id == secondOrderId)
            .OrderBy(order => order.Id)
            .ToArrayAsync(timeout.Token);
        await Assert.That(orders.Count(order => order.RegistrationOrderStatusId == (int)RegistrationOrderStatusEnum.Confirmed)).IsEqualTo(1);
        await Assert.That(orders.Count(order => order.RegistrationOrderStatusId == (int)RegistrationOrderStatusEnum.NeedsReconciliation)).IsEqualTo(1);
        await Assert.That(results.Count(result => result.Order?.StatusId == (int)RegistrationOrderStatusEnum.Confirmed)).IsGreaterThanOrEqualTo(1);
        await Assert.That(await verification.RegistrationInventoryHolds.CountAsync(
            hold => hold.CapacityPoolId == poolId &&
                    hold.RegistrationInventoryHoldStatusId == (int)RegistrationInventoryHoldStatusEnum.Consumed,
            timeout.Token)).IsEqualTo(1);
        await Assert.That(await verification.RegistrationInventoryHolds.CountAsync(
            hold => hold.CapacityPoolId == poolId &&
                    hold.RegistrationInventoryHoldStatusId == (int)RegistrationInventoryHoldStatusEnum.Active,
            timeout.Token)).IsEqualTo(0);
        await Assert.That(await verification.EventRegistrations.CountAsync(
            registration => registration.RegistrationOrderId == firstOrderId || registration.RegistrationOrderId == secondOrderId,
            timeout.Token)).IsEqualTo(1);
        await Assert.That(await verification.OutboxMessages.CountAsync(
            message => (message.AggregateId == firstOrderId || message.AggregateId == secondOrderId) &&
                       message.EventType == RegistrationOrderOutboxMessageFactory.ConfirmedEventType,
            timeout.Token)).IsEqualTo(1);
        string payload = await verification.OutboxMessages
            .Where(message => message.EventType == RegistrationOrderOutboxMessageFactory.ConfirmedEventType)
            .Select(message => message.Payload)
            .SingleAsync(timeout.Token);
        using JsonDocument payloadDocument = JsonDocument.Parse(payload);
        await Assert.That(payloadDocument.RootElement.GetProperty("AdmissionIssuanceRequested").GetBoolean()).IsTrue();
        await Assert.That(await verification.RegistrationFinalizationEffects.CountAsync(
            effect => effect.RegistrationOrderId == firstOrderId || effect.RegistrationOrderId == secondOrderId,
            timeout.Token)).IsEqualTo(2);
    }

    [Test]
    public async Task PaidFinalizationOutboxFailureRollsBackHoldAdmissionAndOrderTransitionAtomically()
    {
        (Guid tenantId, Guid eventId, _, Guid firstTicketTypeId, Guid secondTicketTypeId) =
            await SeedAsync(holdPolicy: CapacityHoldPolicyEnum.NoHoldUntilReady, paidTickets: true);
        (Guid firstOrderId, _) = await SeedPaidOrdersAsync(
            tenantId, eventId, firstTicketTypeId, secondTicketTypeId);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(60));

        await Assert.That(() => FinalizePaidWithOutboxFailureAsync(tenantId, firstOrderId, timeout.Token))
            .Throws<InvalidOperationException>();

        await using ExploreDbContext verification = fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenantId));
        RegistrationOrder order = await verification.RegistrationOrders.SingleAsync(
            value => value.Id == firstOrderId, timeout.Token);
        await Assert.That(order.RegistrationOrderStatusId).IsEqualTo((int)RegistrationOrderStatusEnum.AwaitingPayment);
        await Assert.That(await verification.RegistrationInventoryHolds.CountAsync(
            hold => hold.RegistrationOrderId == firstOrderId, timeout.Token)).IsEqualTo(0);
        await Assert.That(await verification.EventRegistrations.CountAsync(
            registration => registration.RegistrationOrderId == firstOrderId, timeout.Token)).IsEqualTo(0);
        await Assert.That(await verification.OutboxMessages.CountAsync(
            message => message.AggregateId == firstOrderId, timeout.Token)).IsEqualTo(0);
    }

    [Test]
    public async Task ExpiredHold_AtomicallyReleasesCapacityAndMovesOrderToReconciliation()
    {
        (Guid tenantId, Guid eventId, Guid poolId, Guid ticketTypeId, Guid _) = await SeedAsync();
        DateTime createdAt = UtcNow.AddMinutes(-20);
        DateTime expiresAt = UtcNow.AddMinutes(-5);
        Guid orderId = Guid.CreateVersion7();
        Guid holdId = Guid.CreateVersion7();
        await using (ExploreDbContext setupContext = fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenantId)))
        {
            var catalogs = new EventTicketCatalogRepository(setupContext);
            var inventory = new RegistrationInventoryRepository(setupContext);
            EventTicketCatalogVersion catalog = (await catalogs.GetPublishedCatalogAsync(eventId, tenantId, CancellationToken.None))!;
            EventTicketType ticketType = catalog.TicketTypes.Single(ticket => ticket.Id == ticketTypeId);
            RegistrationOrder order = RegistrationOrder.Create(
                orderId,
                tenantId,
                eventId,
                accountUserId: Guid.CreateVersion7(),
                purchaserActorId: null,
                bookingPartyType: BookingPartyTypeEnum.Individual,
                ticketCatalogVersionId: catalog.Id,
                participationSnapshot: RegistrationParticipationSnapshot.Create(Guid.CreateVersion7(), 4, 3, 2, GuestRecoveryPolicyEnum.VerifiedEmailRequired),
                registrationWorkflowVersionId: null,
                guestAccessTokenHash: null,
                currencyCode: catalog.CurrencyCode,
                createdAt,
                expiresAt);
            order.AddLine(RegistrationOrderLine.Create(
                Guid.CreateVersion7(),
                catalog,
                ticketType,
                order.Id,
                quantity: 1,
                chosenUnitPriceAmount: null,
                platformFeePolicy: null));
            order.TransitionTo(RegistrationOrderStatusEnum.AwaitingParticipantDetails, createdAt);
            RegistrationInventoryHold hold = RegistrationInventoryHold.Create(
                holdId,
                order.Id,
                poolId,
                ticketType.Id,
                tenantId,
                quantity: 1,
                createdAt,
                expiresAt);
            await inventory.AddOrderWithHoldsAsync(order, [hold], CancellationToken.None);
            await inventory.SaveChangesAsync(CancellationToken.None);
        }

        var baseline = new PersistenceQueryBaselineInterceptor();
        await using (ExploreDbContext expiryContext = fixture.CreateTenantFilteredDbContext(
                         new TestTenantContext(tenantId),
                         baseline))
        {
            var inventory = new RegistrationInventoryRepository(expiryContext);
            var elapsed = Stopwatch.StartNew();
            bool expired = await inventory.TryExpireDueHoldAsync(holdId, UtcNow, CancellationToken.None);
            elapsed.Stop();
            PersistenceQueryBaselineEvidence.Record(baseline
                .Snapshot("inventory_expire_due_hold", expired ? 1 : 0, elapsed.Elapsed));
            await Assert.That(expired).IsTrue();
        }

        await using ExploreDbContext verificationContext = fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenantId));
        var verificationRepository = new RegistrationInventoryRepository(verificationContext);
        RegistrationOrder persistedOrder = await verificationContext.RegistrationOrders.SingleAsync(order => order.Id == orderId);
        await Assert.That(persistedOrder.RegistrationOrderStatusId).IsEqualTo((int)RegistrationOrderStatusEnum.NeedsReconciliation);
        await Assert.That(await verificationRepository.GetAllocatedQuantityAsync(poolId, tenantId, CancellationToken.None)).IsEqualTo(0);
    }

    [Test]
    public async Task RecoveredHoldConsumption_RetainsExpiredAuditRowAndConsumesOnlyTheReplacementHold()
    {
        (Guid tenantId, Guid eventId, Guid poolId, Guid ticketTypeId, Guid _) = await SeedAsync();
        Guid orderId = Guid.CreateVersion7();
        Guid expiredHoldId = Guid.CreateVersion7();
        Guid replacementHoldId = Guid.CreateVersion7();
        await using (ExploreDbContext setupContext = fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenantId)))
        {
            var catalogs = new EventTicketCatalogRepository(setupContext);
            var inventory = new RegistrationInventoryRepository(setupContext);
            EventTicketCatalogVersion catalog = (await catalogs.GetPublishedCatalogAsync(eventId, tenantId, CancellationToken.None))!;
            EventTicketType ticketType = catalog.TicketTypes.Single(ticket => ticket.Id == ticketTypeId);
            RegistrationOrder order = RegistrationOrder.Create(
                orderId,
                tenantId,
                eventId,
                accountUserId: null,
                purchaserActorId: null,
                bookingPartyType: BookingPartyTypeEnum.Individual,
                ticketCatalogVersionId: catalog.Id,
                participationSnapshot: RegistrationParticipationSnapshot.Create(Guid.CreateVersion7(), 4, 3, 2, GuestRecoveryPolicyEnum.VerifiedEmailRequired),
                registrationWorkflowVersionId: null,
                guestAccessTokenHash: CreateGuestCapabilityHash(orderId),
                currencyCode: catalog.CurrencyCode,
                createdAt: UtcNow.AddMinutes(-15),
                expiresAt: UtcNow.AddMinutes(15));
            order.AddLine(RegistrationOrderLine.Create(Guid.CreateVersion7(), catalog, ticketType, order.Id, 1, null, null));
            RegistrationInventoryHold expiredHold = RegistrationInventoryHold.Create(
                expiredHoldId,
                order.Id,
                poolId,
                ticketType.Id,
                tenantId,
                1,
                UtcNow.AddMinutes(-15),
                UtcNow.AddMinutes(-1));
            expiredHold.TryExpire(UtcNow);
            RegistrationInventoryHold replacementHold = RegistrationInventoryHold.Create(
                replacementHoldId,
                order.Id,
                poolId,
                ticketType.Id,
                tenantId,
                1,
                UtcNow,
                UtcNow.AddMinutes(15));
            await inventory.AddOrderWithHoldsAsync(order, [expiredHold, replacementHold], CancellationToken.None);
            await inventory.SaveChangesAsync(CancellationToken.None);

            await Assert.That(await inventory.TryConsumeActiveHoldsForOrderAsync(order.Id, tenantId, UtcNow, CancellationToken.None)).IsEqualTo(1);
        }

        await using ExploreDbContext verificationContext = fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenantId));
        RegistrationInventoryHold[] holds = await verificationContext.RegistrationInventoryHolds
            .Where(hold => hold.RegistrationOrderId == orderId)
            .ToArrayAsync();
        RegistrationInventoryHold expiredHoldAudit = holds.Single(hold => hold.Id == expiredHoldId);
        RegistrationInventoryHold consumedReplacementHold = holds.Single(hold => hold.Id == replacementHoldId);
        var verificationRepository = new RegistrationInventoryRepository(verificationContext);

        await Assert.That(expiredHoldAudit.RegistrationInventoryHoldStatusId).IsEqualTo((int)RegistrationInventoryHoldStatusEnum.Expired);
        await Assert.That(expiredHoldAudit.ConsumedAt).IsNull();
        await Assert.That(consumedReplacementHold.RegistrationInventoryHoldStatusId).IsEqualTo((int)RegistrationInventoryHoldStatusEnum.Consumed);
        await Assert.That(consumedReplacementHold.ConsumedAt).IsEqualTo(UtcNow);
        await Assert.That(await verificationRepository.GetAllocatedQuantityAsync(poolId, tenantId, CancellationToken.None)).IsEqualTo(1);
    }

    [Test]
    public async Task CancelledOrderWithoutParticipants_ReleasesEveryActiveLineHold()
    {
        (Guid tenantId, Guid eventId, Guid poolId, Guid firstTicketTypeId, Guid secondTicketTypeId) =
            await SeedAsync(maximumQuantity: 2);
        Guid orderId = Guid.CreateVersion7();

        await using (ExploreDbContext setupContext = fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenantId)))
        {
            var catalogs = new EventTicketCatalogRepository(setupContext);
            var inventory = new RegistrationInventoryRepository(setupContext);
            EventTicketCatalogVersion catalog = (await catalogs.GetPublishedCatalogAsync(eventId, tenantId, CancellationToken.None))!;
            EventTicketType firstTicket = catalog.TicketTypes.Single(ticket => ticket.Id == firstTicketTypeId);
            EventTicketType secondTicket = catalog.TicketTypes.Single(ticket => ticket.Id == secondTicketTypeId);
            RegistrationOrder order = RegistrationOrder.Create(
                orderId,
                tenantId,
                eventId,
                accountUserId: null,
                purchaserActorId: null,
                bookingPartyType: BookingPartyTypeEnum.Individual,
                ticketCatalogVersionId: catalog.Id,
                participationSnapshot: RegistrationParticipationSnapshot.Create(Guid.CreateVersion7(), 4, 3, 2, GuestRecoveryPolicyEnum.VerifiedEmailRequired),
                registrationWorkflowVersionId: null,
                guestAccessTokenHash: CreateGuestCapabilityHash(orderId),
                currencyCode: catalog.CurrencyCode,
                createdAt: UtcNow,
                expiresAt: UtcNow.AddMinutes(15));
            order.AddLine(RegistrationOrderLine.Create(Guid.CreateVersion7(), catalog, firstTicket, order.Id, 1, null, null));
            order.AddLine(RegistrationOrderLine.Create(Guid.CreateVersion7(), catalog, secondTicket, order.Id, 1, null, null));
            order.TransitionTo(RegistrationOrderStatusEnum.AwaitingParticipantDetails, UtcNow);

            RegistrationInventoryHold[] holds =
            [
                RegistrationInventoryHold.Create(Guid.CreateVersion7(), order.Id, poolId, firstTicket.Id, tenantId, 1, UtcNow, UtcNow.AddMinutes(15)),
                RegistrationInventoryHold.Create(Guid.CreateVersion7(), order.Id, poolId, secondTicket.Id, tenantId, 1, UtcNow, UtcNow.AddMinutes(15))
            ];
            await inventory.AddOrderWithHoldsAsync(order, holds, CancellationToken.None);
            await inventory.SaveChangesAsync(CancellationToken.None);

            int released = await inventory.TryReleaseActiveHoldsForOrderAsync(
                order.Id,
                tenantId,
                RegistrationInventoryHoldStatusEnum.Cancelled,
                UtcNow,
                CancellationToken.None);

            await Assert.That(released).IsEqualTo(2);
        }

        await using ExploreDbContext verificationContext = fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenantId));
        var verificationRepository = new RegistrationInventoryRepository(verificationContext);
        int[] holdStatusIds = await verificationContext.RegistrationInventoryHolds
            .Where(hold => hold.RegistrationOrderId == orderId)
            .OrderBy(hold => hold.Id)
            .Select(hold => hold.RegistrationInventoryHoldStatusId)
            .ToArrayAsync();
        await Assert.That(holdStatusIds).IsEquivalentTo(
            [(int)RegistrationInventoryHoldStatusEnum.Cancelled, (int)RegistrationInventoryHoldStatusEnum.Cancelled]);
        await Assert.That(await verificationContext.EventRegistrations.CountAsync(registration => registration.RegistrationOrderId == orderId)).IsEqualTo(0);
        await Assert.That(await verificationRepository.GetAllocatedQuantityAsync(poolId, tenantId, CancellationToken.None)).IsEqualTo(0);
    }

    [Test]
    public async Task PaymentCancelAndDispatchClaimsRaceWithOneAuthoritativeWinner()
    {
        PaymentRaceSeed seed = await SeedPaymentRaceAsync(PaymentAttemptStatusEnum.Created);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        await using ExploreDbContext cancelContext = CreateRetryingTenantContext(seed.TenantId);
        await using ExploreDbContext dispatchContext = CreateRetryingTenantContext(seed.TenantId);
        var cancelRepository = new RegistrationPaymentAttemptRepository(cancelContext);
        var dispatchRepository = new RegistrationPaymentAttemptRepository(dispatchContext);

        Task<PaymentCancellationDisposition> cancellation = cancelRepository.TryCancelBeforeProviderHandoffAsync(
            seed.TenantId, seed.OrderId, UtcNow, timeout.Token);
        Task<IReadOnlyList<CheckoutDispatchClaim>> dispatch = dispatchRepository.ClaimDueDispatchEffectsAsync(
            "dispatch-racer", 1, UtcNow, TimeSpan.FromMinutes(2), timeout.Token);
        await Task.WhenAll(cancellation, dispatch);

        PaymentCancellationDisposition cancellationResult = await cancellation;
        IReadOnlyList<CheckoutDispatchClaim> claims = await dispatch;
        bool handoffEligible = claims.Count == 1 && await dispatchRepository.MarkCheckoutDispatchPendingAsync(
            claims[0], UtcNow.AddTicks(1), timeout.Token);
        await Assert.That(cancellationResult == PaymentCancellationDisposition.CancelledBeforeHandoff && handoffEligible).IsFalse();

        await using ExploreDbContext verification = CreateRetryingTenantContext(seed.TenantId);
        PaymentAttempt attempt = await verification.PaymentAttempts.SingleAsync(value => value.Id == seed.AttemptId, timeout.Token);
        if (cancellationResult == PaymentCancellationDisposition.CancelledBeforeHandoff)
        {
            await Assert.That(attempt.PaymentAttemptStatusId).IsEqualTo((int)PaymentAttemptStatusEnum.Cancelled);
        }
        else
        {
            await Assert.That(await verification.PaymentReconciliationEffects.AnyAsync(
                value => value.PaymentAttemptId == seed.AttemptId, timeout.Token)).IsTrue();
            await Assert.That(await verification.RegistrationInventoryHolds.AnyAsync(
                value => value.RegistrationOrderId == seed.OrderId &&
                         value.RegistrationInventoryHoldStatusId == (int)RegistrationInventoryHoldStatusEnum.Active,
                timeout.Token)).IsTrue();
        }
    }

    [Test]
    public async Task PaymentSuccessAtCutoffRacingHoldExpiryNeverReleasesCapacity()
    {
        PaymentRaceSeed seed = await SeedPaymentRaceAsync(PaymentAttemptStatusEnum.RequiresAction, expiredCutoff: true);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        PaymentReconciliationClaim claim;
        await using (ExploreDbContext claimContext = CreateRetryingTenantContext(seed.TenantId))
        {
            var repository = new RegistrationPaymentAttemptRepository(claimContext);
            PaymentAttempt attempt = await claimContext.PaymentAttempts.SingleAsync(value => value.Id == seed.AttemptId, timeout.Token);
            await repository.EnsureReconciliationDueAsync(attempt, null, UtcNow, timeout.Token);
            claim = (await repository.ClaimDueReconciliationsAsync(
                "payment-racer", 1, UtcNow, TimeSpan.FromMinutes(2), timeout.Token)).Single();
        }

        await using ExploreDbContext paymentContext = CreateRetryingTenantContext(seed.TenantId);
        await using ExploreDbContext expiryContext = CreateRetryingTenantContext(seed.TenantId);
        var paymentRepository = new RegistrationPaymentAttemptRepository(paymentContext);
        var inventoryRepository = new RegistrationInventoryRepository(expiryContext);
        var success = new PaymentReconciliationDecision(
            PaymentReconciliationDisposition.Complete,
            PaymentAttemptStatusEnum.Succeeded,
            "cs_race",
            "pi_race",
            "req_race",
            string.Empty,
            UtcNow.AddTicks(1));

        await Task.WhenAll(
            paymentRepository.SettleReconciliationAsync(claim, success, timeout.Token),
            inventoryRepository.TryExpireDueHoldAsync(seed.HoldId, UtcNow, timeout.Token));

        await using ExploreDbContext verification = CreateRetryingTenantContext(seed.TenantId);
        await Assert.That((await verification.PaymentAttempts.SingleAsync(value => value.Id == seed.AttemptId, timeout.Token)).PaymentAttemptStatusId)
            .IsEqualTo((int)PaymentAttemptStatusEnum.Succeeded);
        await Assert.That((await verification.RegistrationInventoryHolds.SingleAsync(value => value.Id == seed.HoldId, timeout.Token)).RegistrationInventoryHoldStatusId)
            .IsEqualTo((int)RegistrationInventoryHoldStatusEnum.Active);
    }

    [Test]
    public async Task ConcurrentTerminalRetriesPersistOneActiveReplacementWinner()
    {
        PaymentRaceSeed seed = await SeedPaymentRaceAsync(PaymentAttemptStatusEnum.Failed);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));

        RegistrationPaymentAttemptClaimOutcome?[] outcomes = await Task.WhenAll(
            CreateTerminalRetryAsync(seed, "retry-a", timeout.Token),
            CreateTerminalRetryAsync(seed, "retry-b", timeout.Token));

        await using ExploreDbContext verification = CreateRetryingTenantContext(seed.TenantId);
        PaymentAttempt[] attempts = await verification.PaymentAttempts
            .Where(value => value.RegistrationOrderId == seed.OrderId)
            .ToArrayAsync(timeout.Token);
        await Assert.That(attempts.Count(value => value.ActiveUniquenessSlot == PaymentAttempt.ActiveUniquenessSlotValue)).IsEqualTo(1);
        await Assert.That(attempts.Select(value => value.ProviderIdempotencyKey).Distinct().Count()).IsEqualTo(attempts.Length);
        await Assert.That(outcomes.Count(value => value is not null)).IsGreaterThanOrEqualTo(1);
    }

    [Test]
    public async Task ConcurrentFreshPaymentClaimsWithDifferentCompositionRevisionsConvergeOnOneActiveAttempt()
    {
        PaymentRaceSeed seed = await SeedPaymentRaceAsync(PaymentAttemptStatusEnum.Created);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        await using (ExploreDbContext cleanup = CreateRetryingTenantContext(seed.TenantId))
        {
            await cleanup.CheckoutDispatchEffects
                .Where(value => value.PaymentAttemptId == seed.AttemptId)
                .ExecuteDeleteAsync(timeout.Token);
            await cleanup.PaymentAttempts
                .Where(value => value.Id == seed.AttemptId)
                .ExecuteDeleteAsync(timeout.Token);
        }

        RegistrationPaymentAttemptClaimOutcome[] outcomes = await Task.WhenAll(
            CreateFreshPaymentClaimAsync(seed, "composition-a", timeout.Token),
            CreateFreshPaymentClaimAsync(seed, "composition-b", timeout.Token));

        await Assert.That(outcomes.Select(value => value.Attempt.Id).Distinct().Count()).IsEqualTo(1);
        await using ExploreDbContext verification = CreateRetryingTenantContext(seed.TenantId);
        PaymentAttempt[] attempts = await verification.PaymentAttempts
            .Where(value => value.RegistrationOrderId == seed.OrderId)
            .ToArrayAsync(timeout.Token);
        await Assert.That(attempts.Length).IsEqualTo(1);
        await Assert.That(attempts.Single().ActiveUniquenessSlot).IsEqualTo(PaymentAttempt.ActiveUniquenessSlotValue);
        await Assert.That(attempts.Select(value => value.ProviderIdempotencyKey).Distinct().Count()).IsEqualTo(1);
        await Assert.That(await verification.CheckoutDispatchEffects.CountAsync(
            value => value.RegistrationOrderId == seed.OrderId, timeout.Token)).IsEqualTo(1);
    }

    [Test]
    public async Task ExpiredDispatchLeaseCannotAuthorizeProviderHandoffAfterSecondWorkerClaims()
    {
        PaymentRaceSeed seed = await SeedPaymentRaceAsync(PaymentAttemptStatusEnum.Created);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        await using ExploreDbContext firstContext = CreateRetryingTenantContext(seed.TenantId);
        await using ExploreDbContext secondContext = CreateRetryingTenantContext(seed.TenantId);
        var firstRepository = new RegistrationPaymentAttemptRepository(firstContext);
        var secondRepository = new RegistrationPaymentAttemptRepository(secondContext);
        CheckoutDispatchClaim first = (await firstRepository.ClaimDueDispatchEffectsAsync(
            "worker-a", 1, UtcNow, TimeSpan.FromSeconds(1), timeout.Token)).Single();
        DateTime afterLease = UtcNow.AddSeconds(2);
        CheckoutDispatchClaim second = (await secondRepository.ClaimDueDispatchEffectsAsync(
            "worker-b", 1, afterLease, TimeSpan.FromMinutes(2), timeout.Token)).Single();

        await Assert.That(await firstRepository.GetClaimedAttemptAsync(first, afterLease, timeout.Token)).IsNull();
        await Assert.That(await firstRepository.MarkCheckoutDispatchPendingAsync(first, afterLease, timeout.Token)).IsFalse();
        await Assert.That(await secondRepository.GetClaimedAttemptAsync(second, afterLease, timeout.Token)).IsNotNull();
        await Assert.That(await secondRepository.MarkCheckoutDispatchPendingAsync(second, afterLease.AddTicks(1), timeout.Token)).IsTrue();
    }

    [Test]
    public async Task DelayedPreHandoffDispatchAtomicallyRenewsAttemptOrderAndActiveHoldCutoff()
    {
        PaymentRaceSeed seed = await SeedPaymentRaceAsync(PaymentAttemptStatusEnum.Created, expiredCutoff: true);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        await using ExploreDbContext context = CreateRetryingTenantContext(seed.TenantId);
        var repository = new RegistrationPaymentAttemptRepository(context);
        CheckoutDispatchClaim claim = (await repository.ClaimDueDispatchEffectsAsync(
            "delayed-dispatch", 1, UtcNow, TimeSpan.FromMinutes(2), timeout.Token)).Single();
        DateTime renewedCutoff = UtcNow.AddMinutes(31);

        PaymentAttempt? prepared = await repository.PrepareCheckoutDispatchAsync(
            claim, UtcNow, renewedCutoff, timeout.Token);

        await Assert.That(prepared).IsNotNull();
        await Assert.That(prepared!.ExpiresAt).IsEqualTo(renewedCutoff);
        await using ExploreDbContext verification = CreateRetryingTenantContext(seed.TenantId);
        await Assert.That((await verification.RegistrationOrders.SingleAsync(value => value.Id == seed.OrderId, timeout.Token)).ExpiresAt)
            .IsEqualTo(renewedCutoff);
        await Assert.That((await verification.RegistrationInventoryHolds.SingleAsync(value => value.Id == seed.HoldId, timeout.Token)).ExpiresAt)
            .IsEqualTo(renewedCutoff);
    }

    [Test]
    public async Task CommittedStopSalePreventsProviderHandoffAuthorization()
    {
        PaymentRaceSeed seed = await SeedPaymentRaceAsync(PaymentAttemptStatusEnum.Created);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        await using ExploreDbContext context = CreateRetryingTenantContext(seed.TenantId);
        var repository = new RegistrationPaymentAttemptRepository(context);
        CheckoutDispatchClaim claim = (await repository.ClaimDueDispatchEffectsAsync(
            "worker-stop-fence",
            1,
            UtcNow,
            TimeSpan.FromMinutes(2),
            timeout.Token)).Single();
        context.PaidCheckoutSaleControls.Add(PaidCheckoutSaleControl.CreateStopped(
            seed.TenantId,
            seed.Order.EventId,
            Guid.CreateVersion7(),
            "incident",
            UtcNow.AddTicks(1)));
        await context.SaveChangesAsync(timeout.Token);

        PaymentAttempt? prepared = await repository.PrepareCheckoutDispatchAsync(
            claim,
            UtcNow.AddTicks(2),
            UtcNow.AddMinutes(31),
            timeout.Token);

        await Assert.That(prepared).IsNull();
    }

    [Test]
    public async Task ConfigurationBlockedPastCutoffRequiresApplicationLifecycleWithoutRepositoryCancellation()
    {
        PaymentRaceSeed seed = await SeedPaymentRaceAsync(PaymentAttemptStatusEnum.Created, expiredCutoff: true);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        await using ExploreDbContext context = CreateRetryingTenantContext(seed.TenantId);
        var repository = new RegistrationPaymentAttemptRepository(context);
        CheckoutDispatchClaim claim = (await repository.ClaimDueDispatchEffectsAsync(
            "configuration-expiry", 1, UtcNow, TimeSpan.FromMinutes(2), timeout.Token)).Single();

        CheckoutDispatchConfigurationDisposition disposition = await repository.DeferCheckoutDispatchForConfigurationAsync(
            claim,
            "checkout_provider_secret_unavailable",
            UtcNow.AddMinutes(15),
            UtcNow,
            timeout.Token);

        await Assert.That(disposition).IsEqualTo(CheckoutDispatchConfigurationDisposition.RequiresLifecycleCancellation);
        await using ExploreDbContext verification = CreateRetryingTenantContext(seed.TenantId);
        PaymentAttempt attempt = await verification.PaymentAttempts.SingleAsync(value => value.Id == seed.AttemptId, timeout.Token);
        await Assert.That(attempt.PaymentAttemptStatusId).IsEqualTo((int)PaymentAttemptStatusEnum.Created);
        await Assert.That(attempt.ActiveUniquenessSlot).IsEqualTo(PaymentAttempt.ActiveUniquenessSlotValue);
        await Assert.That((await verification.RegistrationOrders.SingleAsync(value => value.Id == seed.OrderId, timeout.Token)).RegistrationOrderStatusId)
            .IsEqualTo((int)RegistrationOrderStatusEnum.AwaitingPayment);
        await Assert.That((await verification.RegistrationInventoryHolds.SingleAsync(value => value.Id == seed.HoldId, timeout.Token)).RegistrationInventoryHoldStatusId)
            .IsEqualTo((int)RegistrationInventoryHoldStatusEnum.Active);
    }

    [Test]
    public async Task ConfigurationExpiryLifecycleReleasesPromotionAndHoldsWritesOneOutboxAndWithdrawsDeadlineIdempotently()
    {
        PaymentRaceSeed seed = await SeedPaymentRaceAsync(
            PaymentAttemptStatusEnum.Created,
            expiredCutoff: true,
            activePromotion: true);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        await using ExploreDbContext context = CreateRetryingTenantContext(seed.TenantId);
        var paymentRepository = new RegistrationPaymentAttemptRepository(context);
        CheckoutDispatchClaim claim = (await paymentRepository.ClaimDueDispatchEffectsAsync(
            "configuration-lifecycle", 1, UtcNow, TimeSpan.FromMinutes(2), timeout.Token)).Single();
        IScheduledDeadlineDispatcher deadlines = Substitute.For<IScheduledDeadlineDispatcher>();
        var inventory = new RegistrationInventoryRepository(context);
        var lifecycle = new RegistrationOrderLifecycleService(
            inventory,
            new PromotionRedemptionRepository(context),
            new RegistrationParticipantRepository(context),
            new EventTicketCatalogRepository(context),
            new PlatformContributionSettingRepository(context),
            new EventSessionRepository(context),
            new OutboxRepository(context),
            new EfCoreUnitOfWork(context),
            new RegistrationFinalizationRepository(context),
            paymentRepository,
            deadlines,
            new FixedTimeProvider(UtcNow),
            Substitute.For<IPaidOrderAcceptanceService>(),
            new RegistrationOrderTransitionCoordinator(inventory));

        CheckoutDispatchConfigurationDisposition first = await lifecycle.CancelExpiredConfigurationBlockedPaymentAsync(
            claim, UtcNow, timeout.Token);
        CheckoutDispatchConfigurationDisposition duplicate = await lifecycle.CancelExpiredConfigurationBlockedPaymentAsync(
            claim, UtcNow.AddSeconds(1), timeout.Token);

        await Assert.That(first).IsEqualTo(CheckoutDispatchConfigurationDisposition.CancelledExpired);
        await Assert.That(duplicate).IsEqualTo(CheckoutDispatchConfigurationDisposition.CancelledExpired);
        await using ExploreDbContext verification = CreateRetryingTenantContext(seed.TenantId);
        await Assert.That((await verification.PromotionReservations.SingleAsync(value => value.Id == seed.PromotionReservationId, timeout.Token)).PromotionReservationStatusId)
            .IsEqualTo((int)PromotionReservationStatusEnum.Released);
        await Assert.That((await verification.RegistrationInventoryHolds.SingleAsync(value => value.Id == seed.HoldId, timeout.Token)).RegistrationInventoryHoldStatusId)
            .IsEqualTo((int)RegistrationInventoryHoldStatusEnum.Cancelled);
        await Assert.That(await verification.OutboxMessages.CountAsync(value =>
            value.AggregateId == seed.OrderId && value.EventType == RegistrationOrderOutboxMessageFactory.CancelledEventType,
            timeout.Token)).IsEqualTo(1);
        await deadlines.Received(1).CancelAsync(
            ScheduledJobNames.InventoryHoldExpiry,
            InventoryHoldDeadline.KeyFor(seed.OrderId),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ConcurrentExplicitTerminalRetryServicesCreateOrReuseOneActiveReplacement()
    {
        PaymentRaceSeed seed = await SeedPaymentRaceAsync(PaymentAttemptStatusEnum.RequiresAction);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(60));
        await using (ExploreDbContext reconciliationContext = CreateRetryingTenantContext(seed.TenantId))
        {
            var repository = new RegistrationPaymentAttemptRepository(reconciliationContext);
            RegistrationOrder order = await reconciliationContext.RegistrationOrders
                .SingleAsync(value => value.Id == seed.OrderId, timeout.Token);
            _ = order.ExtendPaymentCutoff(UtcNow.AddHours(1), UtcNow);
            foreach (RegistrationInventoryHold hold in await reconciliationContext.RegistrationInventoryHolds
                         .Where(value => value.RegistrationOrderId == seed.OrderId &&
                                         value.RegistrationInventoryHoldStatusId == (int)RegistrationInventoryHoldStatusEnum.Active)
                         .ToArrayAsync(timeout.Token))
            {
                _ = hold.ExtendPaymentCutoff(UtcNow.AddHours(1), UtcNow);
            }

            PaymentAttempt attempt = await reconciliationContext.PaymentAttempts.SingleAsync(value => value.Id == seed.AttemptId, timeout.Token);
            await repository.EnsureReconciliationDueAsync(attempt, null, UtcNow, timeout.Token);
            PaymentReconciliationClaim claim = (await repository.ClaimDueReconciliationsAsync(
                "terminal-retry-release", 1, UtcNow, TimeSpan.FromMinutes(2), timeout.Token)).Single();
            await Assert.That(await repository.SettleReconciliationAsync(
                claim,
                new PaymentReconciliationDecision(
                    PaymentReconciliationDisposition.Complete,
                    PaymentAttemptStatusEnum.Failed,
                    "cs_race",
                    "pi_race",
                    "req_failed",
                    string.Empty,
                    UtcNow.AddTicks(1)),
                timeout.Token)).IsTrue();
        }

        await using ExploreDbContext eventContext = CreateRetryingTenantContext(seed.TenantId);
        DomainEvent eventTarget = await eventContext.Events.AsNoTracking().SingleAsync(value => value.Id == seed.Order.EventId, timeout.Token);
        OrganizerPaymentProviderConnection connection = ReadyConnection(seed.TenantId, eventTarget.OrganizerActorId!.Value);
        PaidEventPolicyVersion defaultPolicy = PaidEventPolicyVersion.CreateDefaultInstance();
        PaidEventPolicyVersion instancePolicy = defaultPolicy.CreateRevision(
            isPaymentsEnabled: true,
            defaultPolicy.AllowedOrganizerKinds,
            defaultPolicy.RequiresLocalVerification,
            defaultPolicy.AllowedCurrencyCodes,
            defaultPolicy.DefaultCurrencyCode,
            defaultPolicy.RefundProtections,
            defaultPolicy.CurrencyRiskLimits,
            defaultPolicy.RequiresFirstPaidEventReview,
            defaultPolicy.FarFutureReviewThresholdDays);
        RegistrationPaymentAttemptClaimResult[] results = await Task.WhenAll(
            RetryThroughServiceAsync(seed, eventTarget, connection, instancePolicy, timeout.Token),
            RetryThroughServiceAsync(seed, eventTarget, connection, instancePolicy, timeout.Token));

        await Assert.That(results.All(value => value.Success && value.Attempt is not null))
            .IsTrue()
            .Because(string.Join(
                " | ",
                results.Select(value =>
                    $"{value.FailureCode ?? "success"}:{value.Message}")));
        await Assert.That(results.Select(value => value.Attempt!.Id).Distinct().Count()).IsEqualTo(1);
        Guid secondAttemptId = results[0].Attempt!.Id;
        await using (ExploreDbContext terminalContext = CreateRetryingTenantContext(seed.TenantId))
        {
            var repository = new RegistrationPaymentAttemptRepository(terminalContext);
            PaymentAttempt second = await terminalContext.PaymentAttempts
                .SingleAsync(value => value.Id == secondAttemptId, timeout.Token);
            second.MarkDispatchFailed(UtcNow.AddSeconds(2), "req_second_failed");
            await repository.ReleaseActiveSlotAsync(second, UtcNow.AddSeconds(2), timeout.Token);
        }

        RegistrationPaymentAttemptClaimResult[] nextResults = await Task.WhenAll(
            RetryThroughServiceAsync(seed, eventTarget, connection, instancePolicy, timeout.Token, secondAttemptId, UtcNow.AddSeconds(3)),
            RetryThroughServiceAsync(seed, eventTarget, connection, instancePolicy, timeout.Token, secondAttemptId, UtcNow.AddSeconds(3)));

        await Assert.That(nextResults.All(value => value.Success && value.Attempt is not null))
            .IsTrue()
            .Because(string.Join(
                " | ",
                nextResults.Select(value =>
                    $"{value.FailureCode ?? "success"}:{value.Message}")));
        await Assert.That(nextResults.Select(value => value.Attempt!.Id).Distinct().Count()).IsEqualTo(1);
        await Assert.That(nextResults[0].Attempt!.Id).IsNotEqualTo(secondAttemptId);
        await using ExploreDbContext verification = CreateRetryingTenantContext(seed.TenantId);
        PaymentAttempt[] attempts = await verification.PaymentAttempts
            .Where(value => value.RegistrationOrderId == seed.OrderId)
            .OrderBy(value => value.CreatedAt)
            .ThenBy(value => value.Id)
            .ToArrayAsync(timeout.Token);
        await Assert.That(attempts.Length).IsEqualTo(3);
        await Assert.That(attempts.Count(value => value.ActiveUniquenessSlot == PaymentAttempt.ActiveUniquenessSlotValue)).IsEqualTo(1);
        await Assert.That(attempts.Select(value => value.ProviderIdempotencyKey).Distinct().Count()).IsEqualTo(3);
        await Assert.That(await verification.CheckoutDispatchEffects.CountAsync(
            value => value.RegistrationOrderId == seed.OrderId, timeout.Token)).IsEqualTo(3);
    }

    private async Task<RegistrationPaymentAttemptClaimResult> RetryThroughServiceAsync(
        PaymentRaceSeed seed,
        DomainEvent eventTarget,
        OrganizerPaymentProviderConnection connection,
        PaidEventPolicyVersion instancePolicy,
        CancellationToken cancellationToken,
        Guid? terminalAttemptId = null,
        DateTime? requestedAt = null)
    {
        await using ExploreDbContext context = CreateRetryingTenantContext(seed.TenantId);
        var eventRepository = Substitute.For<IEventRepository>();
        eventRepository.GetEventWithDetailsAsync(
                eventTarget.Id,
                seed.TenantId,
                Arg.Any<CancellationToken>())
            .Returns(eventTarget);
        var connections = Substitute.For<IOrganizerPaymentProviderConnectionRepository>();
        connections.GetActiveByScopeAsync(
                seed.TenantId,
                eventTarget.OrganizerActorId!.Value,
                "stripe",
                "platform-eu",
                Arg.Any<CancellationToken>())
            .Returns(connection);
        var policies = Substitute.For<IPaidEventPolicyRepository>();
        policies.GetActiveInstanceAsync(Arg.Any<CancellationToken>()).Returns(instancePolicy);
        var commerce = Substitute.For<IOrganizerPaymentCommerceConfiguration>();
        commerce.ProviderCode.Returns("stripe");
        commerce.ConnectPlatformId.Returns("platform-eu");
        var descriptor = Substitute.For<IPaymentProviderDescriptor>();
        descriptor.Describe().Returns(new PaymentProviderDescriptor(
            "stripe", "OrganizerDirect", "2026-07-29.dahlia", "test", "instance-operator"));
        var service = new RegistrationPaymentAttemptClaimService(
            new RegistrationPaymentAttemptRepository(context),
            new RegistrationInventoryRepository(context),
            eventRepository,
            connections,
            policies,
            commerce,
            descriptor,
            ReadyActivation(),
            CurrentAcceptance(),
            new EfCoreUnitOfWork(context));
        RegistrationOrder currentOrder = await context.RegistrationOrders
            .AsNoTracking()
            .SingleAsync(value => value.Id == seed.OrderId, cancellationToken);
        PaidOrderAcceptanceSnapshot acceptance = PaidAcceptanceTestFacts.Create(
            currentOrder.TenantId,
            currentOrder.Id,
            currentOrder.EventId,
            currentOrder.ConcurrencyStamp.ToString("N"),
            instancePolicy.Id,
            currentOrder.OrganizerDirectedTotalMinorSnapshot,
            currentOrder.PlatformFeeTotalMinorSnapshot,
            currentOrder.PlatformContributionTotalMinorSnapshot,
            requestedAt ?? UtcNow.AddSeconds(1),
            currentOrder.CurrencyCode,
            OrganizerPaymentRecipientSnapshot.Create(
                seed.TenantId,
                eventTarget.OrganizerActorId.Value,
                connection.Id,
                "stripe",
                "platform-eu",
                connection.ExternalAccountId,
                connection.MerchantCountryCode!,
                currentOrder.CurrencyCode,
                instancePolicy.Id,
                null,
                requestedAt ?? UtcNow.AddSeconds(1)));
        return await service.ClaimAsync(
            new(
                seed.TenantId,
                seed.OrderId,
                requestedAt ?? UtcNow.AddSeconds(1),
                terminalAttemptId ?? seed.AttemptId,
                acceptance),
            cancellationToken);
    }

    private static IPaidCheckoutActivationService ReadyActivation()
    {
        var activation = Substitute.For<IPaidCheckoutActivationService>();
        activation.EvaluateAsync(Arg.Any<PaidCheckoutActivationRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PaidCheckoutActivationResult(true, null, "active"));
        return activation;
    }

    private static IPaidOrderAcceptanceFreshnessService CurrentAcceptance()
    {
        var freshness = Substitute.For<IPaidOrderAcceptanceFreshnessService>();
        freshness.IsCurrentAsync(Arg.Any<PaymentAttempt>(), Arg.Any<CancellationToken>()).Returns(true);
        return freshness;
    }

    private static OrganizerPaymentProviderConnection ReadyConnection(Guid tenantId, Guid organizerActorId)
    {
        OrganizerPaymentProviderConnection connection = OrganizerPaymentProviderConnection.Create(
            Guid.CreateVersion7(), tenantId, organizerActorId, "stripe", "platform-eu", "acct_retry", UtcNow.AddMinutes(-5));
        connection.ApplyReadiness(OrganizerPaymentProviderReadinessObservation.Create(
            "BE", ChargeCapabilityState.Active, ProviderRequirementsState.Satisfied, ["USD"], UtcNow, "retry-ready"));
        return connection;
    }

    private async Task<RegistrationPaymentAttemptClaimOutcome?> CreateTerminalRetryAsync(
        PaymentRaceSeed seed,
        string suffix,
        CancellationToken cancellationToken)
    {
        try
        {
            await using ExploreDbContext context = CreateRetryingTenantContext(seed.TenantId);
            var repository = new RegistrationPaymentAttemptRepository(context);
            var unitOfWork = new EfCoreUnitOfWork(context);
            return await unitOfWork.ExecuteSerializableAsync(async token =>
            {
                (PaymentAttempt Attempt, CheckoutDispatchEffect DispatchEffect) current =
                    (await repository.GetLatestByOrderAsync(seed.TenantId, seed.OrderId, token))!.Value;
                _ = current.Attempt.TryReleaseActiveSlot(UtcNow);
                PaymentAttempt replacement = CreatePaymentAttempt(
                    seed.Order,
                    PaymentAttemptStatusEnum.Created,
                    $"checkout:{suffix}:{Guid.CreateVersion7():N}");
                return await repository.ClaimAsync(
                    new(replacement, CheckoutDispatchEffect.Create(replacement, UtcNow)), token);
            }, cancellationToken);
        }
        catch (Exception exception) when (
            exception is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.UniqueViolation } or
            DbUpdateException)
        {
            return null;
        }
    }

    private async Task<RegistrationPaymentAttemptClaimOutcome> CreateFreshPaymentClaimAsync(
        PaymentRaceSeed seed,
        string compositionRevision,
        CancellationToken cancellationToken)
    {
        await using ExploreDbContext context = CreateRetryingTenantContext(seed.TenantId);
        var repository = new RegistrationPaymentAttemptRepository(context);
        var unitOfWork = new EfCoreUnitOfWork(context);
        return await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var inventory = new RegistrationInventoryRepository(context);
            RegistrationOrder order = (await inventory.GetOrderForUpdateWithLinesAsync(
                seed.OrderId,
                seed.TenantId,
                token))!;
            PaymentAttempt attempt = CreatePaymentAttempt(
                order,
                PaymentAttemptStatusEnum.Created,
                $"checkout:fresh:{Guid.CreateVersion7():N}",
                compositionRevision);
            return await repository.ClaimAsync(
                new(attempt, CheckoutDispatchEffect.Create(attempt, UtcNow)), token);
        }, cancellationToken);
    }

    private async Task<PaymentRaceSeed> SeedPaymentRaceAsync(
        PaymentAttemptStatusEnum status,
        bool expiredCutoff = false,
        bool activePromotion = false)
    {
        (Guid tenantId, Guid eventId, Guid poolId, Guid ticketTypeId, _) = await SeedAsync(paidTickets: true);
        await using ExploreDbContext context = CreateRetryingTenantContext(tenantId);
        EventTicketCatalogVersion catalog = await context.EventTicketCatalogVersions
            .Include(value => value.TicketTypes)
            .SingleAsync(value => value.EventId == eventId);
        EventTicketType ticketType = catalog.TicketTypes.Single(value => value.Id == ticketTypeId);
        DateTime cutoff = expiredCutoff ? UtcNow.AddMinutes(-1) : UtcNow.AddMinutes(30);
        RegistrationOrder order = RegistrationOrder.Create(
            tenantId,
            eventId,
            null,
            null,
            BookingPartyTypeEnum.Individual,
            catalog.Id,
            RegistrationParticipationSnapshot.Create(Guid.CreateVersion7(), 4, 3, 2, GuestRecoveryPolicyEnum.VerifiedEmailRequired),
            null,
            CreateGuestCapabilityHash(Guid.CreateVersion7()),
            catalog.CurrencyCode,
            UtcNow.AddMinutes(-5),
            cutoff);
        order.AddLine(RegistrationOrderLine.Create(Guid.CreateVersion7(), catalog, ticketType, order.Id, 1, null, null));
        order.ApplyTotals(RegistrationOrderTotalsSnapshot.Create(catalog.CurrencyCode, 100, 0, 100, 0));
        order.TransitionTo(RegistrationOrderStatusEnum.AwaitingParticipantDetails, UtcNow.AddMinutes(-4));
        order.TransitionTo(RegistrationOrderStatusEnum.AwaitingRequirements, UtcNow.AddMinutes(-4));
        order.TransitionTo(RegistrationOrderStatusEnum.ReadyForCheckout, UtcNow.AddMinutes(-3));
        order.TransitionTo(RegistrationOrderStatusEnum.AwaitingPayment, UtcNow.AddMinutes(-2));
        RegistrationInventoryHold hold = RegistrationInventoryHold.Create(
            Guid.CreateVersion7(), order.Id, poolId, ticketType.Id, tenantId, 1, UtcNow.AddMinutes(-5), cutoff);
        PaymentAttempt attempt = CreatePaymentAttempt(order, status, $"checkout:seed:{Guid.CreateVersion7():N}");
        PromotionReservation? reservation = null;
        if (activePromotion)
        {
            PromotionScopeMetadata scope = PromotionScopeMetadata.Create(
                tenantId, eventId, catalog.Id, catalog.VersionNumber, catalog.CurrencyCode);
            PromotionDefinition definition = PromotionDefinition.CreateDraft(
                scope,
                "Expiry promotion",
                PromotionEligibility.AllTickets(),
                PromotionDiscountRule.FixedMinor(catalog.CurrencyCode, 1, maximumDiscountMinor: null),
                UtcNow.AddDays(-1),
                UtcNow.AddDays(1),
                10,
                1);
            definition.Publish(UtcNow.AddMinutes(-3));
            PromotionCode code = PromotionCode.Create(definition, "EXPIRY", scope);
            reservation = PromotionReservation.Reserve(Guid.CreateVersion7(), order, definition, code, UtcNow.AddMinutes(-2));
            var promotions = new PromotionManagementRepository(context);
            await promotions.AddDefinitionAsync(definition, CancellationToken.None);
            await promotions.AddPublishedCodeAsync(
                code,
                new PromotionCodeDigest(1, "expiry-promotion-digest"),
                CancellationToken.None);
            context.Add(reservation);
        }

        context.AddRange(order, hold, attempt, CheckoutDispatchEffect.Create(attempt, UtcNow.AddMinutes(-2)));
        await context.SaveChangesAsync();
        return new(tenantId, order.Id, hold.Id, attempt.Id, order, reservation?.Id);
    }

    private static PaymentAttempt CreatePaymentAttempt(
        RegistrationOrder order,
        PaymentAttemptStatusEnum status,
        string idempotencyKey,
        string? compositionRevision = null)
    {
        OrganizerPaymentRecipientSnapshot recipient =
            OrganizerPaymentRecipientSnapshot.Create(
                order.TenantId,
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                "stripe",
                "platform-eu",
                "acct_race",
                "BE",
                order.CurrencyCode,
                Guid.CreateVersion7(),
                null,
                UtcNow.AddMinutes(-5));
        PaymentAttempt attempt = PaymentAttempt.Create(
            Guid.CreateVersion7(),
            order.TenantId,
            order.Id,
            recipient,
            "OrganizerDirect",
            "2026-08-20.acacia",
            compositionRevision ?? order.ConcurrencyStamp.ToString("N"),
            Money.Create(order.OrganizerDirectedTotalMinorSnapshot, order.CurrencyCode),
            Money.Create(order.PlatformFeeTotalMinorSnapshot, order.CurrencyCode),
            Money.Create(order.PlatformContributionTotalMinorSnapshot, order.CurrencyCode),
            idempotencyKey,
            UtcNow.AddMinutes(-2),
            order.ExpiresAt);
        if (status == PaymentAttemptStatusEnum.RequiresAction)
        {
            attempt.MarkRequiresAction("cs_race", UtcNow.AddMinutes(-1), "req_create");
        }
        else if (status == PaymentAttemptStatusEnum.Failed)
        {
            attempt.MarkDispatchFailed(UtcNow.AddMinutes(-1), "req_failed");
        }

        attempt.AttachAcceptance(PaidAcceptanceTestFacts.Create(
            order.TenantId,
            order.Id,
            order.EventId,
            attempt.CompositionRevision,
            recipient.InstancePolicyVersionId,
            order.OrganizerDirectedTotalMinorSnapshot,
            order.PlatformFeeTotalMinorSnapshot,
            order.PlatformContributionTotalMinorSnapshot,
            UtcNow.AddMinutes(-2),
            order.CurrencyCode,
            recipient));
        return attempt;
    }

    private sealed record PaymentRaceSeed(
        Guid TenantId,
        Guid OrderId,
        Guid HoldId,
        Guid AttemptId,
        RegistrationOrder Order,
        Guid? PromotionReservationId = null);

    private async Task<bool> ReserveAsync(
        Guid tenantId,
        Guid eventId,
        Guid poolId,
        Guid ticketTypeId,
        CancellationToken cancellationToken)
    {
        await using ExploreDbContext context = CreateRetryingTenantContext(tenantId);
        var inventory = new RegistrationInventoryRepository(context);
        var catalogs = new EventTicketCatalogRepository(context);
        var unitOfWork = new EfCoreUnitOfWork(context);
        Guid orderId = Guid.CreateVersion7();
        Guid accountUserId = Guid.CreateVersion7();
        Guid lineId = Guid.CreateVersion7();
        Guid holdId = Guid.CreateVersion7();

        return await unitOfWork.ExecuteSerializableAsync(async token =>
        {
            EventTicketCatalogVersion catalog = (await catalogs.GetPublishedCatalogAsync(eventId, tenantId, token))!;
            EventTicketType ticketType = catalog.TicketTypes.Single(ticket => ticket.Id == ticketTypeId);
            EventCapacityPool pool = (await inventory.GetPoolsForUpdateAsync([poolId], eventId, tenantId, token)).Single();
            int allocated = await inventory.GetAllocatedQuantityAsync(pool.Id, tenantId, token);
            if (pool.MaximumQuantity is int maximumQuantity && allocated >= maximumQuantity)
            {
                return false;
            }

            RegistrationOrder order = RegistrationOrder.Create(
                orderId,
                tenantId,
                eventId,
                accountUserId,
                purchaserActorId: null,
                bookingPartyType: BookingPartyTypeEnum.Individual,
                ticketCatalogVersionId: catalog.Id,
                participationSnapshot: RegistrationParticipationSnapshot.Create(Guid.CreateVersion7(), 4, 3, 2, GuestRecoveryPolicyEnum.VerifiedEmailRequired),
                registrationWorkflowVersionId: null,
                guestAccessTokenHash: null,
                currencyCode: catalog.CurrencyCode,
                createdAt: UtcNow,
                expiresAt: UtcNow.AddSeconds(pool.HoldDurationSeconds));
            order.AddLine(RegistrationOrderLine.Create(
                lineId,
                catalog,
                ticketType,
                order.Id,
                quantity: 1,
                chosenUnitPriceAmount: null,
                platformFeePolicy: null));
            RegistrationInventoryHold hold = RegistrationInventoryHold.Create(
                holdId,
                order.Id,
                pool.Id,
                ticketType.Id,
                tenantId,
                quantity: 1,
                UtcNow,
                UtcNow.AddSeconds(pool.HoldDurationSeconds));

            await inventory.AddOrderWithHoldsAsync(order, [hold], token);
            await inventory.SaveChangesAsync(token);
            return true;
        }, cancellationToken);
    }

    private async Task<bool> ReserveNonTimedAtReadyAsync(
        Guid tenantId,
        Guid eventId,
        Guid poolId,
        Guid ticketTypeId,
        CancellationToken cancellationToken)
    {
        await using ExploreDbContext context = CreateRetryingTenantContext(tenantId);
        var inventory = new RegistrationInventoryRepository(context);
        var catalogs = new EventTicketCatalogRepository(context);
        var unitOfWork = new EfCoreUnitOfWork(context);
        Guid orderId = Guid.CreateVersion7();
        Guid lineId = Guid.CreateVersion7();
        Guid holdId = Guid.CreateVersion7();

        return await unitOfWork.ExecuteSerializableAsync(async token =>
        {
            EventTicketCatalogVersion catalog = (await catalogs.GetPublishedCatalogAsync(eventId, tenantId, token))!;
            EventTicketType ticketType = catalog.TicketTypes.Single(ticket => ticket.Id == ticketTypeId);
            RegistrationOrder order = RegistrationOrder.Create(
                orderId,
                tenantId,
                eventId,
                accountUserId: null,
                purchaserActorId: null,
                bookingPartyType: BookingPartyTypeEnum.Individual,
                ticketCatalogVersionId: catalog.Id,
                participationSnapshot: RegistrationParticipationSnapshot.Create(Guid.CreateVersion7(), 4, 3, 2, GuestRecoveryPolicyEnum.VerifiedEmailRequired),
                registrationWorkflowVersionId: null,
                guestAccessTokenHash: CreateGuestCapabilityHash(orderId),
                currencyCode: catalog.CurrencyCode,
                createdAt: UtcNow,
                expiresAt: null);
            order.AddLine(RegistrationOrderLine.Create(lineId, catalog, ticketType, order.Id, 1, null, null));
            order.TransitionTo(RegistrationOrderStatusEnum.AwaitingParticipantDetails, UtcNow);
            order.TransitionTo(RegistrationOrderStatusEnum.AwaitingRequirements, UtcNow);
            await inventory.AddOrderWithHoldsAsync(order, [], token);
            await inventory.SaveChangesAsync(token);

            RegistrationInventoryReservationResult reservation = await inventory.ReserveNonTimedHoldsAsync(
                eventId,
                tenantId,
                [new RegistrationInventoryReservation(holdId, order.Id, poolId, ticketType.Id, 1)],
                approvalGranted: false,
                UtcNow,
                token);
            return reservation.Reserved;
        }, cancellationToken);
    }

    private async Task<RegistrationOrderLifecycleResponseDto> FinalizePaidAsync(
        Guid tenantId,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        await using ExploreDbContext context = CreateRetryingTenantContext(tenantId);
        var inventory = new RegistrationInventoryRepository(context);
        var service = new RegistrationOrderLifecycleService(
            inventory,
            new PromotionRedemptionRepository(context),
            new RegistrationParticipantRepository(context),
            new EventTicketCatalogRepository(context),
            new PlatformContributionSettingRepository(context),
            new EventSessionRepository(context),
            new OutboxRepository(context),
            new EfCoreUnitOfWork(context),
            new RegistrationFinalizationRepository(context),
            new RegistrationPaymentAttemptRepository(context),
            Substitute.For<IScheduledDeadlineDispatcher>(),
            new FixedTimeProvider(UtcNow),
            Substitute.For<IPaidOrderAcceptanceService>(),
            new RegistrationOrderTransitionCoordinator(inventory));
        return await service.FinalizePaidAsync(orderId, tenantId, cancellationToken);
    }

    private async Task<RegistrationOrderLifecycleResponseDto> FinalizePaidWithOutboxFailureAsync(
        Guid tenantId,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        await using ExploreDbContext context = CreateRetryingTenantContext(tenantId);
        IOutboxRepository outbox = Substitute.For<IOutboxRepository>();
        outbox.Create(Arg.Any<OutboxMessage>())
            .Returns(Task.FromException<OutboxMessage>(new InvalidOperationException("manual_outbox_failure")));
        var inventory = new RegistrationInventoryRepository(context);
        var service = new RegistrationOrderLifecycleService(
            inventory,
            new PromotionRedemptionRepository(context),
            new RegistrationParticipantRepository(context),
            new EventTicketCatalogRepository(context),
            new PlatformContributionSettingRepository(context),
            new EventSessionRepository(context),
            outbox,
            new EfCoreUnitOfWork(context),
            new RegistrationFinalizationRepository(context),
            new RegistrationPaymentAttemptRepository(context),
            Substitute.For<IScheduledDeadlineDispatcher>(),
            new FixedTimeProvider(UtcNow),
            Substitute.For<IPaidOrderAcceptanceService>(),
            new RegistrationOrderTransitionCoordinator(inventory));
        return await service.FinalizePaidAsync(orderId, tenantId, cancellationToken);
    }

    private async Task<(Guid FirstOrderId, Guid SecondOrderId)> SeedPaidOrdersAsync(
        Guid tenantId,
        Guid eventId,
        Guid firstTicketTypeId,
        Guid secondTicketTypeId)
    {
        await using ExploreDbContext context = fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenantId));
        EventTicketCatalogVersion catalog = await context.EventTicketCatalogVersions
            .Include(value => value.TicketTypes)
            .SingleAsync(value => value.EventId == eventId);
        EventTicketType firstTicket = catalog.TicketTypes.Single(ticket => ticket.Id == firstTicketTypeId);
        EventTicketType secondTicket = catalog.TicketTypes.Single(ticket => ticket.Id == secondTicketTypeId);
        RegistrationOrder firstOrder = CreatePaidOrder(catalog, firstTicket);
        RegistrationOrder secondOrder = CreatePaidOrder(catalog, secondTicket);
        PaymentAttempt firstAttempt = CreateSucceededPayment(firstOrder);
        PaymentAttempt secondAttempt = CreateSucceededPayment(secondOrder);
        context.AddRange(
            firstOrder,
            secondOrder,
            firstAttempt,
            secondAttempt,
            PaymentSucceededObservation.Create(firstAttempt, null, "cs_first", "pi_first", null, UtcNow),
            PaymentSucceededObservation.Create(secondAttempt, null, "cs_second", "pi_second", null, UtcNow),
            RegistrationFinalizationEffect.Create(firstOrder, UtcNow),
            RegistrationFinalizationEffect.Create(secondOrder, UtcNow));
        await context.SaveChangesAsync();
        return (firstOrder.Id, secondOrder.Id);
    }

    private static RegistrationOrder CreatePaidOrder(
        EventTicketCatalogVersion catalog,
        EventTicketType ticketType)
    {
        RegistrationOrder order = RegistrationOrder.Create(
            catalog.TenantId,
            catalog.EventId,
            accountUserId: null,
            purchaserActorId: null,
            BookingPartyTypeEnum.Individual,
            catalog.Id,
            RegistrationParticipationSnapshot.Create(Guid.CreateVersion7(), 4, 3, 2, GuestRecoveryPolicyEnum.VerifiedEmailRequired),
            registrationWorkflowVersionId: null,
            CreateGuestCapabilityHash(Guid.CreateVersion7()),
            catalog.CurrencyCode,
            UtcNow.AddMinutes(-5),
            UtcNow.AddMinutes(-1));
        order.AddLine(RegistrationOrderLine.Create(Guid.CreateVersion7(), catalog, ticketType, order.Id, 1, null, null));
        order.ApplyTotals(RegistrationOrderTotalsSnapshot.Create(catalog.CurrencyCode, 100, 0, 100, 0));
        order.TransitionTo(RegistrationOrderStatusEnum.AwaitingParticipantDetails, UtcNow.AddMinutes(-4));
        order.TransitionTo(RegistrationOrderStatusEnum.AwaitingRequirements, UtcNow.AddMinutes(-4));
        order.TransitionTo(RegistrationOrderStatusEnum.ReadyForCheckout, UtcNow.AddMinutes(-3));
        order.TransitionTo(RegistrationOrderStatusEnum.AwaitingPayment, UtcNow.AddMinutes(-2));
        return order;
    }

    private static PaymentAttempt CreateSucceededPayment(RegistrationOrder order)
    {
        string suffix = order.Id.ToString("N");
        OrganizerPaymentRecipientSnapshot recipient = OrganizerPaymentRecipientSnapshot.Create(
            order.TenantId,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "stripe",
            "platform-eu",
            $"acct_{suffix}",
            "BE",
            order.CurrencyCode,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            UtcNow.AddMinutes(-5));
        PaymentAttempt attempt = PaymentAttempt.Create(
            Guid.CreateVersion7(),
            order.TenantId,
            order.Id,
            recipient,
            "OrganizerDirect",
            "2026-08-20.acacia",
            $"composition-{suffix}",
            Money.Create(order.OrganizerDirectedTotalMinorSnapshot, recipient.CurrencyCode),
            Money.Create(order.PlatformFeeTotalMinorSnapshot, recipient.CurrencyCode),
            Money.Create(order.PlatformContributionTotalMinorSnapshot, recipient.CurrencyCode),
            $"checkout:{suffix}",
            UtcNow.AddMinutes(-2),
            UtcNow.AddMinutes(30));
        string checkoutId = order.Lines.Single().TicketTypeNameSnapshot == "General" ? "cs_first" : "cs_second";
        string paymentId = order.Lines.Single().TicketTypeNameSnapshot == "General" ? "pi_first" : "pi_second";
        attempt.MarkSucceededFromCheckout(checkoutId, paymentId, UtcNow, null);
        return attempt;
    }

    private ExploreDbContext CreateRetryingTenantContext(Guid tenantId)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseNpgsql(fixture.ConnectionString, npgsql => npgsql.EnableRetryOnFailure(
                maxRetryCount: 3,
                maxRetryDelay: TimeSpan.FromSeconds(5),
                errorCodesToAdd: null))
            .UseSnakeCaseNamingConvention()
            .ConfigureWarnings(warnings => warnings.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning))
            .Options;

        return new ExploreDbContext(options)
        {
            TenantContext = new TestTenantContext(tenantId)
        };
    }

    private static CapabilityTokenHash CreateGuestCapabilityHash(Guid orderId) =>
        CapabilityTokenHash.Create(Convert.ToBase64String(
            System.Security.Cryptography.SHA256.HashData(orderId.ToByteArray())));

    private async Task<(Guid TenantId, Guid EventId, Guid PoolId, Guid FirstTicketTypeId, Guid SecondTicketTypeId)> SeedAsync(
        int maximumQuantity = 1,
        CapacityHoldPolicyEnum holdPolicy = CapacityHoldPolicyEnum.TimedHoldOnSelection,
        bool paidTickets = false)
    {
        await fixture.ResetAsync();
        await using ExploreDbContext context = fixture.CreateDbContext();
        TenantStatus activeStatus = await context.TenantStatuses.SingleAsync(status => status.Id == (int)TenantStatusEnum.Active);
        var tenant = new Tenant { FullName = "Registration hold race tenant", Slug = $"registration-hold-race-{Guid.NewGuid():N}", TenantStatusId = activeStatus.Id, TenantStatus = activeStatus };
        context.Tenants.Add(tenant);
        var user = new User { Pii = new UserPii { Email = "registration-hold-race@example.test", FirstName = "Registration", LastName = "Race" } };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var actor = new Actor { Pii = new ActorPii { DisplayName = "Registration Hold Race Actor" }, ActorTypeId = 1, ActorType = null!, UserId = user.Id };
        context.Actors.Add(actor);
        await context.SaveChangesAsync();

        Guid eventId = Guid.CreateVersion7();
        var eventTarget = new DomainEvent(EventStatusEnum.Draft)
        {
            Id = eventId,
            Title = "Registration hold race event",
            Subtitle = string.Empty,
            Description = string.Empty,
            FirstSessionDate = DateOnly.FromDateTime(UtcNow.AddDays(1)),
            LastSessionDate = DateOnly.FromDateTime(UtcNow.AddDays(1)),
            EventTypeId = 1,
            AudienceGenderId = 1,
            AudienceAgeId = 1,
            ActorId = actor.Id,
            Actor = null!,
            OrganizerActorId = actor.Id,
            TenantId = tenant.Id,
            Tenant = tenant,
            VisibilityTypeId = 1,
            VisibilityType = null!,
            EventStatus = null!,
            EventFormatId = 1,
            EventFormat = null!,
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            TotalViews = 0
        };
        EventTicketCatalogVersion catalog = EventTicketCatalogVersion.Create(tenant.Id, eventId, "USD", 1);
        EventCapacityPool pool = EventCapacityPool.Create(tenant.Id, eventId, "Shared last seat", maximumQuantity, 900, holdPolicy, CapacityOversellPolicyEnum.Disallow, true);
        EventTicketType firstTicket = CreateTicketType(catalog, "General", pool.Id, paidTickets);
        EventTicketType secondTicket = CreateTicketType(catalog, "Student", pool.Id, paidTickets);
        catalog.AddTicketType(firstTicket, pool);
        catalog.AddTicketType(secondTicket, pool);
        catalog.AddEntitlement(firstTicket, TicketTypeEntitlement.CreateForEvent(firstTicket.Id, tenant.Id, eventId, 1));
        catalog.AddEntitlement(secondTicket, TicketTypeEntitlement.CreateForEvent(secondTicket.Id, tenant.Id, eventId, 1));
        if (paidTickets)
        {
            catalog.UpdateCommercialDisclosures("Merchant", "Refund", "Support");
        }
        catalog.Publish();
        var session = new EventSession
        {
            Id = Guid.CreateVersion7(),
            EventId = eventId,
            Event = null!,
            TenantId = tenant.Id,
            Tenant = tenant,
            Title = "Admission session",
            RegistrationModeId = (int)RegistrationModeEnum.Open
        };
        context.AddRange(eventTarget, catalog, pool, session);
        await context.SaveChangesAsync();

        return (tenant.Id, eventId, pool.Id, firstTicket.Id, secondTicket.Id);
    }

    private static EventTicketType CreateTicketType(
        EventTicketCatalogVersion catalog,
        string name,
        Guid poolId,
        bool paid = false) => EventTicketType.Create(
        Guid.CreateVersion7(),
        catalog.TenantId,
        catalog.Id,
        name,
        catalog.CurrencyCode,
        paid ? TicketPricingModeEnum.Fixed : TicketPricingModeEnum.Free,
        fixedPrice: paid ? Money.Create(100, catalog.CurrencyCode) : null,
        minimumPrice: null,
        suggestedPrice: null,
        ParticipantDataCollectionModeEnum.None,
        poolId,
        minimumAge: null,
        maximumAge: null,
        requiresGuardian: false,
        requiresApproval: false,
        perOrderLimit: null,
        perAccountLimit: null,
        perVerifiedContactLimit: null,
        perBookingPartyLimit: null);

    private sealed class FixedTimeProvider(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private async Task<long> ScalarAsync(
        string sql,
        params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach ((string name, object value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private sealed record TestTenantContext(Guid TenantId) : ITenantContext;
}
