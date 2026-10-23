using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Explore.Application.Contracts.Payments;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Contracts.Services;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Infrastructure.Payments.Stripe;
using Explore.Infrastructure.Payments.Stripe.Checkout;
using Explore.Infrastructure.Payments.Stripe.Refunds;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure;

public sealed class StripeMutationRetryOwnershipTests
{
    private static readonly Guid AttemptId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000101");
    private static readonly Guid OrderId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000102");
    private const string ProviderKey = "operation:immutable-provider-bound";
    private const string AccountId = "acct_original";
    private const string SensitiveMessage = "transport detail must not become an application result";
    private static readonly DateTime RefundNow = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    [Arguments(408)]
    [Arguments(409)]
    [Arguments(429)]
    [Arguments(500)]
    public async Task CheckoutCreation_AmbiguityMakesOneKeyedMutationHandoff(int failure)
    {
        using var transport = new ControlledTransport(StripeCheckoutAdapter.HttpClientName, failure);
        var adapter = new StripeCheckoutAdapter(transport, transport.Secrets, PaymentOptions());
        HostedCheckoutCreateRequest request = HostedCheckoutCreateRequest.Create(
            AttemptId, OrderId, "stripe", AccountId, ProviderKey, "EUR", 1250, 250,
            new DateTime(2026, 10, 10, 12, 30, 0, DateTimeKind.Utc),
            new Uri("https://events.example.test/"),
            new Uri("https://events.example.test/success"),
            new Uri("https://events.example.test/cancel"));

        HostedCheckoutCreateResult result = await adapter.CreateAsync(request, CancellationToken.None);

        await AssertMutation(transport, "/v1/checkout/sessions", ProviderKey, AccountId);
        await Assert.That(transport.Requests).Count().IsEqualTo(1);
        await Assert.That(result.Outcome).IsEqualTo(HostedCheckoutOperationOutcome.Unknown);
        await Assert.That(result.Failure!.ProviderHandoffStarted).IsTrue();
        await Assert.That(result.Failure.Kind).IsEqualTo(failure <= 0
            ? HostedCheckoutFailureKind.Network
            : HostedCheckoutFailureKind.ProviderUnknown);
        await Assert.That(result.Failure.PreHandoffDisposition).IsEqualTo(HostedCheckoutPreHandoffDisposition.None);
        await Assert.That(result.Failure.Code).IsEqualTo(failure <= 0
            ? "checkout_provider_network_ambiguous"
            : "checkout_provider_rejected");
        await AssertNonsecretResult(transport, result);
    }

