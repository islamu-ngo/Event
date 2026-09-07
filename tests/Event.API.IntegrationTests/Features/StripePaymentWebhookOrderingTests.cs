using System.Text;
using System.Text.Json;
using Explore.API.Services;
using Explore.Application.Contracts.Payments;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.Services.Registration;
using Explore.Domain;
using Explore.Domain.ValueObjects;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Infrastructure.Configuration;
using Explore.Infrastructure.Payments.Stripe;
using Explore.Infrastructure.Webhooks;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Secrets.Database;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features;

public sealed class StripePaymentWebhookOrderingTests
{
    private const string Secret = "whsec_ordering_fixture";
    private const string AccountId = "acct_ordering";
    private static readonly Guid TenantId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000001");
    private static readonly Guid OrderId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000010");
    private static readonly DateTime UtcNow = new(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task ExactDuplicateAndDifferentEventSameTransition_AcknowledgeOriginalOnly()
    {
        await using TestHarness harness = await TestHarness.CreateAsync();
        string firstPayload = Payload("evt_original", "checkout.session.completed", "cs_ordering", UtcNow.AddMinutes(-2));
        IncomingWebhookCaptureResult first = await harness.CaptureSignedAsync(firstPayload);
        IncomingWebhookCaptureResult exactDuplicate = await harness.CaptureSignedAsync(firstPayload);
        string transitionDuplicatePayload = Payload("evt_transition_duplicate", "checkout.session.completed", "cs_ordering", UtcNow.AddMinutes(-1));
        IncomingWebhookCaptureResult transitionDuplicate = await harness.CaptureSignedAsync(transitionDuplicatePayload);

        await Assert.That(first.Outcome).IsEqualTo(IncomingWebhookCaptureOutcome.Captured);
        await Assert.That(exactDuplicate.Outcome).IsEqualTo(IncomingWebhookCaptureOutcome.Duplicate);
        await Assert.That(transitionDuplicate.Outcome).IsEqualTo(IncomingWebhookCaptureOutcome.Duplicate);
        await Assert.That(exactDuplicate.MessageId).IsEqualTo(first.MessageId);
        await Assert.That(transitionDuplicate.MessageId).IsEqualTo(first.MessageId);
        await Assert.That(await harness.Context.IncomingWebhookMessages.CountAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task SameEventIdWithDifferentExactSignedBody_IsPayloadConflict()
    {
        await using TestHarness harness = await TestHarness.CreateAsync();
        IncomingWebhookCaptureResult first = await harness.CaptureSignedAsync(
            Payload("evt_conflict", "checkout.session.completed", "cs_ordering", UtcNow.AddMinutes(-2)));
        IncomingWebhookCaptureResult conflict = await harness.CaptureSignedAsync(
            Payload("evt_conflict", "checkout.session.completed", "cs_other", UtcNow.AddMinutes(-1)));

        await Assert.That(first.Outcome).IsEqualTo(IncomingWebhookCaptureOutcome.Captured);
        await Assert.That(conflict.Outcome).IsEqualTo(IncomingWebhookCaptureOutcome.PayloadConflict);
        IncomingWebhookMessage persisted = await harness.Context.IncomingWebhookMessages.AsNoTracking().SingleAsync();
        await Assert.That(persisted.Status).IsEqualTo(IncomingWebhookMessageStatus.PayloadConflict);
    }

    [Test]
    public async Task DelayedCompletedAfterNewerProcessing_RetrievesAuthoritativeStateWithoutRegression()
    {
        await using TestHarness harness = await TestHarness.CreateAsync();
        PaymentAttempt attempt = await harness.CreateAttemptAsync(PaymentAttemptStatusEnum.Processing);
        string payload = Payload("evt_delayed_completed", "checkout.session.completed", "cs_ordering", UtcNow.AddMinutes(-1));

        IncomingWebhookCaptureResult capture = await harness.CaptureAndHandleSignedAsync(payload);
        harness.Checkout.RetrieveAsync(Arg.Any<HostedCheckoutRetrieveRequest>(), Arg.Any<CancellationToken>())
            .Returns(HostedCheckoutRetrieveResult.Succeeded(Session(
                HostedCheckoutSessionStatus.Open,
                HostedCheckoutPaymentStatus.Unpaid,
                "pi_ordering"), "req_session"));
        harness.Payment.RetrievePaymentIntentAsync(Arg.Any<PaymentIntentRetrieveRequest>(), Arg.Any<CancellationToken>())
            .Returns(PaymentIntentRetrieveResult.Succeeded(Intent(PaymentIntentStatus.RequiresAction), "req_action"));

        _ = await harness.ReconcileAsync();

        await Assert.That(capture.Outcome).IsEqualTo(IncomingWebhookCaptureOutcome.Captured);
        await harness.Checkout.Received(1).RetrieveAsync(
            Arg.Is<HostedCheckoutRetrieveRequest>(request => request.ExternalAccountId == AccountId),
            Arg.Any<CancellationToken>());
        PaymentAttempt persisted = await harness.Context.PaymentAttempts.AsNoTracking().SingleAsync(value => value.Id == attempt.Id);
        await Assert.That(persisted.PaymentAttemptStatusId).IsEqualTo((int)PaymentAttemptStatusEnum.Processing);
        await Assert.That(persisted.LastStatusObservedAt).IsEqualTo(UtcNow.AddMinutes(-30));
    }

    [Test]
    [Arguments("checkout.session.async_payment_failed")]
    [Arguments("checkout.session.expired")]
    public async Task OutOfOrderTerminalFailureAfterSucceeded_CannotRegressOrCreateFailure(string eventType)
    {
        await using TestHarness harness = await TestHarness.CreateAsync();
        PaymentAttempt attempt = await harness.CreateAttemptAsync(PaymentAttemptStatusEnum.Succeeded);
        IncomingWebhookCaptureResult capture = await harness.CaptureAndHandleSignedAsync(
            Payload("evt_out_of_order_" + eventType.Split('.').Last(), eventType, "cs_ordering", UtcNow.AddMinutes(-1)));
        HostedCheckoutSession session = eventType == "checkout.session.expired"
            ? Session(HostedCheckoutSessionStatus.Expired, HostedCheckoutPaymentStatus.Unpaid, "pi_ordering")
            : Session(HostedCheckoutSessionStatus.Complete, HostedCheckoutPaymentStatus.Unpaid, "pi_ordering");
        harness.Checkout.RetrieveAsync(Arg.Any<HostedCheckoutRetrieveRequest>(), Arg.Any<CancellationToken>())
            .Returns(HostedCheckoutRetrieveResult.Succeeded(session, "req_session"));
        harness.Payment.RetrievePaymentIntentAsync(Arg.Any<PaymentIntentRetrieveRequest>(), Arg.Any<CancellationToken>())
            .Returns(PaymentIntentRetrieveResult.Succeeded(Intent(PaymentIntentStatus.RequiresPaymentMethod), "req_failed"));

        _ = await harness.ReconcileAsync();

        await Assert.That(capture.Outcome).IsEqualTo(IncomingWebhookCaptureOutcome.Captured);
        PaymentAttempt persisted = await harness.Context.PaymentAttempts.AsNoTracking().SingleAsync(value => value.Id == attempt.Id);
        await Assert.That(persisted.PaymentAttemptStatusId).IsEqualTo((int)PaymentAttemptStatusEnum.Succeeded);
        await Assert.That(persisted.FailedAt).IsNull();
        await Assert.That(persisted.CancelledAt).IsNull();
    }

    private static string Payload(string eventId, string eventType, string sessionId, DateTime createdAt) =>
        JsonSerializer.Serialize(new
        {
            id = eventId,
            @object = "event",
            account = AccountId,
            api_version = global::Stripe.StripeConfiguration.ApiVersion,
            created = new DateTimeOffset(createdAt).ToUnixTimeSeconds(),
            livemode = false,
            type = eventType,
            data = new { @object = new { id = sessionId, @object = "checkout.session", customer_details = new { email = "not-retained@example.test" } } }
        });

    private static HostedCheckoutSession Session(
        HostedCheckoutSessionStatus status,
        HostedCheckoutPaymentStatus paymentStatus,
        string? paymentId) =>
        new("cs_ordering", null, status, paymentStatus, paymentId, UtcNow.AddMinutes(30), 1_125, "EUR");

    private static PaymentIntentObservation Intent(PaymentIntentStatus status) =>
        new("pi_ordering", 1_125, "EUR", 200, status);

    private sealed class TestHarness : IAsyncDisposable
    {
        private readonly string databasePath;
        private readonly IncomingWebhookIntakeService intake;
        private readonly IncomingWebhookMessageRepository incomingRepository;
        private readonly StripePaymentIncomingWebhookHandler handler;
        private readonly RegistrationPaymentAttemptRepository paymentRepository;
        private readonly FixedTimeProvider timeProvider;

        private TestHarness(
            string databasePath,
            ExploreDbContext context,
            IncomingWebhookIntakeService intake,
            IncomingWebhookMessageRepository incomingRepository,
            StripePaymentIncomingWebhookHandler handler,
            RegistrationPaymentAttemptRepository paymentRepository,
            IHostedCheckoutSessionRetriever checkout,
            IPaymentIntentRetriever payment,
            FixedTimeProvider timeProvider)
        {
            this.databasePath = databasePath;
            Context = context;
            this.intake = intake;
            this.incomingRepository = incomingRepository;
            this.handler = handler;
            this.paymentRepository = paymentRepository;
            Checkout = checkout;
            Payment = payment;
            this.timeProvider = timeProvider;
        }

        public ExploreDbContext Context { get; }
        public IHostedCheckoutSessionRetriever Checkout { get; }
        public IPaymentIntentRetriever Payment { get; }

        public static async Task<TestHarness> CreateAsync()
        {
            string databasePath = Path.Combine(
                Path.GetTempPath(),
                $"event-stripe-payment-{Guid.CreateVersion7():N}.db");
            var optionsBuilder = new DbContextOptionsBuilder<ExploreDbContext>();
            PrimaryDatabaseProviderComposition.ConfigureApplication(
                optionsBuilder,
                new PrimaryDatabaseConnectionOptions
                {
                    Role = PrimaryDatabaseRole.Runtime,
                    Provider = PrimaryDatabaseProvider.Sqlite,
                    Database = databasePath
                });
            optionsBuilder.UseSnakeCaseNamingConvention();
            var context = new ExploreDbContext(optionsBuilder.Options);
            context.EnableTenantFilterBypass("Signed payment webhook ordering fixtures.");
            await context.Database.OpenConnectionAsync();
            await context.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF;");
            await context.Database.EnsureCreatedAsync();
            var paymentRepository = new RegistrationPaymentAttemptRepository(context);
            var incomingRepository = new IncomingWebhookMessageRepository(context);
            ISecretResolver secrets = Substitute.For<ISecretResolver>();
            secrets.ResolveAsync(SecretDefinitionRegistry.Keys.Stripe.WebhookSecret, null, Arg.Any<CancellationToken>())
                .Returns(SecretResolutionResult.Resolved(new ResolvedSecret(
                    SecretDefinitionRegistry.Keys.Stripe.WebhookSecret,
                    Secret,
                    SecretSourceType.EnvironmentVariable,
                    SecretScope.Instance,
                    null,
                    DateTimeOffset.UtcNow)));
            var connectionEntity = OrganizerPaymentProviderConnection.Create(
                Guid.CreateVersion7(), TenantId, Guid.CreateVersion7(), "stripe", "platform-test", AccountId, UtcNow.AddHours(-1));
            IOrganizerPaymentProviderConnectionRepository connections = Substitute.For<IOrganizerPaymentProviderConnectionRepository>();
            connections.ListHistoricalByExternalAccountAsync("stripe", AccountId, 2, Arg.Any<CancellationToken>())
                .Returns([connectionEntity]);
            var verifier = new StripeConnectIncomingWebhookVerifier(
                new StaticOptionsMonitor<WebhookOptions>(new WebhookOptions()),
                Options.Create(new StripePaymentOptions()),
                secrets,
                connections,
                NullLogger<StripeConnectIncomingWebhookVerifier>.Instance);
            var timeProvider = new FixedTimeProvider(DateTime.UtcNow.AddMinutes(1));
            var intake = new IncomingWebhookIntakeService(
                new IncomingWebhookVerifierRegistry([verifier]),
                incomingRepository,
                new WebhookRetentionPolicyResolver(new StaticOptionsMonitor<WebhookRetentionSettings>(new WebhookRetentionSettings())),
                timeProvider,
                NullLogger<IncomingWebhookIntakeService>.Instance);
            var checkout = Substitute.For<IHostedCheckoutSessionRetriever>();
            var payment = Substitute.For<IPaymentIntentRetriever>();
            return new(
                databasePath,
                context,
                intake,
                incomingRepository,
                new StripePaymentIncomingWebhookHandler(paymentRepository),
                paymentRepository,
                checkout,
                payment,
                timeProvider);
        }

        public async Task<PaymentAttempt> CreateAttemptAsync(PaymentAttemptStatusEnum targetStatus)
        {
            OrganizerPaymentRecipientSnapshot recipient = OrganizerPaymentRecipientSnapshot.Create(
                TenantId, Guid.CreateVersion7(), Guid.CreateVersion7(), "stripe", "platform-test", AccountId, "BE", "EUR",
                Guid.CreateVersion7(), null, UtcNow.AddHours(-2));
            PaymentAttempt attempt = PaymentAttempt.Create(
                Guid.CreateVersion7(), TenantId, OrderId, recipient, "OrganizerDirect", global::Stripe.StripeConfiguration.ApiVersion,
                "composition-ordering", Money.Create(1_000, recipient.CurrencyCode), Money.Create(75, recipient.CurrencyCode), Money.Create(125, recipient.CurrencyCode), "checkout:ordering", UtcNow.AddHours(-2), UtcNow.AddHours(1));
            _ = await paymentRepository.ClaimAsync(
                new RegistrationPaymentAttemptClaim(attempt, CheckoutDispatchEffect.Create(attempt, UtcNow.AddHours(-2))),
                CancellationToken.None);
            CheckoutDispatchClaim dispatch = (await paymentRepository.ClaimDueDispatchEffectsAsync(
                "setup", 1, UtcNow.AddHours(-1), TimeSpan.FromMinutes(2), CancellationToken.None)).Single();
            _ = await paymentRepository.MarkCheckoutDispatchPendingAsync(dispatch, UtcNow.AddMinutes(-50), CancellationToken.None);
            _ = await paymentRepository.CompleteCheckoutDispatchAsync(
                dispatch, "cs_ordering", "req_create", UtcNow.AddMinutes(-45), CancellationToken.None);
            PaymentAttempt current = await Context.PaymentAttempts.SingleAsync(value => value.Id == attempt.Id);
            if (targetStatus is PaymentAttemptStatusEnum.Processing or PaymentAttemptStatusEnum.Succeeded)
            {
                current.MarkProcessing("cs_ordering", "pi_ordering", UtcNow.AddMinutes(-30), "req_processing");
            }

            if (targetStatus == PaymentAttemptStatusEnum.Succeeded)
            {
                current.MarkSucceededFromCheckout("cs_ordering", "pi_ordering", UtcNow.AddMinutes(-20), "req_success");
            }

            await Context.SaveChangesAsync();
            return current;
        }

        public async Task<IncomingWebhookCaptureResult> CaptureSignedAsync(string payload)
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(payload));
            httpContext.Request.ContentType = "application/json";
            httpContext.Request.Headers["Stripe-Signature"] = global::Stripe.EventUtility.GenerateSignatureHeader(payload, Secret);
            IncomingWebhookReadResult read = await intake.ReadAndVerifyAsync(
                httpContext.Request, "stripe-connect", 65_536, CancellationToken.None);
            await Assert.That(read.Succeeded).IsTrue();
            return await intake.CaptureAsync(read, CancellationToken.None);
        }

        public async Task<IncomingWebhookCaptureResult> CaptureAndHandleSignedAsync(string payload)
        {
            IncomingWebhookCaptureResult capture = await CaptureSignedAsync(payload);
            DateTime processingNow = timeProvider.GetUtcNow().UtcDateTime;
            IncomingWebhookClaim claim = (await incomingRepository.ClaimDueAsync(
                new IncomingWebhookClaimRequest("ordering-test", 1, processingNow, TimeSpan.FromMinutes(2)),
                CancellationToken.None)).Single();
            IncomingWebhookMessage message = await incomingRepository.GetActiveClaimAsync(
                claim.TenantId,
                claim.IncomingWebhookMessageId,
                claim.LeaseToken,
                claim.ProcessingFence,
                claim.ProcessingGeneration,
                processingNow.AddSeconds(1),
                CancellationToken.None) ?? throw new InvalidOperationException("Claimed webhook was not reloadable.");
            IncomingWebhookProcessingContext processing = IncomingWebhookProcessingContext.FromClaimedMessage(
                message, claim.LeaseToken, claim.ProcessingFence, claim.ProcessingGeneration, processingNow.AddSeconds(1));
            IncomingWebhookProcessingResult handled = await handler.HandleAsync(processing, CancellationToken.None);
            await Assert.That(handled.Outcome).IsEqualTo(IncomingWebhookProcessingOutcome.Processed);
            return capture;
        }

        public Task<RegistrationPaymentReconciliationResult> ReconcileAsync() =>
            new RegistrationPaymentReconciliationService(paymentRepository, Checkout, Payment, timeProvider)
                .ReconcileDueAsync(new RegistrationPaymentReconciliationRequest("ordering-reconcile"), CancellationToken.None);

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            File.Delete(databasePath);
        }
    }

    private sealed class FixedTimeProvider(DateTime value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
