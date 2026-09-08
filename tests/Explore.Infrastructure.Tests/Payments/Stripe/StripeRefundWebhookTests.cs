using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Payments;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Contracts.Webhooks;
using Explore.Domain;
using Explore.Domain.ValueObjects;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Infrastructure.Configuration;
using Explore.Infrastructure.Payments.Stripe;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Payments.Stripe;

public sealed class StripeRefundWebhookTests
{
    private static readonly string Secret = $"whsec_{Convert.ToHexString(RandomNumberGenerator.GetBytes(24))}";
    private const string AccountId = "acct_original";
    private static readonly DateTime CreatedAt = new(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid TenantId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000001");
    private static readonly Guid RefundAttemptId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000002");

    [Test]
    public async Task VerifyAsync_SignedRefundRetainsOnlyBoundedAttemptEvidence()
    {
        string payload = RefundPayload("evt_refund", "refund.updated", "re_123", "succeeded", "buyer@example.test");

        IncomingWebhookVerificationResult result = await Verifier().VerifyAsync(Context(payload), CancellationToken.None);

        string retained = Encoding.UTF8.GetString(result.RetainedPayloadBytes.Span);
        await Assert.That(result.IsVerified).IsTrue();
        await Assert.That(result.IdempotencyKey).IsEqualTo("evt_refund");
        await Assert.That(retained).Contains(RefundAttemptId.ToString("D"));
        await Assert.That(retained).Contains("re_123");
        await Assert.That(retained).DoesNotContain("buyer@example.test");
        await Assert.That(retained).DoesNotContain("description");
    }

    [Test]
    public async Task RefundHandler_SuccessWithoutFeeProofPinsIdentityForReconciliation()
    {
        RefundAttempt attempt = RefundAttempt.Create(
            RefundAttemptId, TenantId, Guid.CreateVersion7(), Acceptance(), AccountId,
            "pi_original", "refund:stable", 500, CreatedAt.AddMinutes(-1));
        var repository = Substitute.For<IRefundAttemptRepository>();
        repository.GetByIdAsync(TenantId, RefundAttemptId, Arg.Any<CancellationToken>()).Returns(attempt);
        var handler = new StripeRefundIncomingWebhookHandler(repository);
        IncomingWebhookProcessingContext context = ProcessingContext(
            "evt_refund", "refund.updated", new
            {
                EventId = "evt_refund",
                EventType = "refund.updated",
                RefundAttemptId,
                ProviderRefundId = "re_123",
                ProviderPaymentId = "pi_original",
                AccountId,
                AmountMinor = 500,
                CurrencyCode = "EUR",
                Status = RefundProviderStatus.Succeeded,
                CreatedAt
            });

        IncomingWebhookProcessingResult first = await handler.HandleAsync(context, CancellationToken.None);
        IncomingWebhookProcessingResult duplicate = await handler.HandleAsync(context, CancellationToken.None);

        await Assert.That(first.Outcome).IsEqualTo(IncomingWebhookProcessingOutcome.Processed)
            .Because(first.FailureCategory ?? "unexpected refund webhook outcome");
        await Assert.That(duplicate.Outcome).IsEqualTo(IncomingWebhookProcessingOutcome.Processed);
        await Assert.That(attempt.Status).IsEqualTo(RefundAttemptStatusEnum.Unknown);
        await Assert.That(attempt.ProviderRefundId).IsEqualTo("re_123");
        await repository.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RefundHandler_LaterBuyerSuccessPreservesExistingFeeSettlementBlock()
    {
        RefundAttempt attempt = RefundAttempt.Create(
            RefundAttemptId, TenantId, Guid.CreateVersion7(), Acceptance(), AccountId,
            "pi_original", "refund:fee-blocked", 500, CreatedAt.AddMinutes(-2));
        attempt.MarkDispatchPending(CreatedAt.AddMinutes(-1), null);
        attempt.MarkProviderBlocked(CreatedAt.AddSeconds(-1), "req_fee", "refund_provider_fee_rejected");
        var repository = Substitute.For<IRefundAttemptRepository>();
        repository.GetByIdAsync(TenantId, RefundAttemptId, Arg.Any<CancellationToken>()).Returns(attempt);
        var handler = new StripeRefundIncomingWebhookHandler(repository);

        IncomingWebhookProcessingResult result = await handler.HandleAsync(
            ProcessingContext(
                "evt_fee_blocked_success",
                "refund.updated",
                new
                {
                    EventId = "evt_fee_blocked_success",
                    EventType = "refund.updated",
                    RefundAttemptId,
                    ProviderRefundId = "re_123",
                    ProviderPaymentId = "pi_original",
                    AccountId,
                    AmountMinor = 500,
                    CurrencyCode = "EUR",
                    Status = RefundProviderStatus.Succeeded,
                    CreatedAt
                }),
            CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(IncomingWebhookProcessingOutcome.Processed);
        await Assert.That(attempt.BuyerRefundSucceededAt).IsEqualTo(CreatedAt);
        await Assert.That(attempt.Status).IsEqualTo(RefundAttemptStatusEnum.RequiresAction);
        await Assert.That(attempt.FailureCode).IsEqualTo("refund_provider_fee_rejected");
        await repository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RefundHandler_RejectsSameAccountEvidenceForAnotherPayment()
    {
        RefundAttempt attempt = RefundAttempt.Create(
            RefundAttemptId, TenantId, Guid.CreateVersion7(), Acceptance(), AccountId,
            "pi_original", "refund:stable", 500, CreatedAt.AddMinutes(-1));
        var repository = Substitute.For<IRefundAttemptRepository>();
        repository.GetByIdAsync(TenantId, RefundAttemptId, Arg.Any<CancellationToken>()).Returns(attempt);
        var handler = new StripeRefundIncomingWebhookHandler(repository);
        IncomingWebhookProcessingContext context = ProcessingContext(
            "evt_wrong_payment", "refund.failed", new
            {
                EventId = "evt_wrong_payment",
                EventType = "refund.failed",
                RefundAttemptId,
                ProviderRefundId = "re_wrong",
                ProviderPaymentId = "pi_other",
                AccountId,
                AmountMinor = 500,
                CurrencyCode = "EUR",
                Status = RefundProviderStatus.Failed,
                CreatedAt
            });

        IncomingWebhookProcessingResult result = await handler.HandleAsync(context, CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(IncomingWebhookProcessingOutcome.RejectedPermanent);
        await Assert.That(attempt.Status).IsEqualTo(RefundAttemptStatusEnum.Requested);
        await Assert.That(attempt.ReservesCapacity).IsTrue();
        await repository.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    [Arguments("warning_needs_response", PaymentDisputeStatus.Open)]
    [Arguments("needs_response", PaymentDisputeStatus.Open)]
    [Arguments("won", PaymentDisputeStatus.Won)]
    [Arguments("lost", PaymentDisputeStatus.Lost)]
    [Arguments("prevented", PaymentDisputeStatus.Prevented)]
    public async Task VerifyAsync_SignedDisputeRetainsProviderPaymentAndMonotonicEvidence(
        string providerStatus,
        PaymentDisputeStatus expectedStatus)
    {
        string payload = DisputePayload("evt_dispute", "charge.dispute.updated", "dp_123", providerStatus);

        IncomingWebhookVerificationResult result = await Verifier().VerifyAsync(Context(payload), CancellationToken.None);

        string retained = Encoding.UTF8.GetString(result.RetainedPayloadBytes.Span);
        await Assert.That(result.IsVerified).IsTrue();
        await Assert.That(result.IdempotencyKey).IsEqualTo("evt_dispute");
        await Assert.That(retained).Contains("dp_123");
        await Assert.That(retained).Contains("pi_original");
        await Assert.That(retained).Contains(expectedStatus.ToString());
        await Assert.That(retained).Contains(providerStatus.StartsWith("warning_", StringComparison.Ordinal)
            ? PaymentDisputeStage.Inquiry.ToString()
            : PaymentDisputeStage.Formal.ToString());
    }

    [Test]
    public async Task DisputeHandler_ObservesMultipleDisputesAndLateWinWithoutProviderIo()
    {
        PaymentAttempt payment = Payment();
        var repository = Substitute.For<IRefundAttemptRepository>();
        repository.FindPaymentByProviderPaymentAsync(TenantId, AccountId, "pi_original", Arg.Any<CancellationToken>())
            .Returns(payment);
        repository.ObserveDisputeAsync(Arg.Any<PaymentDispute>(), Arg.Any<CancellationToken>())
            .Returns(call => (PaymentDispute?)call.Arg<PaymentDispute>());
        var handler = new StripeDisputeIncomingWebhookHandler(repository);

        _ = await handler.HandleAsync(DisputeContext("evt_1", "dp_1", PaymentDisputeStatus.Open, CreatedAt), CancellationToken.None);
        _ = await handler.HandleAsync(DisputeContext("evt_2", "dp_2", PaymentDisputeStatus.Open, CreatedAt), CancellationToken.None);
        _ = await handler.HandleAsync(DisputeContext("evt_3", "dp_1", PaymentDisputeStatus.Won, CreatedAt.AddMinutes(1)), CancellationToken.None);

        await repository.Received(3).ObserveDisputeAsync(Arg.Any<PaymentDispute>(), Arg.Any<CancellationToken>());
        await repository.Received(1).ObserveDisputeAsync(
            Arg.Is<PaymentDispute>(dispute => dispute != null &&
                dispute.ProviderDisputeId == "dp_1" && dispute.Status == PaymentDisputeStatus.Won),
            Arg.Any<CancellationToken>());
    }

    private static StripeConnectIncomingWebhookVerifier Verifier()
    {
        OrganizerPaymentProviderConnection connection = OrganizerPaymentProviderConnection.Create(
            Guid.CreateVersion7(), TenantId, Guid.CreateVersion7(), "stripe", "platform-test", AccountId, CreatedAt);
        var connections = Substitute.For<IOrganizerPaymentProviderConnectionRepository>();
        connections.ListHistoricalByExternalAccountAsync("stripe", AccountId, 2, Arg.Any<CancellationToken>())
            .Returns([connection]);
        var secrets = Substitute.For<ISecretResolver>();
        secrets.ResolveAsync(SecretDefinitionRegistry.Keys.Stripe.WebhookSecret, null, Arg.Any<CancellationToken>()).Returns(SecretResolutionResult.Resolved(new ResolvedSecret(SecretDefinitionRegistry.Keys.Stripe.WebhookSecret, Secret, SecretSourceType.EnvironmentVariable,
        SecretScope.Instance, null, DateTimeOffset.UtcNow)));
        return new(
            new StaticOptionsMonitor<WebhookOptions>(new WebhookOptions()),
            Options.Create(new StripePaymentOptions()),
            secrets,
            connections,
            NullLogger<StripeConnectIncomingWebhookVerifier>.Instance);
    }

    private static IncomingWebhookContext Context(string payload) => new(
        "stripe-connect",
        payload,
        Encoding.UTF8.GetBytes(payload),
        new Dictionary<string, string>
        {
            ["Stripe-Signature"] = global::Stripe.EventUtility.GenerateSignatureHeader(payload, Secret)
        },
        DateTimeOffset.UtcNow);

    private static string RefundPayload(string eventId, string eventType, string refundId, string status, string description) =>
        JsonSerializer.Serialize(new
        {
            id = eventId,
            @object = "event",
            account = AccountId,
            api_version = global::Stripe.StripeConfiguration.ApiVersion,
            created = new DateTimeOffset(CreatedAt).ToUnixTimeSeconds(),
            livemode = false,
            type = eventType,
            data = new
            {
                @object = new
                {
                    id = refundId,
                    @object = "refund",
                    amount = 500,
                    currency = "eur",
                    payment_intent = "pi_original",
                    status,
                    description,
                    metadata = new Dictionary<string, string> { ["islamu_refund_attempt_id"] = RefundAttemptId.ToString("D") }
                }
            }
        });

    private static string DisputePayload(string eventId, string eventType, string disputeId, string status) =>
        JsonSerializer.Serialize(new
        {
            id = eventId,
            @object = "event",
            account = AccountId,
            api_version = global::Stripe.StripeConfiguration.ApiVersion,
            created = new DateTimeOffset(CreatedAt).ToUnixTimeSeconds(),
            livemode = false,
            type = eventType,
            data = new
            {
                @object = new
                {
                    id = disputeId,
                    @object = "dispute",
                    amount = 500,
                    currency = "eur",
                    payment_intent = "pi_original",
                    status
                }
            }
        });

    private static IncomingWebhookProcessingContext DisputeContext(
        string eventId,
        string disputeId,
        PaymentDisputeStatus status,
        DateTime createdAt) => ProcessingContext(eventId, "charge.dispute.updated", new
        {
            EventId = eventId,
            EventType = "charge.dispute.updated",
            ProviderDisputeId = disputeId,
            ProviderPaymentId = "pi_original",
            AccountId,
            AmountMinor = 500,
            CurrencyCode = "EUR",
            Stage = PaymentDisputeStage.Formal,
            Status = status,
            CreatedAt = createdAt
        });

    private static IncomingWebhookProcessingContext ProcessingContext(string eventId, string eventType, object envelope)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(envelope);
        IncomingWebhookMessage message = IncomingWebhookMessage.CreateVerified(
            TenantId, "stripe-connect", eventId, eventId, eventType, payload,
            "sha256:" + new string('a', 64), "application/json", "utf-8", null,
            CreatedAt, CreatedAt.AddSeconds(1), CreatedAt.AddDays(14), "test-v1",
            CreatedAt.AddDays(30), CreatedAt.AddDays(90), CreatedAt.AddDays(14), CreatedAt.AddDays(30),
            payloadProvenance: WebhookPayloadProvenance.NormalizedProviderEnvelope);
        Guid leaseToken = Guid.CreateVersion7();
        message.Claim("test", leaseToken, CreatedAt.AddMinutes(2), CreatedAt.AddSeconds(2));
        return IncomingWebhookProcessingContext.FromClaimedMessage(
            message, leaseToken, message.ProcessingFence, message.ProcessingGeneration, CreatedAt.AddSeconds(3));
    }

    private static PaymentAttempt Payment()
    {
        OrganizerPaymentRecipientSnapshot recipient = OrganizerPaymentRecipientSnapshot.Create(
            TenantId, Guid.CreateVersion7(), Guid.CreateVersion7(), "stripe", "platform-test", AccountId,
            "BE", "EUR", Guid.CreateVersion7(), null, CreatedAt.AddMinutes(-2));
        PaymentAttempt payment = PaymentAttempt.Create(
            Guid.CreateVersion7(), TenantId, Guid.CreateVersion7(), recipient, "OrganizerDirect",
            global::Stripe.StripeConfiguration.ApiVersion, "refund-webhook", Money.Create(1_000, recipient.CurrencyCode), Money.Create(75, recipient.CurrencyCode), Money.Create(0, recipient.CurrencyCode),
            "payment:stable", CreatedAt.AddMinutes(-2), CreatedAt.AddMinutes(30));
        payment.MarkSucceeded("pi_original", CreatedAt.AddMinutes(-1), "req_payment");
        return payment;
    }

    private static PaidOrderAcceptanceSnapshot Acceptance() => PaidOrderAcceptanceSnapshot.Create(
        Guid.CreateVersion7(), TenantId, TenantId, Guid.CreateVersion7(), Guid.CreateVersion7(),
        "composition-1", "disclosure-1",
        PaidOrderAcceptanceSnapshot.CurrentAcceptanceTemplateIdentifier,
        PaidOrderAcceptanceSnapshot.CurrentAcceptanceTemplateText,
        Guid.CreateVersion7(),
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
        "EUR", 1_000, 75, 0, 1_000, Guid.CreateVersion7(), 7,
        "Refunds follow accepted policy v7.", "en-GB", "support@example.test",
        PaidCheckoutProviderDisclosure.Create(
            "stripe", "OrganizerDirect", "direct-charge", "EXAMPLE EVENT", "test", "instance-operator"),
        [PaidOrderAcceptanceLineFact.Create(Guid.CreateVersion7(), "Admission", 1, 1_000, 0, 1_000)],
        CreatedAt.AddMinutes(-3),
        organizerPaymentProviderConnectionId: Guid.CreateVersion7(),
        connectPlatformId: "platform-test",
        externalAccountId: AccountId,
        merchantCountryCode: "BE");

    private sealed class StaticOptionsMonitor<T>(T currentValue) : IOptionsMonitor<T>
    {
        public T CurrentValue => currentValue;
        public T Get(string? name) => currentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
