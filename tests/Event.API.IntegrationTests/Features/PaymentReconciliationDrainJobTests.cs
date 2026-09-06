// ABOUTME: Verifies Quartz drains durable Checkout work around authoritative payment reconciliation.
// ABOUTME: Proves new attempts and Unknown same-key replays progress without manual service calls.

using Explore.API.Scheduling;
using Explore.Application.Contracts.Payments;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Services.Registration;
using Explore.Domain;
using Explore.Domain.ValueObjects;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Quartz;

namespace Event.Api.IntegrationTests.Features;

public sealed class PaymentReconciliationDrainJobTests
{
    private static readonly DateTime UtcNow = new(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid TenantId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000001");

    [Test]
    public async Task Execute_UsesStableConsumersAndQuartzCancellation()
    {
        var setup = Setup();

        await setup.Job.Execute(setup.Context);

        await setup.Repository.Received(2).ClaimDueDispatchEffectsAsync(
            "payment-checkout-dispatch-drain-job", 1, Arg.Any<DateTime>(), TimeSpan.FromMinutes(2), setup.Cancellation.Token);
        await setup.Repository.Received(1).ClaimDueReconciliationsAsync(
            "payment-reconciliation-drain-job", 50, Arg.Any<DateTime>(), TimeSpan.FromMinutes(2), setup.Cancellation.Token);
    }

    [Test]
    public async Task Execute_NewAttemptCreatesCheckoutWithoutManualDispatchCall()
    {
        var setup = Setup();
        PaymentAttempt attempt = Attempt();
        CheckoutDispatchClaim claim = CheckoutClaim(attempt);
        int dispatchClaims = 0;
        setup.Repository.ClaimDueDispatchEffectsAsync(
                "payment-checkout-dispatch-drain-job", 1, Arg.Any<DateTime>(), TimeSpan.FromMinutes(2), Arg.Any<CancellationToken>())
            .Returns(_ => dispatchClaims++ == 0 ? [claim] : []);
        setup.Repository.GetClaimedAttemptAsync(claim, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(attempt);
        setup.Repository.PrepareCheckoutDispatchAsync(claim, Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(attempt);
        setup.Creator.CreateAsync(Arg.Any<HostedCheckoutCreateRequest>(), Arg.Any<CancellationToken>())
            .Returns(HostedCheckoutCreateResult.Succeeded(Session("cs_new"), "req_new"));
        setup.Repository.CompleteCheckoutDispatchAsync(claim, "cs_new", "req_new", Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(true);

        await setup.Job.Execute(setup.Context);

        await setup.Creator.Received(1).CreateAsync(
            Arg.Is<HostedCheckoutCreateRequest>(request => request.PaymentAttemptId == attempt.Id),
            setup.Cancellation.Token);
    }

    [Test]
    public async Task Execute_PublicBaseUrlSubpathPreservesCallbackPrefix()
    {
        var setup = Setup("https://events.example.test/events");
        PaymentAttempt attempt = Attempt();
        CheckoutDispatchClaim claim = CheckoutClaim(attempt);
        int dispatchClaims = 0;
        setup.Repository.ClaimDueDispatchEffectsAsync(
                "payment-checkout-dispatch-drain-job", 1, Arg.Any<DateTime>(), TimeSpan.FromMinutes(2), Arg.Any<CancellationToken>())
            .Returns(_ => dispatchClaims++ == 0 ? [claim] : []);
        setup.Repository.GetClaimedAttemptAsync(claim, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(attempt);
        setup.Repository.PrepareCheckoutDispatchAsync(claim, Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(attempt);
        setup.Creator.CreateAsync(Arg.Any<HostedCheckoutCreateRequest>(), Arg.Any<CancellationToken>())
            .Returns(HostedCheckoutCreateResult.Succeeded(Session("cs_subpath"), "req_subpath"));
        setup.Repository.CompleteCheckoutDispatchAsync(claim, "cs_subpath", "req_subpath", Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(true);

        await setup.Job.Execute(setup.Context);

        await setup.Creator.Received(1).CreateAsync(
            Arg.Is<HostedCheckoutCreateRequest>(request =>
                request.SuccessUrl.AbsoluteUri == "https://events.example.test/events/payments/checkout/success" &&
                request.CancelUrl.AbsoluteUri == "https://events.example.test/events/payments/checkout/cancel"),
            setup.Cancellation.Token);
    }

    [Test]
    public async Task Execute_UnknownReconciliationRequeuesThenReplaysSameIdempotencyKey()
    {
        var setup = Setup();
        PaymentAttempt attempt = Attempt();
        attempt.MarkUnknown(UtcNow.AddSeconds(1), "req_timeout");
        CheckoutDispatchClaim checkoutClaim = CheckoutClaim(attempt);
        int dispatchClaims = 0;
        var reconciliationClaim = new PaymentReconciliationClaim(
            TenantId, Guid.CreateVersion7(), attempt.Id, Guid.CreateVersion7(), 1, 1,
            checkoutClaim.EffectId, UtcNow.AddSeconds(1), checkoutClaim.ProcessingFence, checkoutClaim.AttemptCount);
        int reconciliationClaims = 0;
        setup.Repository.ClaimDueDispatchEffectsAsync(
                "payment-checkout-dispatch-drain-job", 1, Arg.Any<DateTime>(), TimeSpan.FromMinutes(2), Arg.Any<CancellationToken>())
            .Returns(_ => dispatchClaims++ == 1 ? [checkoutClaim] : []);
        setup.Repository.ClaimDueReconciliationsAsync(
                "payment-reconciliation-drain-job", 50, Arg.Any<DateTime>(), TimeSpan.FromMinutes(2), Arg.Any<CancellationToken>())
            .Returns(_ => reconciliationClaims++ == 0 ? [reconciliationClaim] : []);
        setup.Repository.GetReconciliationAttemptAsync(reconciliationClaim, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(attempt);
        setup.Repository.RequeueLatestUnknownDispatchAsync(TenantId, attempt.Id, Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(true);
        setup.Repository.SettleReconciliationAsync(reconciliationClaim, Arg.Any<PaymentReconciliationDecision>(), Arg.Any<CancellationToken>()).Returns(true);
        setup.Repository.GetClaimedAttemptAsync(checkoutClaim, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(attempt);
        setup.Repository.PrepareCheckoutDispatchAsync(checkoutClaim, Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(attempt);
        setup.Creator.CreateAsync(Arg.Any<HostedCheckoutCreateRequest>(), Arg.Any<CancellationToken>())
            .Returns(HostedCheckoutCreateResult.Succeeded(Session("cs_replayed"), "req_replayed"));
        setup.Repository.CompleteCheckoutDispatchAsync(checkoutClaim, "cs_replayed", "req_replayed", Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(true);

        await setup.Job.Execute(setup.Context);

        await setup.Repository.Received(1).RequeueLatestUnknownDispatchAsync(
            TenantId, attempt.Id, Arg.Any<DateTime>(), Arg.Any<DateTime>(), setup.Cancellation.Token);
        await setup.Creator.Received(1).CreateAsync(
            Arg.Is<HostedCheckoutCreateRequest>(request => request.ProviderIdempotencyKey == attempt.ProviderIdempotencyKey),
            setup.Cancellation.Token);
    }

    [Test]
    public async Task Execute_MissingBrowserOriginStillRunsAuthoritativeReconciliation()
    {
        var setup = Setup(publicBaseUrl: null);

        await setup.Job.Execute(setup.Context);

        await setup.Repository.Received(1).ClaimDueReconciliationsAsync(
            "payment-reconciliation-drain-job", 50, Arg.Any<DateTime>(), TimeSpan.FromMinutes(2), setup.Cancellation.Token);
        await setup.Creator.DidNotReceiveWithAnyArgs().CreateAsync(default!, default);
    }

    [Test]
    public async Task Execute_InvalidHttpBrowserOriginDefersPreHandoffWithoutProviderCall()
    {
        var setup = Setup("http://localhost:7177");
        PaymentAttempt attempt = Attempt();
        CheckoutDispatchClaim claim = CheckoutClaim(attempt);
        int dispatchClaims = 0;
        setup.Repository.ClaimDueDispatchEffectsAsync(
                "payment-checkout-dispatch-drain-job", 1, Arg.Any<DateTime>(), TimeSpan.FromMinutes(2), Arg.Any<CancellationToken>())
            .Returns(_ => dispatchClaims++ == 0 ? [claim] : []);
        setup.Repository.DeferCheckoutDispatchForConfigurationAsync(
                claim, "checkout_return_origin_invalid", Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(CheckoutDispatchConfigurationDisposition.Deferred);

        await setup.Job.Execute(setup.Context);

        await setup.Repository.Received(1).DeferCheckoutDispatchForConfigurationAsync(
            claim,
            "checkout_return_origin_invalid",
            Arg.Any<DateTime>(),
            Arg.Any<DateTime>(),
            setup.Cancellation.Token);
        await setup.Repository.Received(1).ClaimDueReconciliationsAsync(
            "payment-reconciliation-drain-job", 50, Arg.Any<DateTime>(), TimeSpan.FromMinutes(2), setup.Cancellation.Token);
        await setup.Creator.DidNotReceiveWithAnyArgs().CreateAsync(default!, default);
    }

    private static TestSetup Setup(string? publicBaseUrl = "https://events.example.test")
    {
        var repository = Substitute.For<IRegistrationPaymentAttemptRepository>();
        repository.ClaimDueDispatchEffectsAsync(
                "payment-checkout-dispatch-drain-job", 1, Arg.Any<DateTime>(), TimeSpan.FromMinutes(2), Arg.Any<CancellationToken>())
            .Returns([]);
        repository.ClaimDueReconciliationsAsync(
                "payment-reconciliation-drain-job", 50, Arg.Any<DateTime>(), TimeSpan.FromMinutes(2), Arg.Any<CancellationToken>())
            .Returns([]);
        var creator = Substitute.For<IHostedCheckoutSessionCreator>();
        var checkout = Substitute.For<IHostedCheckoutSessionRetriever>();
        var payment = Substitute.For<IPaymentIntentRetriever>();
        var time = new FixedTimeProvider(UtcNow);
        var dispatch = new RegistrationPaymentCheckoutDispatchService(
            repository,
            creator,
            checkout,
            payment,
            Substitute.For<IRegistrationOrderLifecycleService>(),
            time,
            ReadyCheckoutActivation(),
            CurrentAcceptance());
        var reconciliation = new RegistrationPaymentReconciliationService(repository, checkout, payment, time);
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["PublicBaseUrl"] = publicBaseUrl })
            .Build();
        var job = new PaymentReconciliationDrainJob(dispatch, reconciliation, configuration, NullLogger<PaymentReconciliationDrainJob>.Instance);
        IJobExecutionContext context = Substitute.For<IJobExecutionContext>();
        var cancellation = new CancellationTokenSource();
        context.CancellationToken.Returns(cancellation.Token);
        return new(repository, creator, job, context, cancellation);
    }

    private static PaymentAttempt Attempt()
    {
        OrganizerPaymentRecipientSnapshot recipient = OrganizerPaymentRecipientSnapshot.Create(
            TenantId, Guid.CreateVersion7(), Guid.CreateVersion7(), "stripe", "platform-test", "acct_123", "BE", "EUR",
            Guid.CreateVersion7(), null, UtcNow.AddMinutes(-2));
        PaymentAttempt attempt = PaymentAttempt.Create(
            Guid.CreateVersion7(), TenantId, Guid.CreateVersion7(), recipient, "OrganizerDirect", "2026-07-29.dahlia",
            "composition-job", Money.Create(1_000, recipient.CurrencyCode), Money.Create(75, recipient.CurrencyCode), Money.Create(125, recipient.CurrencyCode), "checkout:job:stable", UtcNow.AddMinutes(-2), UtcNow.AddMinutes(30));
        attempt.AttachAcceptance(PaidAcceptanceTestFacts.Create(
            recipient, attempt.RegistrationOrderId, Guid.CreateVersion7(), "composition-job",
            1_000, 75, 125, UtcNow.AddMinutes(-1)));
        return attempt;
    }

    private static IPaidOrderAcceptanceFreshnessService CurrentAcceptance()
    {
        var freshness = Substitute.For<IPaidOrderAcceptanceFreshnessService>();
        freshness.IsCurrentAsync(Arg.Any<PaymentAttempt>(), Arg.Any<CancellationToken>()).Returns(true);
        return freshness;
    }

    private static IPaidCheckoutActivationService ReadyCheckoutActivation()
    {
        var activation = Substitute.For<IPaidCheckoutActivationService>();
        activation.EvaluateAsync(Arg.Any<PaidCheckoutActivationRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PaidCheckoutActivationResult(true, null, "active"));
        return activation;
    }

    private static CheckoutDispatchClaim CheckoutClaim(PaymentAttempt attempt) =>
        new(Guid.CreateVersion7(), TenantId, attempt.RegistrationOrderId, attempt.Id, Guid.CreateVersion7(), 1, AttemptCount: 1);

    private static HostedCheckoutSession Session(string id) =>
        new(id, null, HostedCheckoutSessionStatus.Open, HostedCheckoutPaymentStatus.Unpaid, null, UtcNow.AddMinutes(30), 1_125, "EUR");

    private sealed record TestSetup(
        IRegistrationPaymentAttemptRepository Repository,
        IHostedCheckoutSessionCreator Creator,
        PaymentReconciliationDrainJob Job,
        IJobExecutionContext Context,
        CancellationTokenSource Cancellation);

    private sealed class FixedTimeProvider(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
