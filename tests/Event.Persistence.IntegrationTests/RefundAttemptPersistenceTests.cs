// ABOUTME: Proves refund reservation, dispute, duplicate, and tenant persistence invariants on SQLite.
// ABOUTME: Exercises the repository contract without provider I/O or generated migrations.

using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Services.Registration;
using Explore.Domain;
using Explore.Domain.ValueObjects;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests;

public sealed class RefundAttemptPersistenceTests
{
    private static readonly DateTime UtcNow = new(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid TenantA = Guid.Parse("018e4e5c-7f00-7000-8000-000000000001");
    private static readonly Guid TenantB = Guid.Parse("018e4e5c-7f00-7000-8000-000000000002");

    [Test]
    public async Task TenantQualifiedRefundAndDisputeIdentitiesPersistIndependentlyAndFilterByTenant()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        DbContextOptions<ExploreDbContext> options = Options(connection);
        Guid paymentA = Guid.CreateVersion7();
        Guid paymentB = Guid.CreateVersion7();
        Guid orderA = Guid.CreateVersion7();
        Guid orderB = Guid.CreateVersion7();

        await using (var setup = await CreateContextAsync(options, bypassTenantFilter: true))
        {
            await SeedCapturedPaymentAsync(setup, TenantA, paymentA, orderA);
            await SeedCapturedPaymentAsync(setup, TenantB, paymentB, orderB);
            var repository = new RefundAttemptRepository(setup);

            RefundReservationResult refundA = await repository.ReserveAsync(
                Refund(TenantA, paymentA, orderA, 400, "refund:shared"), CancellationToken.None);
            RefundReservationResult refundB = await repository.ReserveAsync(
                Refund(TenantB, paymentB, orderB, 400, "refund:shared"), CancellationToken.None);
            PaymentDispute disputeA = await repository.ObserveDisputeAsync(
                Dispute(TenantA, paymentA, "dp_shared"), CancellationToken.None);
            PaymentDispute disputeB = await repository.ObserveDisputeAsync(
                Dispute(TenantB, paymentB, "dp_shared"), CancellationToken.None);

            await Assert.That(refundA.Disposition).IsEqualTo(RefundReservationDisposition.Reserved);
            await Assert.That(refundB.Disposition).IsEqualTo(RefundReservationDisposition.Reserved);
            await Assert.That(disputeA.Id).IsNotEqualTo(disputeB.Id);
        }

        await using var tenantA = new ExploreDbContext(options) { TenantContext = new TestTenantContext(TenantA) };
        await Assert.That(await tenantA.RefundAttempts.CountAsync()).IsEqualTo(1);
        await Assert.That(await tenantA.PaymentDisputes.CountAsync()).IsEqualTo(1);
        await Assert.That((await tenantA.RefundAttempts.SingleAsync()).Allocation.TotalMinor).IsEqualTo(400L);
    }

    [Test]
    public async Task DuplicateReservationIsIdempotentAndAmbiguousStatesKeepCapacityUntilDefinitiveRelease()
    {
        await using ExploreDbContext context = await CreateContextAsync();
        Guid paymentId = Guid.CreateVersion7();
        Guid orderId = Guid.CreateVersion7();
        await SeedCapturedPaymentAsync(context, TenantA, paymentId, orderId);
        var repository = new RefundAttemptRepository(context);
        RefundAttempt first = Refund(TenantA, paymentId, orderId, 700, "refund:capacity");

        RefundReservationResult reserved = await repository.ReserveAsync(first, CancellationToken.None);
        RefundReservationResult duplicate = await repository.ReserveAsync(
            Refund(TenantA, paymentId, orderId, 700, "refund:capacity"), CancellationToken.None);
        first.MarkUnknown(UtcNow.AddMinutes(2), "req_ambiguous");
        await repository.SaveChangesAsync(CancellationToken.None);
        RefundReservationResult blocked = await repository.ReserveAsync(
            Refund(TenantA, paymentId, orderId, 400, "refund:blocked"), CancellationToken.None);
        first.MarkFailed("re_capacity", UtcNow.AddMinutes(3), "req_failed");
        await repository.SaveChangesAsync(CancellationToken.None);
        RefundReservationResult afterRelease = await repository.ReserveAsync(
            Refund(TenantA, paymentId, orderId, 400, "refund:released"), CancellationToken.None);

        await Assert.That(reserved.Disposition).IsEqualTo(RefundReservationDisposition.Reserved);
        await Assert.That(duplicate.Disposition).IsEqualTo(RefundReservationDisposition.Duplicate);
        await Assert.That(duplicate.Attempt!.Id).IsEqualTo(first.Id);
        await Assert.That(blocked.Disposition).IsEqualTo(RefundReservationDisposition.CapacityExceeded);
        await Assert.That(afterRelease.Disposition).IsEqualTo(RefundReservationDisposition.Reserved);
        await Assert.That(await context.RefundAttempts.CountAsync()).IsEqualTo(2);
    }

