using System.Text;
using System.Text.Json;
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

public sealed class StripePaymentWebhookVerifierTests
{
    private const string Secret = "whsec_payment_test";
    private const string AccountId = "acct_payment_123";
    private static readonly Guid TenantId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000001");
    private static readonly DateTime CreatedAt = new(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    [Arguments("checkout.session.completed")]
    [Arguments("checkout.session.async_payment_succeeded")]
    [Arguments("checkout.session.async_payment_failed")]
    [Arguments("checkout.session.expired")]
    public async Task VerifyAsync_SignedAllowlistedPaymentEvent_RetainsOnlyNormalizedEnvelope(string eventType)
    {
        StripeConnectIncomingWebhookVerifier verifier = Verifier();
        string payload = PaymentPayload("evt_payment_1", eventType, "cs_payment_1", AccountId, buyerEmail: "buyer@example.test");

        IncomingWebhookVerificationResult result = await verifier.VerifyAsync(Context(payload), CancellationToken.None);

        await Assert.That(result.IsVerified).IsTrue();
        await Assert.That(result.TenantId).IsEqualTo(TenantId);
        await Assert.That(result.ProviderMessageId).IsEqualTo("evt_payment_1");
        await Assert.That(result.EventType).IsEqualTo(eventType);
        await Assert.That(result.IdempotencyKey).IsEqualTo($"{eventType}:cs_payment_1");
        string retained = Encoding.UTF8.GetString(result.RetainedPayloadBytes.Span);
        await Assert.That(retained).Contains("evt_payment_1");
        await Assert.That(retained).Contains("cs_payment_1");
        await Assert.That(retained).Contains(AccountId);
        await Assert.That(retained).DoesNotContain("buyer@example.test");
        await Assert.That(retained).DoesNotContain("customer_details");
    }

    [Test]
    public async Task VerifyAsync_ModifiedBodyWithOriginalSignature_RejectsExactRawBodyMismatch()
    {
        StripeConnectIncomingWebhookVerifier verifier = Verifier();
        string signedPayload = PaymentPayload("evt_exact", "checkout.session.completed", "cs_exact", AccountId);
        string modifiedPayload = signedPayload.Replace("cs_exact", "cs_changed", StringComparison.Ordinal);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Stripe-Signature"] = global::Stripe.EventUtility.GenerateSignatureHeader(signedPayload, Secret)
        };

        IncomingWebhookVerificationResult result = await verifier.VerifyAsync(
            new IncomingWebhookContext("stripe-connect", modifiedPayload, Encoding.UTF8.GetBytes(modifiedPayload), headers, DateTimeOffset.UtcNow),
            CancellationToken.None);

        await Assert.That(result.IsVerified).IsFalse();
        await Assert.That(result.FailureCategory).IsEqualTo("stripe_connect_signature_invalid");
    }

    [Test]
    public async Task VerifyAsync_StaleSignature_RejectsDefaultToleranceReplay()
    {
        StripeConnectIncomingWebhookVerifier verifier = Verifier();
        string payload = PaymentPayload("evt_stale", "checkout.session.completed", "cs_stale", AccountId);
        long staleTimestamp = DateTimeOffset.UtcNow.AddMinutes(-6).ToUnixTimeSeconds();
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Stripe-Signature"] = global::Stripe.EventUtility.GenerateSignatureHeader(payload, Secret, staleTimestamp)
        };

        IncomingWebhookVerificationResult result = await verifier.VerifyAsync(
            new IncomingWebhookContext("stripe-connect", payload, Encoding.UTF8.GetBytes(payload), headers, DateTimeOffset.UtcNow),
            CancellationToken.None);