    [Test]
    [Arguments(false, 0)]
    [Arguments(false, -1)]
    [Arguments(false, 408)]
    [Arguments(false, 409)]
    [Arguments(false, 429)]
    [Arguments(false, 500)]
    [Arguments(true, 0)]
    [Arguments(true, -1)]
    [Arguments(true, 408)]
    [Arguments(true, 409)]
    [Arguments(true, 429)]
    [Arguments(true, 500)]
    public async Task CheckoutCancellation_ObservationDoesNotReplayFailedMutation(bool paymentIntent, int failure)
    {
        using var transport = new ControlledTransport(StripeCheckoutAdapter.HttpClientName, failure);
        var adapter = new StripeCheckoutAdapter(transport, transport.Secrets, PaymentOptions());

        PaymentCancellationProviderResult result = await adapter.CancelAsync(
            PaymentCancellationRequest.Create(
                "stripe", AccountId, paymentIntent ? null : "cs_original",
                paymentIntent ? "pi_original" : null, ProviderKey),
            CancellationToken.None);

        await AssertMutation(transport, paymentIntent
            ? "/v1/payment_intents/pi_original/cancel"
            : "/v1/checkout/sessions/cs_original/expire", ProviderKey, AccountId);
        await Assert.That(transport.Requests).Count().IsEqualTo(2);
        RecordedRequest observation = transport.Requests.Single(request => request.Method == HttpMethod.Get);
        await Assert.That(observation.Path).IsEqualTo(paymentIntent
            ? "/v1/payment_intents/pi_original"
            : "/v1/checkout/sessions/cs_original");
        await Assert.That(observation.Account).IsEqualTo(AccountId);
        await Assert.That(observation.Key).IsNull();
        await Assert.That(result.Outcome).IsEqualTo(PaymentCancellationProviderOutcome.Unknown);
        await Assert.That(result.ProviderHandoffStarted).IsTrue();
        await Assert.That(result.FailureCode).IsEqualTo("payment_cancellation_provider_failed");
        await AssertNonsecretResult(transport, result);
    }

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    [Arguments(408)]
    [Arguments(409)]
    [Arguments(429)]
    [Arguments(500)]
    public async Task RefundCreation_AmbiguityMakesOneKeyedMutationHandoff(int failure)
    {
        using var transport = new ControlledTransport(StripeRefundAdapter.HttpClientName, failure);
        var adapter = new StripeRefundAdapter(transport, transport.Secrets, PaymentOptions(), new RefundClock());

        RefundProviderResult result = await adapter.CreateAsync(
            RefundCreateRequest.Create(AttemptId, "stripe", AccountId, "pi_original", ProviderKey, 500, "EUR", 38)
                with
            { MutationExpiresAt = RefundNow.AddHours(23) },
            CancellationToken.None);

        await AssertMutation(transport, "/v1/refunds", ProviderKey, AccountId);
        await Assert.That(transport.Requests).Count().IsEqualTo(1);
        await Assert.That(result.Outcome).IsEqualTo(RefundProviderOutcome.Unknown);
        await Assert.That(result.Failure!.ProviderHandoffStarted).IsTrue();
        await Assert.That(result.Failure.Kind).IsEqualTo(failure <= 0
            ? RefundProviderFailureKind.Network
            : RefundProviderFailureKind.ProviderUnknown);
        await Assert.That(result.Failure.Code).IsEqualTo(failure <= 0
            ? "refund_provider_network_ambiguous"
            : "refund_provider_rejected");
        await AssertNonsecretResult(transport, result);
    }

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    [Arguments(408)]
    [Arguments(429)]
    [Arguments(500)]
    public async Task FeeRefund_AmbiguityDoesNotReplayOrDiscardProvenBuyerRefund(int failure)
    {
        using var transport = new ControlledTransport(StripeRefundAdapter.HttpClientName, failure, failFeeRefund: true);
        var adapter = new StripeRefundAdapter(transport, transport.Secrets, PaymentOptions(), new RefundClock());

        RefundProviderResult buyer = await adapter.CreateAsync(
            RefundCreateRequest.Create(AttemptId, "stripe", AccountId, "pi_original", ProviderKey, 500, "EUR", 38)
                with
            { MutationExpiresAt = RefundNow.AddHours(23) },
            CancellationToken.None);
        await Assert.That(buyer.Observation!.Status).IsEqualTo(RefundProviderStatus.Succeeded);
        RefundProviderResult result = await adapter.RetrieveAsync(
            RefundRetrieveRequest.Create("stripe", AccountId, "pi_original", "re_original", ProviderKey, 500, "EUR", 38)
                with
            { RefundAttemptId = AttemptId, AllowApplicationFeeMutation = true, MutationExpiresAt = RefundNow.AddHours(23) },
            CancellationToken.None);

        await Assert.That(transport.Requests).Count().IsEqualTo(5);
        RecordedRequest buyerRefund = transport.Requests.Single(request => request.Path == "/v1/refunds");
        await Assert.That(buyerRefund.Method).IsEqualTo(HttpMethod.Post);
        await Assert.That(buyerRefund.Account).IsEqualTo(AccountId);
        await Assert.That(buyerRefund.Key).IsEqualTo(ProviderKey);
        RecordedRequest feeRefund = transport.Requests.Single(request =>
            request.Method == HttpMethod.Post && request.Path == "/v1/application_fees/fee_original/refunds");
        await Assert.That(feeRefund.Method).IsEqualTo(HttpMethod.Post);
        await Assert.That(feeRefund.Account).IsNull();
        await Assert.That(feeRefund.Key).IsEqualTo(ProviderKey + ":fee");
        RecordedRequest observation = transport.Requests.Single(request => request.Path == "/v1/charges/ch_original");
        await Assert.That(observation.Path).IsEqualTo("/v1/charges/ch_original");
        await Assert.That(observation.Account).IsEqualTo(AccountId);
        await Assert.That(observation.Key).IsNull();
        await Assert.That(result.Outcome).IsEqualTo(RefundProviderOutcome.Observed);
        await Assert.That(result.Observation!.Status).IsEqualTo(RefundProviderStatus.Succeeded);
        await Assert.That(result.Observation.ProviderRefundId).IsEqualTo("re_original");
        await Assert.That(result.Observation.ApplicationFeeRefundAmountMinor).IsNull();
        await Assert.That(result.Observation.ApplicationFeeRefundFailureCode).IsNull();
        await AssertNonsecretResult(transport, result);
    }

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    [Arguments(408)]
    [Arguments(429)]
    [Arguments(500)]
    public async Task ConnectAccountCreation_AmbiguityRequiresReconciliationWithoutSdkReplay(int failure)
    {
        using var transport = new ControlledTransport(StripeConnectAccountAdapter.HttpClientName, failure);
        var adapter = new StripeConnectAccountAdapter(
            transport, transport.Secrets, TimeProvider.System, PaymentOptions());

        OrganizerPaymentProviderAccountCreationResult result = await adapter.CreateAccountAsync(
            new(AttemptId, OrderId, "stripe", "platform_original", ProviderKey),
            CancellationToken.None);

        await AssertMutation(transport, "/v1/accounts", ProviderKey, account: null);
        await Assert.That(transport.Requests).Count().IsEqualTo(1);
        await Assert.That(result.Status).IsEqualTo(OrganizerPaymentProviderAccountCreationStatus.ManualReconciliationRequired);
        await Assert.That(result.FailureKind).IsEqualTo(failure <= 0
            ? OrganizerPaymentProviderFailureKind.Network
            : OrganizerPaymentProviderFailureKind.ProviderUnknown);
        await Assert.That(result.FailureCode).IsEqualTo(failure <= 0
            ? "organizer_payment_provider_network_failure"
            : "organizer_payment_provider_api_error");
        await Assert.That(result.ExternalAccountId).IsNull();
        await AssertNonsecretResult(transport, result);
    }

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    [Arguments(429)]
    [Arguments(500)]
    public async Task ConnectOnboardingLink_FailureDoesNotReplayTheMutation(int failure)
    {
        using var transport = new ControlledTransport(StripeConnectAccountAdapter.HttpClientName, failure);
        var adapter = new StripeConnectAccountAdapter(
            transport, transport.Secrets, TimeProvider.System, PaymentOptions());

        OrganizerPaymentOnboardingLinkCreationResult result = await adapter.CreateOnboardingLinkAsync(
            new("stripe", "platform_original", AccountId,
                new Uri("https://events.example.test/return"),
                new Uri("https://events.example.test/refresh"),
                OrganizerPaymentOnboardingType.AccountOnboarding),
            CancellationToken.None);

        RecordedRequest mutation = transport.Requests.Single();
        await Assert.That(mutation.Method).IsEqualTo(HttpMethod.Post);
        await Assert.That(mutation.Path).IsEqualTo("/v1/account_links");
        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.OnboardingUrl).IsNull();
        await Assert.That(result.FailureKind).IsEqualTo(failure <= 0
            ? OrganizerPaymentProviderFailureKind.Network
            : OrganizerPaymentProviderFailureKind.ProviderUnknown);
        await AssertNonsecretResult(transport, result);
    }

    private static IOptions<StripePaymentOptions> PaymentOptions() => Options.Create(new StripePaymentOptions());

    private sealed class RefundClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(RefundNow);
    }

    private static async Task AssertMutation(
        ControlledTransport transport, string path, string key, string? account)
    {
        RecordedRequest mutation = transport.Requests.Single(request => request.Method == HttpMethod.Post);
        await Assert.That(mutation.Path).IsEqualTo(path);
        await Assert.That(mutation.Key).IsEqualTo(key);
        await Assert.That(mutation.Account).IsEqualTo(account);
    }

    private static async Task AssertNonsecretResult<T>(ControlledTransport transport, T result)
    {
        string json = JsonSerializer.Serialize(result);
        await Assert.That(json).DoesNotContain(transport.PlatformSecret);
        await Assert.That(json).DoesNotContain(SensitiveMessage);
    }

    private sealed record RecordedRequest(HttpMethod Method, string Path, string? Account, string? Key);

    private sealed class ControlledTransport : HttpMessageHandler, IHttpClientFactory
    {
        private readonly string clientName;
        private readonly int failure;
        private readonly bool failFeeRefund;
        private readonly HttpClient client;

        public ControlledTransport(string clientName, int failure, bool failFeeRefund = false)
        {
            this.clientName = clientName;
            this.failure = failure;
            this.failFeeRefund = failFeeRefund;
            client = new HttpClient(this, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(15) };
            PlatformSecret = $"sk_test_{Convert.ToHexString(RandomNumberGenerator.GetBytes(24))}";
            Secrets = Substitute.For<ISecretResolver>();
            Secrets.ResolveAsync(SecretDefinitionRegistry.Keys.Stripe.PlatformSecretKey, null, Arg.Any<CancellationToken>())
                .Returns(SecretResolutionResult.Resolved(new ResolvedSecret(
                    SecretDefinitionRegistry.Keys.Stripe.PlatformSecretKey,
                    PlatformSecret, SecretSourceType.EnvironmentVariable,
                    SecretScope.Instance, null, DateTimeOffset.UtcNow)));
        }

        public string PlatformSecret { get; }
        public ISecretResolver Secrets { get; }
        public List<RecordedRequest> Requests { get; } = [];

        public HttpClient CreateClient(string name) => name == clientName
            ? client
            : throw new InvalidOperationException("Unexpected named HTTP client.");

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string path = request.RequestUri!.AbsolutePath;
            Requests.Add(new(request.Method, path,
                request.Headers.TryGetValues("Stripe-Account", out IEnumerable<string>? accounts) ? accounts.Single() : null,
                request.Headers.TryGetValues("Idempotency-Key", out IEnumerable<string>? keys) ? keys.Single() : null));
            if (request.Method == HttpMethod.Get)
            {
                return Task.FromResult(Json(HttpStatusCode.OK, path switch
                {
                    "/v1/payment_intents/pi_original" =>
                        """{"id":"pi_original","object":"payment_intent","status":"requires_action","amount":1250,"currency":"eur"}""",
                    "/v1/checkout/sessions/cs_original" =>
                        """{"id":"cs_original","object":"checkout.session","livemode":false,"status":"open","payment_status":"unpaid"}""",
                    "/v1/charges/ch_original" =>
                        """{"id":"ch_original","object":"charge","payment_intent":"pi_original","application_fee":"fee_original"}""",
                    "/v1/refunds/re_original" =>
                        """{"id":"re_original","object":"refund","payment_intent":"pi_original","charge":"ch_original","amount":500,"currency":"eur","livemode":false,"status":"succeeded"}""",
                    "/v1/application_fees/fee_original/refunds" =>
                        """{"object":"list","data":[],"has_more":false}""",
                    _ => throw new InvalidOperationException("Unexpected observation path.")
                }));
            }
            if (failFeeRefund && path == "/v1/refunds")
            {
                return Task.FromResult(Json(HttpStatusCode.OK,
                    """{"id":"re_original","object":"refund","payment_intent":"pi_original","charge":"ch_original","amount":500,"currency":"eur","livemode":false,"status":"succeeded"}"""));
            }
            return failure switch
            {
                0 => Task.FromException<HttpResponseMessage>(new OperationCanceledException(SensitiveMessage)),
                -1 => Task.FromException<HttpResponseMessage>(new HttpRequestException(SensitiveMessage)),
                _ => Task.FromResult(RetryableResponse((HttpStatusCode)failure))
            };
        }

        private static HttpResponseMessage RetryableResponse(HttpStatusCode status)
        {
            HttpResponseMessage response = Json(status,
                $$$"""{"error":{"type":"api_error","code":"api_error","message":"{{{SensitiveMessage}}}"}}""");
            response.Headers.Add("Stripe-Should-Retry", "true");
            return response;
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body)
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent(body) };
            response.Headers.Add("Request-Id", "req_controlled");
            return response;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                client.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