    [Test]
    public async Task MultipleDisputesPersistIndependentlyDuplicateObservationIsIdempotentAndOpenDisputeBlocksRefund()
    {
        await using ExploreDbContext context = await CreateContextAsync();
        Guid paymentId = Guid.CreateVersion7();
        Guid orderId = Guid.CreateVersion7();
        await SeedCapturedPaymentAsync(context, TenantA, paymentId, orderId);
        var repository = new RefundAttemptRepository(context);
        PaymentDispute first = Dispute(TenantA, paymentId, "dp_first");
        PaymentDispute second = Dispute(TenantA, paymentId, "dp_second");

        PaymentDispute observedFirst = await repository.ObserveDisputeAsync(first, CancellationToken.None);
        PaymentDispute observedSecond = await repository.ObserveDisputeAsync(second, CancellationToken.None);
        PaymentDispute duplicate = await repository.ObserveDisputeAsync(
            Dispute(TenantA, paymentId, "dp_first"), CancellationToken.None);
        RefundReservationResult blocked = await repository.ReserveAsync(
            Refund(TenantA, paymentId, orderId, 100, "refund:disputed"), CancellationToken.None);

        await Assert.That(observedFirst.Id).IsNotEqualTo(observedSecond.Id);
        await Assert.That(duplicate.Id).IsEqualTo(observedFirst.Id);
        await Assert.That(await context.PaymentDisputes.CountAsync()).IsEqualTo(2);
        await Assert.That(blocked.Disposition).IsEqualTo(RefundReservationDisposition.OpenDispute);
        await Assert.That(await context.RefundAttempts.CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task DuplicateDisputeObservationAppliesLaterProviderEvidenceWithoutCreatingAnotherProjection()
    {
        await using ExploreDbContext context = await CreateContextAsync();
        Guid paymentId = Guid.CreateVersion7();
        Guid orderId = Guid.CreateVersion7();
        await SeedCapturedPaymentAsync(context, TenantA, paymentId, orderId);
        var repository = new RefundAttemptRepository(context);
        PaymentDispute opened = PaymentDispute.Observe(
            Guid.CreateVersion7(), TenantA, paymentId, "dp_late_win",
            PaymentDisputeStage.Inquiry, PaymentDisputeStatus.Open, 200, "EUR", UtcNow.AddMinutes(1));
        PaymentDispute won = PaymentDispute.Observe(
            Guid.CreateVersion7(), TenantA, paymentId, "dp_late_win",
            PaymentDisputeStage.Formal, PaymentDisputeStatus.Won, 200, "EUR", UtcNow.AddMinutes(3));

        PaymentDispute first = await repository.ObserveDisputeAsync(opened, CancellationToken.None);
        PaymentDispute updated = await repository.ObserveDisputeAsync(won, CancellationToken.None);

        await Assert.That(updated.Id).IsEqualTo(first.Id);
        await Assert.That(updated.Stage).IsEqualTo(PaymentDisputeStage.Formal);
        await Assert.That(updated.Status).IsEqualTo(PaymentDisputeStatus.Won);
        await Assert.That(updated.LastObservedAt).IsEqualTo(UtcNow.AddMinutes(3));
        await Assert.That(await context.PaymentDisputes.CountAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task StaleDisputeEvidenceCannotRegressLaterProviderTruth()
    {
        await using ExploreDbContext context = await CreateContextAsync();
        Guid paymentId = Guid.CreateVersion7();
        Guid orderId = Guid.CreateVersion7();
        await SeedCapturedPaymentAsync(context, TenantA, paymentId, orderId);
        var repository = new RefundAttemptRepository(context);
        PaymentDispute won = PaymentDispute.Observe(
            Guid.CreateVersion7(), TenantA, paymentId, "dp_stale",
            PaymentDisputeStage.Formal, PaymentDisputeStatus.Won, 200, "EUR", UtcNow.AddMinutes(3));
        PaymentDispute staleOpen = PaymentDispute.Observe(
            Guid.CreateVersion7(), TenantA, paymentId, "dp_stale",
            PaymentDisputeStage.Inquiry, PaymentDisputeStatus.Open, 200, "EUR", UtcNow.AddMinutes(2));

        _ = await repository.ObserveDisputeAsync(won, CancellationToken.None);
        PaymentDispute observed = await repository.ObserveDisputeAsync(staleOpen, CancellationToken.None);

        await Assert.That(observed.Stage).IsEqualTo(PaymentDisputeStage.Formal);
        await Assert.That(observed.Status).IsEqualTo(PaymentDisputeStatus.Won);
        await Assert.That(observed.LastObservedAt).IsEqualTo(UtcNow.AddMinutes(3));
    }

    [Test]
    public async Task CampaignCursorPagesCapturedPaymentsOnceAndPersistsContinuationAtomically()
    {
        await using ExploreDbContext context = await CreateContextAsync();
        Guid eventId = Guid.CreateVersion7();
        Guid firstPaymentId = Guid.CreateVersion7();
        Guid secondPaymentId = Guid.CreateVersion7();
        await SeedCapturedPaymentAsync(context, TenantA, firstPaymentId, Guid.CreateVersion7(), eventId);
        await SeedCapturedPaymentAsync(context, TenantA, secondPaymentId, Guid.CreateVersion7(), eventId);
        RefundCampaign campaign = RefundCampaign.CreateCancellation(
            Guid.CreateVersion7(), TenantA, eventId, Guid.CreateVersion7(), "Cancelled.", UtcNow.AddMinutes(1));
        var repository = new RefundCampaignRepository(context);
        await repository.CreateAsync(
            campaign, RefundOutboxMessageFactory.CreateCampaignProcess(campaign, UtcNow.AddMinutes(1)), CancellationToken.None);

        (RefundCampaign Campaign, RefundCampaignClaim Claim)? firstClaim = await repository.TryClaimAsync(
            TenantA, campaign.Id, Guid.CreateVersion7(), UtcNow.AddMinutes(2), TimeSpan.FromMinutes(5), CancellationToken.None);
        RefundCampaignPaymentPage firstPage = await repository.GetCapturedPaymentPageAsync(
            firstClaim!.Value.Campaign, 1, CancellationToken.None);
        PaymentAttempt first = firstPage.Payments.Single();
        await repository.CompleteBatchAsync(
            TenantA, campaign.Id, firstClaim.Value.Claim, first.CampaignCursor,
            new RefundCampaignBatchOutcome(1, 1, 0), true, [],
            [RefundOutboxMessageFactory.CreateCampaignProcess(campaign, UtcNow.AddMinutes(2))],
            UtcNow.AddMinutes(2), CancellationToken.None);

        (RefundCampaign Campaign, RefundCampaignClaim Claim)? secondClaim = await repository.TryClaimAsync(
            TenantA, campaign.Id, Guid.CreateVersion7(), UtcNow.AddMinutes(3), TimeSpan.FromMinutes(5), CancellationToken.None);
        RefundCampaignPaymentPage secondPage = await repository.GetCapturedPaymentPageAsync(
            secondClaim!.Value.Campaign, 1, CancellationToken.None);
        PaymentAttempt second = secondPage.Payments.Single();
        await repository.CompleteBatchAsync(
            TenantA, campaign.Id, secondClaim.Value.Claim, second.CampaignCursor,
            new RefundCampaignBatchOutcome(1, 1, 0), false, [], [], UtcNow.AddMinutes(3), CancellationToken.None);

        RefundCampaign persisted = (await repository.GetByIdAsync(TenantA, campaign.Id, CancellationToken.None))!;
        await Assert.That(first.Id).IsNotEqualTo(second.Id);
        await Assert.That(firstPage.HasMore).IsTrue();
        await Assert.That(secondPage.HasMore).IsFalse();
        await Assert.That(persisted.Status).IsEqualTo(RefundCampaignStatus.Completed);
        await Assert.That(persisted.TotalPaymentCount).IsEqualTo(2);
        await Assert.That(persisted.GeneratedCount).IsEqualTo(0);
        await Assert.That(await context.OutboxMessages.CountAsync()).IsEqualTo(2);
    }

    [Test]
    public async Task SequentialPartialReservationsConsumeCumulativeFeeRoundingOnlyOnce()
    {
        await using ExploreDbContext context = await CreateContextAsync();
        Guid paymentId = Guid.CreateVersion7();
        Guid orderId = Guid.CreateVersion7();
        await SeedCapturedPaymentAsync(context, TenantA, paymentId, orderId);
        var repository = new RefundAttemptRepository(context);

        RefundReservationResult first = await repository.ReserveAsync(
            Refund(TenantA, paymentId, orderId, 7, "refund:cumulative:1"), CancellationToken.None);
        RefundReservationResult second = await repository.ReserveAsync(
            Refund(TenantA, paymentId, orderId, 7, "refund:cumulative:2"), CancellationToken.None);

        first.Attempt!.MarkDispatchPending(UtcNow.AddMinutes(2), null);
        first.Attempt.MarkFailed("re_first", UtcNow.AddMinutes(3), null);
        await repository.SaveChangesAsync(CancellationToken.None);
        RefundReservationResult replacement = await repository.ReserveAsync(
            Refund(TenantA, paymentId, orderId, 7, "refund:cumulative:replacement"), CancellationToken.None);

        await Assert.That(first.Disposition).IsEqualTo(RefundReservationDisposition.Reserved);
        await Assert.That(second.Disposition).IsEqualTo(RefundReservationDisposition.Reserved);
        await Assert.That(replacement.Disposition).IsEqualTo(RefundReservationDisposition.Reserved);
        long allocatedFee = await context.RefundAttempts
            .Where(value => value.TenantId == TenantA && value.PaymentAttemptId == paymentId &&
                            value.Status != RefundAttemptStatusEnum.Failed &&
                            value.Status != RefundAttemptStatusEnum.Cancelled)
            .SumAsync(value => value.Allocation.PlatformFeeMinor);
        await Assert.That(allocatedFee).IsEqualTo(1);
    }

    [Test]
    public async Task ResumeCampaignExplicitlyRequeuesExistingProviderBlockedAttempt()
    {
        await using ExploreDbContext context = await CreateContextAsync();
        Guid eventId = Guid.CreateVersion7();
        Guid paymentId = Guid.CreateVersion7();
        Guid orderId = Guid.CreateVersion7();
        await SeedCapturedPaymentAsync(context, TenantA, paymentId, orderId, eventId);
        RefundCampaign campaign = RefundCampaign.CreateCancellation(
            Guid.CreateVersion7(), TenantA, eventId, Guid.CreateVersion7(), "Cancelled.", UtcNow.AddMinutes(1));
        var campaignRepository = new RefundCampaignRepository(context);
        await campaignRepository.CreateAsync(
            campaign, RefundOutboxMessageFactory.CreateCampaignProcess(campaign, UtcNow.AddMinutes(1)), CancellationToken.None);
        RefundAttempt blocked = RefundAttempt.Create(
            Guid.CreateVersion7(), TenantA, paymentId, Acceptance(TenantA, paymentId, orderId), "acct_original",
            PaymentProviderId(paymentId), "refund:campaign:blocked", 100, UtcNow.AddMinutes(2), campaign.Id, "campaign");
        blocked.MarkDispatchPending(UtcNow.AddMinutes(3), null);
        blocked.MarkProviderBlocked(UtcNow.AddMinutes(4), "req_blocked", "refund_provider_rejected");
        context.RefundAttempts.Add(blocked);
        await context.SaveChangesAsync();

        bool resumed = await campaignRepository.ResumeAsync(
            TenantA,
            campaign.Id,
            RefundOutboxMessageFactory.CreateCampaignProcess(campaign, UtcNow.AddMinutes(5)),
            UtcNow.AddMinutes(5),
            CancellationToken.None);

        RefundAttempt recovered = await context.RefundAttempts.SingleAsync(value => value.Id == blocked.Id);
        await Assert.That(resumed).IsTrue();
        await Assert.That(recovered.Status).IsEqualTo(RefundAttemptStatusEnum.Unknown);
        await Assert.That(recovered.FailureCode).IsNull();
        await Assert.That(await context.OutboxMessages.CountAsync(
            value => value.AggregateId == blocked.Id &&
                     value.EventType == RefundOutboxMessageFactory.ReconciliationRequested)).IsEqualTo(1);
    }

    [Test]
    public async Task RetryProviderBlockedRefundRequeuesTheSameAttemptAndSchedulesReconciliation()
    {
        await using ExploreDbContext context = await CreateContextAsync();
        Guid paymentId = Guid.CreateVersion7();
        Guid orderId = Guid.CreateVersion7();
        await SeedCapturedPaymentAsync(context, TenantA, paymentId, orderId);
        RefundAttempt blocked = Refund(TenantA, paymentId, orderId, 100, "refund:blocked:retry");
        blocked.MarkDispatchPending(UtcNow.AddMinutes(2), null);
        blocked.MarkProviderBlocked(UtcNow.AddMinutes(3), "req_blocked", "refund_provider_rejected");
        context.RefundAttempts.Add(blocked);
        await context.SaveChangesAsync();
        var repository = new RefundAttemptRepository(context);

        bool retried = await repository.RetryProviderBlockedAndScheduleAsync(
            blocked,
            RefundOutboxMessageFactory.CreateReconciliation(blocked, UtcNow.AddMinutes(4), UtcNow.AddMinutes(4)),
            UtcNow.AddMinutes(4),
            CancellationToken.None);

        await Assert.That(retried).IsTrue();
        await Assert.That(blocked.Status).IsEqualTo(RefundAttemptStatusEnum.Unknown);
        await Assert.That(blocked.FailureCode).IsNull();
        await Assert.That(await context.OutboxMessages.CountAsync(
            value => value.AggregateId == blocked.Id &&
                     value.EventType == RefundOutboxMessageFactory.ReconciliationRequested)).IsEqualTo(1);
    }

    private static RefundAttempt Refund(
        Guid tenantId,
        Guid paymentId,
        Guid orderId,
        long totalMinor,
        string idempotencyKey) =>
        RefundAttempt.Create(
            Guid.CreateVersion7(), tenantId, paymentId, Acceptance(tenantId, paymentId, orderId), "acct_original",
            paymentId == Guid.Empty ? string.Empty : PaymentProviderId(paymentId), idempotencyKey,
            totalMinor, UtcNow.AddMinutes(1));

    private static PaymentDispute Dispute(Guid tenantId, Guid paymentId, string providerDisputeId) =>
        PaymentDispute.Observe(
            Guid.CreateVersion7(), tenantId, paymentId, providerDisputeId,
            PaymentDisputeStage.Formal, PaymentDisputeStatus.Open, 200, "EUR", UtcNow.AddMinutes(1));

    private static async Task SeedCapturedPaymentAsync(
        ExploreDbContext context,
        Guid tenantId,
        Guid paymentId,
        Guid orderId,
        Guid? eventId = null)
    {
        RegistrationOrder order = RegistrationOrder.Create(
            orderId, tenantId, eventId ?? Guid.CreateVersion7(), Guid.CreateVersion7(), null,
            BookingPartyTypeEnum.Individual, Guid.CreateVersion7(),
            RegistrationParticipationSnapshot.Create(
                Guid.CreateVersion7(), 1, 1, 1, GuestRecoveryPolicyEnum.VerifiedEmailRequired),
            null, null, "EUR", UtcNow, UtcNow.AddMinutes(30));
        OrganizerPaymentRecipientSnapshot recipient = OrganizerPaymentRecipientSnapshot.Create(
            tenantId, Guid.CreateVersion7(), Guid.CreateVersion7(), "stripe", "platform-live-eu", "acct_original",
            "BE", "EUR", Guid.CreateVersion7(), null, UtcNow);
        PaymentAttempt payment = PaymentAttempt.Create(
            paymentId, tenantId, orderId, recipient, "OrganizerDirect", "2026-08-20.acacia", "refund-fixture",
            Money.Create(1_000, recipient.CurrencyCode), Money.Create(75, recipient.CurrencyCode), Money.Create(0, recipient.CurrencyCode), $"payment:{tenantId:N}:{paymentId:N}", UtcNow, UtcNow.AddMinutes(30));
        long lastCursor = await context.PaymentAttempts
            .Where(value => value.TenantId == tenantId)
            .MaxAsync(value => (long?)value.CampaignCursor) ?? 0;
        payment.AssignCampaignCursor(lastCursor + 1);
        payment.AttachAcceptance(Acceptance(tenantId, paymentId, orderId, recipient));
        payment.MarkSucceeded(PaymentProviderId(paymentId), UtcNow.AddSeconds(1), "req_payment");
        context.RegistrationOrders.Add(order);
        context.PaymentAttempts.Add(payment);
        await context.SaveChangesAsync();
    }

    private static string PaymentProviderId(Guid paymentId) => $"pi_{paymentId:N}";

    private static PaidOrderAcceptanceSnapshot Acceptance(
        Guid tenantId,
        Guid paymentId,
        Guid orderId,
        OrganizerPaymentRecipientSnapshot? recipient = null) =>
        PaidOrderAcceptanceSnapshot.Create(
            paymentId, tenantId, tenantId, orderId, Guid.CreateVersion7(), "refund-fixture", "disclosure-1",
            PaidOrderAcceptanceSnapshot.CurrentAcceptanceTemplateIdentifier,
            PaidOrderAcceptanceSnapshot.CurrentAcceptanceTemplateText,
            recipient?.OrganizerActorId ?? Guid.CreateVersion7(),
            "Example Organizer",
            PaidCheckoutTenantDirectoryOperatorDisclosure.Create(
                Guid.CreateVersion7(), Guid.CreateVersion7(), "Community Events", "Community Events ASBL",
                "registered_organization", "BE", null, "contact@example.test", "https://example.test/legal",
                "https://example.test/terms", "https://example.test/privacy"),
            PaidCheckoutOperatorDisclosure.Create(
                Guid.CreateVersion7(), "Example Operator", false, "https://events.example.test", "BE",
                "https://events.example.test", "https://events.example.test/legal", "https://events.example.test/terms",
                "https://events.example.test/privacy", "complaints@example.test", "Trust and Safety", "Payments Operations",
                "Dispute Operations", "Payment Reconciliation", "approved"),
            PaidOrderDeliverySnapshot.Create(
                DateTimeOffset.Parse("2026-09-10T17:00:00Z"), DateTimeOffset.Parse("2026-09-10T20:00:00Z"), "Europe/Brussels"),
            "EUR", 1_000, 75, 0, 1_000,
            recipient?.InstancePolicyVersionId ?? Guid.CreateVersion7(), 7,
            "Refunds follow accepted policy v7.", "en-GB", "support@example.test",
            PaidCheckoutProviderDisclosure.Create(
                recipient?.ProviderCode ?? "stripe",
                recipient?.ProfileCode ?? "OrganizerDirect",
                "direct-charge", "EXAMPLE EVENT", "test", "instance-operator"),
            [PaidOrderAcceptanceLineFact.Create(orderId, "Admission", 1, 1_000, 0, 1_000)],
            UtcNow,
            tenantPolicyVersionId: recipient?.TenantPolicyVersionId,
            organizerPaymentProviderConnectionId:
                recipient?.OrganizerPaymentProviderConnectionId ?? Guid.CreateVersion7(),
            connectPlatformId: recipient?.ConnectPlatformId ?? "platform-live-eu",
            externalAccountId: recipient?.ExternalAccountId ?? "acct_original",
            merchantCountryCode: recipient?.MerchantCountryCode ?? "BE");

    private static DbContextOptions<ExploreDbContext> Options(SqliteConnection connection) =>
        TestDbContextOptions.Create<ExploreDbContext>()
            .UseSqlite(connection)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(
                SqliteNamedLockTransactionInterceptor.Instance,
                SqliteProjectionLockTransactionInterceptor.Instance)
            .Options;

    private static async Task<ExploreDbContext> CreateContextAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        return await CreateContextAsync(Options(connection), bypassTenantFilter: true);
    }

    private static async Task<ExploreDbContext> CreateContextAsync(
        DbContextOptions<ExploreDbContext> options,
        bool bypassTenantFilter)
    {
        var context = new ExploreDbContext(options);
        if (bypassTenantFilter)
        {
            context.EnableTenantFilterBypass("Phase 19 refund persistence test setup.");
        }
        await context.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF;");
        await context.Database.EnsureCreatedAsync();
        return context;
    }

    private sealed record TestTenantContext(Guid TenantId) : ITenantContext;
}