        await Assert.That(result.IsVerified).IsFalse();
        await Assert.That(result.FailureCategory).IsEqualTo("stripe_connect_signature_invalid");
    }

    [Test]
    [Arguments("payment_intent.succeeded", "stripe_connect_event_unsupported")]
    [Arguments("checkout.session.completed", "stripe_connect_object_id_invalid")]
    public async Task VerifyAsync_UnsupportedTypeOrMissingObjectId_Rejects(string eventType, string failureCode)
    {
        StripeConnectIncomingWebhookVerifier verifier = Verifier();
        string payload = PaymentPayload("evt_bad", eventType, null, AccountId);

        IncomingWebhookVerificationResult result = await verifier.VerifyAsync(Context(payload), CancellationToken.None);

        await Assert.That(result.IsVerified).IsFalse();
        await Assert.That(result.FailureCategory).IsEqualTo(failureCode);
    }

    [Test]
    public async Task VerifyAsync_WrongModeOrAccount_RejectsWithoutRetainedPayload()
    {
        StripeConnectIncomingWebhookVerifier verifier = Verifier();
        string livePayload = PaymentPayload("evt_live", "checkout.session.completed", "cs_live", AccountId, livemode: true);

        IncomingWebhookVerificationResult result = await verifier.VerifyAsync(Context(livePayload), CancellationToken.None);

        await Assert.That(result.IsVerified).IsFalse();
        await Assert.That(result.FailureCategory).IsEqualTo("stripe_connect_event_mode_mismatch");
        await Assert.That(result.RetainedPayloadBytes.IsEmpty).IsTrue();
    }

    [Test]
    public async Task PaymentHandler_VerifiedEnvelopeSchedulesOnlyDurableReconciliation()
    {
        PaymentAttempt attempt = PaymentAttemptForWebhook();
        var repository = Substitute.For<IRegistrationPaymentAttemptRepository>();
        repository.FindByProviderObjectAsync(TenantId, AccountId, "cs_payment_1", Arg.Any<CancellationToken>())
            .Returns(attempt);
        var handler = new StripePaymentIncomingWebhookHandler(repository);
        IncomingWebhookProcessingContext context = PaymentProcessingContext(
            "evt_handler",
            "checkout.session.completed",
            "cs_payment_1",
            AccountId);

        IncomingWebhookProcessingResult result = await handler.HandleAsync(context, CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(IncomingWebhookProcessingOutcome.Processed);
        await repository.Received(1).EnsureReconciliationDueAsync(
            attempt,
            context.IncomingWebhookMessageId,
            CreatedAt,
            Arg.Any<CancellationToken>());
        await repository.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Test]
    public async Task PaymentHandler_UnknownObjectParksWithoutTenantOrProviderMutation()
    {
        var repository = Substitute.For<IRegistrationPaymentAttemptRepository>();
        var handler = new StripePaymentIncomingWebhookHandler(repository);
        IncomingWebhookProcessingContext context = PaymentProcessingContext(
            "evt_orphan",
            "checkout.session.completed",
            "cs_orphan",
            AccountId);

        IncomingWebhookProcessingResult result = await handler.HandleAsync(context, CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(IncomingWebhookProcessingOutcome.RejectedPermanent);
        await Assert.That(result.FailureCategory).IsEqualTo("stripe_payment_attempt_orphaned");
        await repository.DidNotReceiveWithAnyArgs().EnsureReconciliationDueAsync(default!, default, default, default);
    }

    [Test]
    public async Task VerifyAsync_ApiRevisionMismatchRejectsSignedEvent()
    {
        StripeConnectIncomingWebhookVerifier verifier = Verifier();
        string payload = PaymentPayload("evt_revision", "checkout.session.completed", "cs_revision", AccountId)
            .Replace(global::Stripe.StripeConfiguration.ApiVersion, "2000-01-01", StringComparison.Ordinal);

        IncomingWebhookVerificationResult result = await verifier.VerifyAsync(Context(payload), CancellationToken.None);

        await Assert.That(result.IsVerified).IsFalse();
        await Assert.That(result.FailureCategory).IsEqualTo("stripe_connect_signature_invalid");
    }

    [Test]
    public async Task VerifyAsync_UnknownConnectedAccountRejectsWithoutTenantDisclosure()
    {
        StripeConnectIncomingWebhookVerifier verifier = Verifier(hasConnection: false);
        string payload = PaymentPayload("evt_unknown_account", "checkout.session.completed", "cs_unknown", AccountId);

        IncomingWebhookVerificationResult result = await verifier.VerifyAsync(Context(payload), CancellationToken.None);

        await Assert.That(result.IsVerified).IsFalse();
        await Assert.That(result.TenantId).IsNull();
        await Assert.That(result.SafeDetail ?? string.Empty).DoesNotContain(AccountId);
        await Assert.That(result.FailureCategory).IsEqualTo("stripe_connect_account_not_unique");
    }

    private static StripeConnectIncomingWebhookVerifier Verifier(bool hasConnection = true)
    {
        var connection = OrganizerPaymentProviderConnection.Create(
            Guid.CreateVersion7(), TenantId, Guid.CreateVersion7(), "stripe", "platform-test", AccountId, CreatedAt);
        var connections = Substitute.For<IOrganizerPaymentProviderConnectionRepository>();
        connections.ListHistoricalByExternalAccountAsync("stripe", AccountId, 2, Arg.Any<CancellationToken>())
            .Returns(hasConnection ? [connection] : []);
        var secrets = Substitute.For<ISecretResolver>();
        secrets.ResolveAsync(SecretDefinitionRegistry.Keys.Stripe.WebhookSecret, null, Arg.Any<CancellationToken>()).Returns(SecretResolutionResult.Resolved(new ResolvedSecret(SecretDefinitionRegistry.Keys.Stripe.WebhookSecret,
        Secret,
        SecretSourceType.EnvironmentVariable,
        SecretScope.Instance,
        null,
        DateTimeOffset.UtcNow)));
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
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Stripe-Signature"] = global::Stripe.EventUtility.GenerateSignatureHeader(payload, Secret)
        },
        DateTimeOffset.UtcNow);

    private static string PaymentPayload(
        string eventId,
        string eventType,
        string? sessionId,
        string accountId,
        bool livemode = false,
        string? buyerEmail = null) => JsonSerializer.Serialize(new
        {
            id = eventId,
            @object = "event",
            account = accountId,
            api_version = global::Stripe.StripeConfiguration.ApiVersion,
            created = new DateTimeOffset(CreatedAt).ToUnixTimeSeconds(),
            livemode,
            type = eventType,
            data = new
            {
                @object = new
                {
                    id = sessionId,
                    @object = "checkout.session",
                    customer_details = new { email = buyerEmail }
                }
            }
        });

    private static PaymentAttempt PaymentAttemptForWebhook()
    {
        OrganizerPaymentRecipientSnapshot recipient = OrganizerPaymentRecipientSnapshot.Create(
            TenantId,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "stripe",
            "platform-test",
            AccountId,
            "BE",
            "EUR",
            Guid.CreateVersion7(),
            null,
            CreatedAt.AddMinutes(-2));
        PaymentAttempt attempt = PaymentAttempt.Create(
            Guid.CreateVersion7(), TenantId, Guid.CreateVersion7(), recipient, "OrganizerDirect",
            global::Stripe.StripeConfiguration.ApiVersion, "composition-handler", Money.Create(1_000, recipient.CurrencyCode), Money.Create(75, recipient.CurrencyCode), Money.Create(125, recipient.CurrencyCode),
            "checkout:handler", CreatedAt.AddMinutes(-2), CreatedAt.AddMinutes(30));
        attempt.MarkRequiresAction("cs_payment_1", CreatedAt.AddMinutes(-1), "req_create");
        return attempt;
    }

    private static IncomingWebhookProcessingContext PaymentProcessingContext(
        string eventId,
        string eventType,
        string objectId,
        string accountId)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            EventId = eventId,
            EventType = eventType,
            ObjectId = objectId,
            AccountId = accountId,
            LiveMode = false,
            ApiRevision = global::Stripe.StripeConfiguration.ApiVersion,
            CreatedAt
        });
        IncomingWebhookMessage message = IncomingWebhookMessage.CreateVerified(
            TenantId,
            "stripe-connect",
            eventId,
            $"{eventType}:{objectId}",
            eventType,
            payload,
            "sha256:" + new string('a', 64),
            "application/json",
            "utf-8",
            null,
            CreatedAt,
            CreatedAt.AddSeconds(1),
            CreatedAt.AddDays(14),
            "test-v1",
            CreatedAt.AddDays(30),
            CreatedAt.AddDays(90),
            CreatedAt.AddDays(14),
            CreatedAt.AddDays(30),
            payloadProvenance: WebhookPayloadProvenance.NormalizedProviderEnvelope);
        Guid leaseToken = Guid.CreateVersion7();
        message.Claim("test", leaseToken, CreatedAt.AddMinutes(2), CreatedAt.AddSeconds(2));
        return IncomingWebhookProcessingContext.FromClaimedMessage(
            message,
            leaseToken,
            message.ProcessingFence,
            message.ProcessingGeneration,
            CreatedAt.AddSeconds(3));
    }

    private sealed class StaticOptionsMonitor<T>(T currentValue) : IOptionsMonitor<T>
    {
        public T CurrentValue => currentValue;
        public T Get(string? name) => currentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
