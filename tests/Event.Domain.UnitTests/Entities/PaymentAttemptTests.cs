using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;

namespace Event.Domain.UnitTests.Entities;

public sealed class PaymentAttemptTests
{
    private static readonly Guid TenantId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000001");
    private static readonly Guid OrganizerActorId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000111");
    private static readonly Guid ConnectionId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000222");
    private static readonly Guid OrderId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000333");
    private static readonly DateTime Now = new(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task Create_PinsRecipientCompositionIdempotencyAndStartsCreated()
    {
        OrganizerPaymentRecipientSnapshot recipient = RecipientSnapshot();

        PaymentAttempt attempt = PaymentAttempt.Create(
            Guid.Parse("018e4e5c-7f00-7000-8000-000000000444"),
            TenantId,
            OrderId,
            recipient,
            "OrganizerDirect",
            "2026-08-20.acacia",
            "composition-a",
            Money.Create(1_000, "EUR"),
            Money.Create(75, "EUR"),
            Money.Create(125, "EUR"),
            "registration-order-claim-001",
            Now,
            Now.AddMinutes(30));

        await Assert.That(attempt.TenantId).IsEqualTo(TenantId);
        await Assert.That(attempt.RegistrationOrderId).IsEqualTo(OrderId);
        await Assert.That(attempt.RecipientSnapshot).IsEqualTo(recipient);
        await Assert.That(attempt.ProviderCode).IsEqualTo("stripe");
        await Assert.That(attempt.ProfileCode).IsEqualTo("OrganizerDirect");
        await Assert.That(attempt.ProviderApiRevision).IsEqualTo("2026-08-20.acacia");
        await Assert.That(attempt.CurrencyCode).IsEqualTo("EUR");
        await Assert.That(attempt.OrganizerAmountMinor).IsEqualTo(1_000);
        await Assert.That(attempt.PlatformFeeMinor).IsEqualTo(75);
        await Assert.That(attempt.PlatformContributionMinor).IsEqualTo(125);
        await Assert.That(attempt.TotalMinor).IsEqualTo(1_125);
        await Assert.That(attempt.ProviderIdempotencyKey).IsEqualTo("registration-order-claim-001");
        await Assert.That(attempt.CompositionRevision).IsEqualTo("composition-a");
        await Assert.That(attempt.ActiveScopeKey).IsEqualTo($"{TenantId:N}|{OrderId:N}");
        await Assert.That(attempt.PaymentAttemptStatusId).IsEqualTo((int)PaymentAttemptStatusEnum.Created);
    }

    [Test]
    public async Task Create_RejectsMalformedMoneyAndProviderFactsWithoutCreatingAttempt()
    {
        OrganizerPaymentRecipientSnapshot recipient = RecipientSnapshot();

        await Assert.That(() => PaymentAttempt.Create(Guid.CreateVersion7(), TenantId, OrderId, recipient, "OrganizerDirect", "rev", "composition-a", Money.Create(100, "EUR"), Money.Create(101, "EUR"), Money.Create(0, "EUR"), "claim", Now, null))
            .Throws<ArgumentException>();
        await Assert.That(() => PaymentAttempt.Create(Guid.CreateVersion7(), TenantId, OrderId, recipient, "OrganizerDirect", "rev", "composition-a", Money.Create(long.MaxValue, "EUR"), Money.Create(0, "EUR"), Money.Create(1, "EUR"), "claim", Now, null))
            .Throws<OverflowException>();
        await Assert.That(() => PaymentAttempt.Create(Guid.CreateVersion7(), TenantId, OrderId, recipient, "OrganizerDirect", "rev", "composition-a", Money.Create(100, "EUR"), Money.Create(0, "EUR"), Money.Create(0, "EUR"), " ", Now, null))
            .Throws<ArgumentException>();
        await Assert.That(() => PaymentAttempt.Create(Guid.CreateVersion7(), TenantId, OrderId, recipient, "OrganizerDirect", "rev", "composition-a", Money.Create(100, "EUR"), Money.Create(0, "EUR"), Money.Create(0, "EUR"), "claim", Now, Now))
            .Throws<ArgumentException>();
        await Assert.That(() => PaymentAttempt.Create(Guid.CreateVersion7(), TenantId, OrderId, recipient, "WrongProfile", "rev", "composition-a", Money.Create(100, "EUR"), Money.Create(0, "EUR"), Money.Create(0, "EUR"), "claim", Now, null))
            .Throws<ArgumentException>();
        await Assert.That(() => PaymentAttempt.Create(Guid.CreateVersion7(), TenantId, OrderId, recipient, "OrganizerDirect", "rev", "composition-a", Money.Create(100, "USD"), Money.Create(0, "EUR"), Money.Create(0, "EUR"), "claim", Now, null))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task StateTransitions_AreExplicitMonotonicAndKeepEvidence()
    {
        PaymentAttempt attempt = Attempt();

        bool dispatched = attempt.MarkDispatchPending(Now.AddSeconds(1), "req-create");
        bool requiresAction = attempt.MarkRequiresAction("cs_test_123", Now.AddSeconds(2), "req-checkout");
        bool staleProcessing = attempt.MarkProcessing("pi_test_123", Now.AddSeconds(1), "req-stale");
        bool processing = attempt.MarkProcessing("pi_test_123", Now.AddSeconds(3), "req-payment");
        bool succeeded = attempt.MarkSucceeded("pi_test_123", Now.AddSeconds(4), "req-final");

        await Assert.That(dispatched).IsTrue();
        await Assert.That(requiresAction).IsTrue();
        await Assert.That(staleProcessing).IsFalse();
        await Assert.That(attempt.OrganizerAmountMinor).IsEqualTo(1_000);
        await Assert.That(attempt.PlatformFeeMinor).IsEqualTo(75);
        await Assert.That(attempt.PlatformContributionMinor).IsEqualTo(125);
        await Assert.That(attempt.TotalMinor).IsEqualTo(1_125);
        await Assert.That(processing).IsTrue();
        await Assert.That(succeeded).IsTrue();
        await Assert.That(attempt.PaymentAttemptStatusId).IsEqualTo((int)PaymentAttemptStatusEnum.Succeeded);
        await Assert.That(attempt.ProviderCheckoutSessionId).IsEqualTo("cs_test_123");
        await Assert.That(attempt.ProviderPaymentId).IsEqualTo("pi_test_123");
        await Assert.That(attempt.LastProviderRequestId).IsEqualTo("req-final");
        await Assert.That(attempt.SucceededAt).IsEqualTo(Now.AddSeconds(4));
        await Assert.That(() => attempt.MarkFailed("pi_test_123", Now.AddSeconds(5), "req-fail"))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task MarkDispatchFailed_TerminatesBeforeProviderIdentifiersExist()
    {
        PaymentAttempt attempt = Attempt();

        bool failed = attempt.MarkDispatchFailed(Now.AddSeconds(1), "req_rejected");

        await Assert.That(failed).IsTrue();
        await Assert.That(attempt.PaymentAttemptStatusId).IsEqualTo((int)PaymentAttemptStatusEnum.Failed);
        await Assert.That(attempt.ProviderCheckoutSessionId).IsNull();
        await Assert.That(attempt.ProviderPaymentId).IsNull();
        await Assert.That(attempt.LastProviderRequestId).IsEqualTo("req_rejected");
    }

    [Test]
    public async Task StateTransitions_BindProviderIdentifiersOnceAndDoNotResetToUnknown()
    {
        PaymentAttempt attempt = Attempt();
        attempt.MarkRequiresAction("cs_test_123", Now.AddSeconds(1), "req-checkout");
        attempt.MarkProcessing("pi_test_123", Now.AddSeconds(2), "req-payment");

        bool duplicateObservation = attempt.MarkProcessing("pi_test_123", Now.AddSeconds(3), "req-payment-2");
        bool unknown = attempt.MarkUnknown(Now.AddSeconds(4), "req-unknown");

        await Assert.That(duplicateObservation).IsFalse();
        await Assert.That(unknown).IsTrue();
        await Assert.That(attempt.PaymentAttemptStatusId).IsEqualTo((int)PaymentAttemptStatusEnum.Unknown);
        await Assert.That(attempt.ProviderCheckoutSessionId).IsEqualTo("cs_test_123");
        await Assert.That(attempt.ProviderPaymentId).IsEqualTo("pi_test_123");
        await Assert.That(() => attempt.MarkRequiresAction("cs_other", Now.AddSeconds(5), "req-other"))
            .Throws<InvalidOperationException>();
        await Assert.That(() => attempt.MarkProcessing("pi_other", Now.AddSeconds(5), "req-other"))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task StateTransitions_RecoverCreatedUnknownToRequiresActionAndBindCheckoutId()
    {
        PaymentAttempt attempt = Attempt();

        bool unknown = attempt.MarkUnknown(Now.AddSeconds(1), "req-timeout");
        bool recovered = attempt.MarkRequiresAction("cs_recovered", Now.AddSeconds(2), "req-checkout");

        await Assert.That(unknown).IsTrue();
        await Assert.That(recovered).IsTrue();
        await Assert.That(attempt.PaymentAttemptStatusId).IsEqualTo((int)PaymentAttemptStatusEnum.RequiresAction);
        await Assert.That(attempt.ProviderCheckoutSessionId).IsEqualTo("cs_recovered");
        await Assert.That(attempt.UnknownAt).IsEqualTo(Now.AddSeconds(1));
    }

    [Test]
    public async Task StateTransitions_RecoverDispatchPendingUnknownToProcessingAndBindCheckoutAndPaymentIds()
    {
        PaymentAttempt attempt = Attempt();
        attempt.MarkDispatchPending(Now.AddSeconds(1), "req-dispatch");
        attempt.MarkUnknown(Now.AddSeconds(2), "req-timeout");

        bool recovered = attempt.MarkProcessing("cs_recovered", "pi_recovered", Now.AddSeconds(3), "req-payment");

        await Assert.That(recovered).IsTrue();
        await Assert.That(attempt.PaymentAttemptStatusId).IsEqualTo((int)PaymentAttemptStatusEnum.Processing);
        await Assert.That(attempt.ProviderCheckoutSessionId).IsEqualTo("cs_recovered");
        await Assert.That(attempt.ProviderPaymentId).IsEqualTo("pi_recovered");
    }

    [Test]
    public async Task StateTransitions_RecoverUnknownToCancelled()
    {
        PaymentAttempt attempt = Attempt();
        attempt.MarkUnknown(Now.AddSeconds(1), "req-timeout");

        bool cancelled = attempt.MarkCancelled(Now.AddSeconds(2), "req-cancelled");

        await Assert.That(cancelled).IsTrue();
        await Assert.That(attempt.PaymentAttemptStatusId).IsEqualTo((int)PaymentAttemptStatusEnum.Cancelled);
        await Assert.That(attempt.CancelledAt).IsEqualTo(Now.AddSeconds(2));
    }

    [Test]
    public async Task StateTransitions_RecoverUnknownToSucceeded()
    {
        PaymentAttempt attempt = Attempt();
        attempt.MarkUnknown(Now.AddSeconds(1), "req-timeout");

        bool succeeded = attempt.MarkSucceeded("pi_recovered", Now.AddSeconds(2), "req-succeeded");

        await Assert.That(succeeded).IsTrue();
        await Assert.That(attempt.PaymentAttemptStatusId).IsEqualTo((int)PaymentAttemptStatusEnum.Succeeded);
        await Assert.That(attempt.ProviderPaymentId).IsEqualTo("pi_recovered");
        await Assert.That(attempt.SucceededAt).IsEqualTo(Now.AddSeconds(2));
    }

    [Test]
    public async Task StateTransitions_TerminalDuplicateObservationsAreIdempotent()
    {
        PaymentAttempt attempt = Attempt();
        attempt.MarkSucceeded("pi_final", Now.AddSeconds(1), "req-final");

        bool duplicate = attempt.MarkSucceeded("pi_final", Now.AddSeconds(2), "req-duplicate");

        await Assert.That(duplicate).IsFalse();
        await Assert.That(attempt.PaymentAttemptStatusId).IsEqualTo((int)PaymentAttemptStatusEnum.Succeeded);
        await Assert.That(attempt.LastProviderRequestId).IsEqualTo("req-final");
    }

    [Test]
    public async Task StateTransitions_StaleTerminalProviderTruthIsIgnored()
    {
        PaymentAttempt attempt = Attempt();
        attempt.MarkProcessing("pi_final", Now.AddSeconds(2), "req-processing");

        bool staleSucceeded = attempt.MarkSucceeded("pi_final", Now.AddSeconds(1), "req-stale-success");

        await Assert.That(staleSucceeded).IsFalse();
        await Assert.That(attempt.PaymentAttemptStatusId).IsEqualTo((int)PaymentAttemptStatusEnum.Processing);
        await Assert.That(attempt.LastProviderRequestId).IsEqualTo("req-processing");
        await Assert.That(attempt.SucceededAt).IsNull();
    }

    [Test]
    public async Task StateTransitions_ConflictingIdsRemainRejectedAfterAmbiguity()
    {
        PaymentAttempt attempt = Attempt();
        attempt.MarkRequiresAction("cs_original", Now.AddSeconds(1), "req-checkout");
        attempt.MarkUnknown(Now.AddSeconds(2), "req-timeout");
        attempt.MarkProcessing("pi_original", Now.AddSeconds(3), "req-payment");

        await Assert.That(() => attempt.MarkProcessing("cs_other", "pi_original", Now.AddSeconds(4), "req-conflict-checkout"))
            .Throws<InvalidOperationException>();
        await Assert.That(() => attempt.MarkSucceeded("pi_other", Now.AddSeconds(4), "req-conflict-payment"))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task StateTransitions_ProcessingUnknownCannotRecoverToRequiresAction()
    {
        PaymentAttempt attempt = Attempt();
        attempt.MarkProcessing("pi_processing", Now.AddSeconds(1), "req-processing");
        attempt.MarkUnknown(Now.AddSeconds(2), "req-timeout");

        bool recovered = attempt.MarkRequiresAction("cs_late", Now.AddSeconds(3), "req-late-checkout");

        await Assert.That(recovered).IsFalse();
        await Assert.That(attempt.PaymentAttemptStatusId).IsEqualTo((int)PaymentAttemptStatusEnum.Unknown);
        await Assert.That(attempt.ProviderCheckoutSessionId).IsNull();
        await Assert.That(attempt.AuthoritativeStatusFloorId).IsEqualTo((int)PaymentAttemptStatusEnum.Processing);
    }

    [Test]
    public async Task StateTransitions_RequiresActionUnknownCannotRecoverToDispatchPending()
    {
        PaymentAttempt attempt = Attempt();
        attempt.MarkRequiresAction("cs_requires_action", Now.AddSeconds(1), "req-checkout");
        attempt.MarkUnknown(Now.AddSeconds(2), "req-timeout");

        bool recovered = attempt.MarkDispatchPending(Now.AddSeconds(3), "req-dispatch");

        await Assert.That(recovered).IsFalse();
        await Assert.That(attempt.PaymentAttemptStatusId).IsEqualTo((int)PaymentAttemptStatusEnum.Unknown);
        await Assert.That(attempt.AuthoritativeStatusFloorId).IsEqualTo((int)PaymentAttemptStatusEnum.RequiresAction);
    }

    [Test]
    public async Task StateTransitions_RecoverUnknownToFailedWithCheckoutAndPaymentIds()
    {
        PaymentAttempt attempt = Attempt();
        attempt.MarkDispatchPending(Now.AddSeconds(1), "req-dispatch");
        attempt.MarkUnknown(Now.AddSeconds(2), "req-timeout");

        bool failed = attempt.MarkFailedFromCheckout("cs_failed", "pi_failed", Now.AddSeconds(3), "req-failed");

        await Assert.That(failed).IsTrue();
        await Assert.That(attempt.PaymentAttemptStatusId).IsEqualTo((int)PaymentAttemptStatusEnum.Failed);
        await Assert.That(attempt.ProviderCheckoutSessionId).IsEqualTo("cs_failed");
        await Assert.That(attempt.ProviderPaymentId).IsEqualTo("pi_failed");
        await Assert.That(attempt.AuthoritativeStatusFloorId).IsEqualTo((int)PaymentAttemptStatusEnum.Failed);
    }

    [Test]
    public async Task StateTransitions_RecoverUnknownToSucceededWithCheckoutAndPaymentIds()
    {
        PaymentAttempt attempt = Attempt();
        attempt.MarkUnknown(Now.AddSeconds(1), "req-timeout");

        bool succeeded = attempt.MarkSucceededFromCheckout("cs_succeeded", "pi_succeeded", Now.AddSeconds(2), "req-succeeded");

        await Assert.That(succeeded).IsTrue();
        await Assert.That(attempt.PaymentAttemptStatusId).IsEqualTo((int)PaymentAttemptStatusEnum.Succeeded);
        await Assert.That(attempt.ProviderCheckoutSessionId).IsEqualTo("cs_succeeded");
        await Assert.That(attempt.ProviderPaymentId).IsEqualTo("pi_succeeded");
    }

    [Test]
    public async Task StateTransitions_RecoverUnknownToCancelledWithCheckoutId()
    {
        PaymentAttempt attempt = Attempt();
        attempt.MarkUnknown(Now.AddSeconds(1), "req-timeout");

        bool cancelled = attempt.MarkCancelledFromCheckout("cs_cancelled", Now.AddSeconds(2), "req-cancelled");

        await Assert.That(cancelled).IsTrue();
        await Assert.That(attempt.PaymentAttemptStatusId).IsEqualTo((int)PaymentAttemptStatusEnum.Cancelled);
        await Assert.That(attempt.ProviderCheckoutSessionId).IsEqualTo("cs_cancelled");
    }

    [Test]
    public async Task StateTransitions_StaleConflictingIdsAfterUnknownAreIgnoredWithoutMutation()
    {
        PaymentAttempt attempt = Attempt();
        attempt.MarkRequiresAction("cs_original", Now.AddSeconds(2), "req-checkout");
        attempt.MarkUnknown(Now.AddSeconds(3), "req-timeout");

        bool stale = attempt.MarkProcessing("cs_conflict", "pi_conflict", Now.AddSeconds(1), "req-stale-conflict");

        await Assert.That(stale).IsFalse();
        await Assert.That(attempt.PaymentAttemptStatusId).IsEqualTo((int)PaymentAttemptStatusEnum.Unknown);
        await Assert.That(attempt.ProviderCheckoutSessionId).IsEqualTo("cs_original");
        await Assert.That(attempt.ProviderPaymentId).IsNull();
        await Assert.That(attempt.LastProviderRequestId).IsEqualTo("req-timeout");
    }

    private static PaymentAttempt Attempt() => PaymentAttempt.Create(
        Guid.CreateVersion7(),
        TenantId,
        OrderId,
        RecipientSnapshot(),
        "OrganizerDirect",
        "2026-08-20.acacia",
        "composition-a",
        Money.Create(1_000, "EUR"),
        Money.Create(75, "EUR"),
        Money.Create(125, "EUR"),
        "registration-order-claim-001",
        Now,
        Now.AddMinutes(30));

    private static OrganizerPaymentRecipientSnapshot RecipientSnapshot() => OrganizerPaymentRecipientSnapshot.Create(
        TenantId,
        OrganizerActorId,
        ConnectionId,
        "stripe",
        "platform-live-eu",
        "acct_123",
        "BE",
        "EUR",
        Guid.Parse("018e4e5c-7f00-7000-8000-000000000555"),
        Guid.Parse("018e4e5c-7f00-7000-8000-000000000556"),
        Now);
}
