using System.Net;
using Explore.Application.Contracts.Payments;
using Explore.Application.Contracts.Secrets;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Infrastructure.Configuration;
using Explore.Infrastructure.Payments.Stripe;
using Explore.Infrastructure.Payments.Stripe.Checkout;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Payments.Stripe;

public sealed class StripeCheckoutAdapterTests
{
    private static readonly DateTime UtcNow = new(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid AttemptId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000101");
    private static readonly Guid OrderId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000102");

    [Test]
    public async Task ConfigureInfrastructureServices_RegistersHardenedNamedCheckoutClient()
    {
        var services = new ServiceCollection();
        services.ConfigureInfrastructureServices(new ConfigurationBuilder().Build());
        using ServiceProvider provider = services.BuildServiceProvider();

        HttpClient client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(StripeCheckoutAdapter.HttpClientName);

        await Assert.That(client.Timeout).IsEqualTo(TimeSpan.FromSeconds(15));
    }

    [Test]
    public async Task StripePaymentOptionsValidator_NormalizesTrimmedHostsAndRejectsNullEmptyOrInvalidCleanly()
    {
        var validator = new StripePaymentOptionsValidator();

        ValidateOptionsResult normalized = validator.Validate(null, new StripePaymentOptions
        {
            AllowedCheckoutHosts = [" checkout.stripe.example.test ", "CHECKOUT.STRIPE.EXAMPLE.TEST"]
        });
        ValidateOptionsResult missing = validator.Validate(null, new StripePaymentOptions { AllowedCheckoutHosts = null });
        ValidateOptionsResult empty = validator.Validate(null, new StripePaymentOptions { AllowedCheckoutHosts = [] });
        ValidateOptionsResult invalid = validator.Validate(null, new StripePaymentOptions { AllowedCheckoutHosts = ["*.stripe.com"] });

        await Assert.That(normalized.Succeeded).IsTrue();
        await Assert.That(missing.Failed).IsTrue();
        await Assert.That(empty.Failed).IsTrue();
        await Assert.That(invalid.Failed).IsTrue();
    }

    [Test]
    public async Task Descriptor_UsesPinnedStripeRuntimeApiRevisionAndOrganizerDirectProfile()
    {
        PaymentProviderDescriptor descriptor = Adapter(new RecordingHandler(_ => throw new InvalidOperationException())).Describe();

        await Assert.That(descriptor.ProviderCode).IsEqualTo("stripe");
        await Assert.That(descriptor.ProfileCode).IsEqualTo("OrganizerDirect");
        await Assert.That(descriptor.ApiRevision).IsEqualTo("2026-07-29.dahlia");
        await Assert.That(descriptor.ApiRevision).IsEqualTo(global::Stripe.StripeConfiguration.ApiVersion);
    }

    [Test]
    public async Task CreateAsync_UsesConnectedAccountStableIdempotencyAndExactImmutableComposition()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, """
            {"id":"cs_test_123","object":"checkout.session","livemode":false,"url":"https://checkout.stripe.example.test/c/pay/cs_test_123","status":"open","payment_status":"unpaid","expires_at":1787227200}
            """, "req_create"));
        StripeCheckoutAdapter adapter = Adapter(handler);

        HostedCheckoutCreateResult result = await adapter.CreateAsync(CreateRequest(), CancellationToken.None);

        RecordedRequest request = handler.Requests.Single();
        string body = WebUtility.UrlDecode(request.Body);
        await Assert.That(result.Outcome).IsEqualTo(HostedCheckoutOperationOutcome.Succeeded);
        await Assert.That(result.Session!.SessionId).IsEqualTo("cs_test_123");
        await Assert.That(result.Session.Status).IsEqualTo(HostedCheckoutSessionStatus.Open);
        await Assert.That(result.Session.PaymentStatus).IsEqualTo(HostedCheckoutPaymentStatus.Unpaid);
        await Assert.That(result.ProviderRequestId).IsEqualTo("req_create");
        await Assert.That(request.Method).IsEqualTo(HttpMethod.Post);
        await Assert.That(request.Path).IsEqualTo("/v1/checkout/sessions");
        await Assert.That(request.StripeAccount).IsEqualTo("acct_123");
        await Assert.That(request.IdempotencyKey).IsEqualTo("checkout:stable");
        await Assert.That(body).Contains("mode=payment");
        await Assert.That(body).Contains("line_items[0][price_data][currency]=eur");
        await Assert.That(body).Contains("line_items[0][price_data][unit_amount]=1250");
        await Assert.That(body).Contains("line_items[0][quantity]=1");
        await Assert.That(body).Contains("payment_intent_data[application_fee_amount]=250");
        await Assert.That(body).Contains("success_url=https://events.example.test/checkout/success");
        await Assert.That(body).Contains("cancel_url=https://events.example.test/checkout/cancel");
        await Assert.That(body).Contains($"expires_at={new DateTimeOffset(UtcNow.AddMinutes(30)).ToUnixTimeSeconds()}");
        await Assert.That(body).Contains($"metadata[islamu_payment_attempt_id]={AttemptId:D}");
        await Assert.That(body).Contains($"metadata[islamu_registration_order_id]={OrderId:D}");
        await Assert.That(body).DoesNotContain("email");
        await Assert.That(body).DoesNotContain("customer");
        await Assert.That(body).DoesNotContain("card");
        await Assert.That(body).DoesNotContain("adaptive");
        await Assert.That(global::Stripe.StripeConfiguration.ApiKey).IsNull();
    }

    [Test]
    public async Task CreateAsync_PreservesConfiguredApplicationSubpathInReturnUrls()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, """
            {"id":"cs_subpath","object":"checkout.session","livemode":false,"url":"https://checkout.stripe.example.test/c/pay/cs_subpath","status":"open","payment_status":"unpaid","expires_at":1787227200}
            """, "req_subpath"));

        await Adapter(handler).CreateAsync(CreateRequest("/events"), CancellationToken.None);

        string body = WebUtility.UrlDecode(handler.Requests.Single().Body);
        await Assert.That(body).Contains("success_url=https://events.example.test/events/checkout/success");
        await Assert.That(body).Contains("cancel_url=https://events.example.test/events/checkout/cancel");
    }

    [Test]
    public async Task RetrieveAsync_UsesConnectedAccountWithoutMutationIdempotencyAndMapsProviderState()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, """
            {"id":"cs_test_123","object":"checkout.session","livemode":false,"status":"complete","payment_status":"paid","payment_intent":"pi_123","amount_total":9999,"currency":"usd"}
            """, "req_retrieve"));
        StripeCheckoutAdapter adapter = Adapter(handler);

        HostedCheckoutRetrieveResult result = await adapter.RetrieveAsync(
            HostedCheckoutRetrieveRequest.Create("stripe", "acct_123", "cs_test_123"),
            CancellationToken.None);

        RecordedRequest request = handler.Requests.Single();
        await Assert.That(result.Outcome).IsEqualTo(HostedCheckoutOperationOutcome.Succeeded);
        await Assert.That(result.Session!.Status).IsEqualTo(HostedCheckoutSessionStatus.Complete);
        await Assert.That(result.Session.PaymentStatus).IsEqualTo(HostedCheckoutPaymentStatus.Paid);
        await Assert.That(result.Session.PaymentId).IsEqualTo("pi_123");
        await Assert.That(result.Session.AmountTotalMinor).IsEqualTo(9999);
        await Assert.That(result.Session.CurrencyCode).IsEqualTo("USD");
        await Assert.That(result.ProviderRequestId).IsEqualTo("req_retrieve");
        await Assert.That(request.Method).IsEqualTo(HttpMethod.Get);
        await Assert.That(request.Path).IsEqualTo("/v1/checkout/sessions/cs_test_123");
        await Assert.That(request.StripeAccount).IsEqualTo("acct_123");
        await Assert.That(request.IdempotencyKey).IsNull();
    }

    [Test]
    public async Task CancelAsync_SucceededPaymentReturnsCapturedWithoutMutation()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, """
            {"id":"pi_captured","object":"payment_intent","amount":1000,"currency":"eur","status":"succeeded"}
            """, "req_captured"));

        PaymentCancellationProviderResult result = await Adapter(handler).CancelAsync(
            PaymentCancellationRequest.Create("stripe", "acct_original", null, "pi_captured", "cancel:stable"),
            CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(PaymentCancellationProviderOutcome.Captured);
        await Assert.That(handler.Requests).Count().IsEqualTo(1);
        await Assert.That(handler.Requests.Single().Method).IsEqualTo(HttpMethod.Get);
        await Assert.That(handler.Requests.Single().StripeAccount).IsEqualTo("acct_original");
    }

    [Test]
    public async Task CancelAsync_UncapturedPaymentUsesOriginalAccountAndStableIdempotency()
    {
        var handler = new RecordingHandler(request => request.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, """
                {"id":"pi_cancel","object":"payment_intent","amount":1000,"currency":"eur","status":"requires_action"}
                """, "req_get_cancel")
            : Json(HttpStatusCode.OK, """
                {"id":"pi_cancel","object":"payment_intent","amount":1000,"currency":"eur","status":"canceled"}
                """, "req_cancel"));

        PaymentCancellationProviderResult result = await Adapter(handler).CancelAsync(
            PaymentCancellationRequest.Create("stripe", "acct_original", null, "pi_cancel", "cancel:stable"),
            CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(PaymentCancellationProviderOutcome.Cancelled);
        await Assert.That(handler.Requests).Count().IsEqualTo(2);
        RecordedRequest mutation = handler.Requests.Single(request => request.Method == HttpMethod.Post);
        await Assert.That(mutation.StripeAccount).IsEqualTo("acct_original");
        await Assert.That(mutation.IdempotencyKey).IsEqualTo("cancel:stable");
    }

    [Test]
    public async Task CreateAsync_TimeoutAfterHandoffReturnsUnknownWithoutApplicationRetry()
    {
        var handler = new RecordingHandler(_ => throw new OperationCanceledException());

        HostedCheckoutCreateResult result = await Adapter(handler).CreateAsync(CreateRequest(), CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(HostedCheckoutOperationOutcome.Unknown);
        await Assert.That(result.Failure!.Kind).IsEqualTo(HostedCheckoutFailureKind.Network);
        await Assert.That(handler.Requests).Count().IsEqualTo(3);
    }

    [Test]
    public async Task CreateAsync_DeterministicBadRequestReturnsBoundedFailureWithoutRawMessageOrRetry()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.BadRequest, """
            {"error":{"type":"invalid_request_error","code":"currency_not_supported","message":"raw cardholder message"}}
            """, "req_bad"));

        HostedCheckoutCreateResult result = await Adapter(handler).CreateAsync(CreateRequest(), CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(HostedCheckoutOperationOutcome.Failed);
        await Assert.That(result.Failure!.Type).IsEqualTo("invalid_request_error");
        await Assert.That(result.Failure.ProviderCode).IsEqualTo("currency_not_supported");
        await Assert.That(result.Failure.HttpStatusCode).IsEqualTo(400);
        await Assert.That(result.Failure.ProviderRequestId).IsEqualTo("req_bad");
        await Assert.That(result.Failure.Code).DoesNotContain("raw cardholder message");
        await Assert.That(handler.Requests).Count().IsEqualTo(1);
    }

    [Test]
    [Arguments(HttpStatusCode.RequestTimeout)]
    [Arguments(HttpStatusCode.Conflict)]
    [Arguments(HttpStatusCode.TooManyRequests)]
    [Arguments(HttpStatusCode.InternalServerError)]
    public async Task CreateAsync_AmbiguousHttpStatusesAlwaysReturnUnknown(HttpStatusCode statusCode)
    {
        var handler = new RecordingHandler(_ => Json(statusCode, """
            {"error":{"type":"api_error","code":"ambiguous"}}
            """, "req_ambiguous"));

        HostedCheckoutCreateResult result = await Adapter(handler).CreateAsync(CreateRequest(), CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(HostedCheckoutOperationOutcome.Unknown);
        await Assert.That(result.Failure!.Kind).IsEqualTo(HostedCheckoutFailureKind.ProviderUnknown);
    }

    [Test]
    [Arguments(0, HostedCheckoutFailureKind.ProviderUnknown)]
    [Arguments(399, HostedCheckoutFailureKind.ProviderUnknown)]
    [Arguments(400, HostedCheckoutFailureKind.ProviderRejected)]
    [Arguments(401, HostedCheckoutFailureKind.Configuration)]
    [Arguments(403, HostedCheckoutFailureKind.Configuration)]
    [Arguments(408, HostedCheckoutFailureKind.ProviderUnknown)]
    [Arguments(409, HostedCheckoutFailureKind.ProviderUnknown)]
    [Arguments(429, HostedCheckoutFailureKind.ProviderUnknown)]
    [Arguments(499, HostedCheckoutFailureKind.ProviderRejected)]
    [Arguments(500, HostedCheckoutFailureKind.ProviderUnknown)]
    [Arguments(600, HostedCheckoutFailureKind.ProviderUnknown)]
    public async Task MapFailureKind_RejectsOnlyProvenDeterministic4xx(
        int statusCode,
        HostedCheckoutFailureKind expected)
    {
        await Assert.That(StripeCheckoutAdapter.MapFailureKind((HttpStatusCode)statusCode)).IsEqualTo(expected);
    }

    [Test]
    public async Task CreateAsync_CancellationBeforeHandoffPropagatesWithoutNetwork()
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("HTTP must not run."));
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Assert.That(async () => await Adapter(handler).CreateAsync(CreateRequest(), source.Token))
            .Throws<OperationCanceledException>();
        await Assert.That(handler.Requests).IsEmpty();
    }

    [Test]
    public async Task CreateAsync_MissingSecretReturnsExplicitPreHandoffFailureWithoutNetwork()
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("HTTP must not run."));
        ISecretResolver secrets = Substitute.For<ISecretResolver>();

        HostedCheckoutCreateResult result = await Adapter(handler, secrets).CreateAsync(CreateRequest(), CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(HostedCheckoutOperationOutcome.Failed);
        await Assert.That(result.Failure!.Kind).IsEqualTo(HostedCheckoutFailureKind.Configuration);
        await Assert.That(result.Failure.ProviderHandoffStarted).IsFalse();
        await Assert.That(result.Failure.PreHandoffDisposition).IsEqualTo(HostedCheckoutPreHandoffDisposition.Transient);
        await Assert.That(handler.Requests).IsEmpty();
    }

    [Test]
    public async Task CreateAsync_UnsupportedProviderIsPermanentPreHandoffFailure()
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("HTTP must not run."));
        HostedCheckoutCreateRequest request = HostedCheckoutCreateRequest.Create(
            AttemptId, OrderId, "other", "acct_123", "checkout:stable", "EUR", 1250, 250,
            UtcNow.AddMinutes(30),
            new Uri("https://events.example.test"),
            new Uri("https://events.example.test/success"),
            new Uri("https://events.example.test/cancel"));

        HostedCheckoutCreateResult result = await Adapter(handler).CreateAsync(request, CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(HostedCheckoutOperationOutcome.Failed);
        await Assert.That(result.Failure!.PreHandoffDisposition).IsEqualTo(HostedCheckoutPreHandoffDisposition.Permanent);
        await Assert.That(handler.Requests).IsEmpty();
    }

    [Test]
    public async Task CreateAsync_SecretModeMismatchIsPermanentPreHandoffFailure()
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("HTTP must not run."));
        ISecretResolver secrets = Substitute.For<ISecretResolver>();
        secrets.ResolveAsync(SecretDefinitionRegistry.Keys.Stripe.PlatformSecretKey, null, Arg.Any<CancellationToken>()).Returns(SecretResolutionResult.Resolved(new ResolvedSecret(SecretDefinitionRegistry.Keys.Stripe.PlatformSecretKey,
        "sk_live_platform",
        SecretSourceType.EnvironmentVariable,
        SecretScope.Instance,
        null,
        DateTimeOffset.UtcNow)));

        HostedCheckoutCreateResult result = await Adapter(handler, secrets).CreateAsync(CreateRequest(), CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(HostedCheckoutOperationOutcome.Failed);
        await Assert.That(result.Failure!.PreHandoffDisposition).IsEqualTo(HostedCheckoutPreHandoffDisposition.Permanent);
        await Assert.That(handler.Requests).IsEmpty();
    }

    [Test]
    public async Task CreateAsync_CancellationDuringHandoffReturnsUnknownForReconciliation()
    {
        using var source = new CancellationTokenSource();
        var handler = new RecordingHandler(_ =>
        {
            source.Cancel();
            throw new OperationCanceledException(source.Token);
        });

        HostedCheckoutCreateResult result = await Adapter(handler).CreateAsync(CreateRequest(), source.Token);

        await Assert.That(result.Outcome).IsEqualTo(HostedCheckoutOperationOutcome.Unknown);
        await Assert.That(handler.Requests).IsNotEmpty();
    }

    [Test]
    public async Task RetrievePaymentIntentAsync_MapsAuthoritativeMoneyFeeStatusAndRequestIdOnConnectedAccount()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, """
            {"id":"pi_123","object":"payment_intent","livemode":false,"amount":7777,"currency":"gbp","application_fee_amount":333,"status":"requires_action"}
            """, "req_pi"));

        PaymentIntentRetrieveResult result = await Adapter(handler).RetrievePaymentIntentAsync(
            PaymentIntentRetrieveRequest.Create("stripe", "acct_123", "pi_123"),
            CancellationToken.None);

        RecordedRequest request = handler.Requests.Single();
        await Assert.That(result.Outcome).IsEqualTo(HostedCheckoutOperationOutcome.Succeeded);
        await Assert.That(result.PaymentIntent!.AmountMinor).IsEqualTo(7777);
        await Assert.That(result.PaymentIntent.CurrencyCode).IsEqualTo("GBP");
        await Assert.That(result.PaymentIntent.ApplicationFeeMinor).IsEqualTo(333);
        await Assert.That(result.PaymentIntent.Status).IsEqualTo(PaymentIntentStatus.RequiresAction);
        await Assert.That(result.ProviderRequestId).IsEqualTo("req_pi");
        await Assert.That(request.Path).IsEqualTo("/v1/payment_intents/pi_123");
        await Assert.That(request.StripeAccount).IsEqualTo("acct_123");
        await Assert.That(request.IdempotencyKey).IsNull();
    }

    [Test]
    [Arguments("https://evil.example.test/c/pay/cs_test", false)]
    [Arguments("https://checkout.stripe.example.test:444/c/pay/cs_test", false)]
    [Arguments("https://user@checkout.stripe.example.test/c/pay/cs_test", false)]
    [Arguments("https://checkout.stripe.example.test/c/pay/cs_test#fragment", false)]
    [Arguments("https://checkout.strípe.example.test/c/pay/cs_test", false)]
    [Arguments("https://checkout.stripe.example.test/c/pay/cs_test", true)]
    public async Task CreateAsync_AcceptsOnlyExactConfiguredHttpsCheckoutHost(string hostedUrl, bool succeeds)
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, $$"""
            {"id":"cs_test_123","object":"checkout.session","livemode":false,"url":"{{hostedUrl}}","status":"open","payment_status":"unpaid"}
            """, "req_host"));

        HostedCheckoutCreateResult result = await Adapter(handler).CreateAsync(CreateRequest(), CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(succeeds
            ? HostedCheckoutOperationOutcome.Succeeded
            : HostedCheckoutOperationOutcome.Unknown);
    }

    [Test]
    public async Task CreateAsync_UsesCanonicalTrimmedCheckoutHostSet()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, """
            {"id":"cs_test_123","object":"checkout.session","livemode":false,"url":"https://checkout.stripe.example.test/c/pay/cs_test","status":"open","payment_status":"unpaid"}
            """, "req_host"));
        var paymentOptions = new StripePaymentOptions
        {
            AllowedCheckoutHosts = [" CHECKOUT.STRIPE.EXAMPLE.TEST "]
        };

        HostedCheckoutCreateResult result = await Adapter(handler, paymentOptions: paymentOptions)
            .CreateAsync(CreateRequest(), CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(HostedCheckoutOperationOutcome.Succeeded);
    }

    private static HostedCheckoutCreateRequest CreateRequest(string pathBase = "") => HostedCheckoutCreateRequest.Create(
        AttemptId,
        OrderId,
        "stripe",
        "acct_123",
        "checkout:stable",
        "EUR",
        12_50,
        2_50,
        UtcNow.AddMinutes(30),
        new Uri($"https://events.example.test{pathBase}/"),
        new Uri($"https://events.example.test{pathBase}/checkout/success"),
        new Uri($"https://events.example.test{pathBase}/checkout/cancel"));

    private static StripeCheckoutAdapter Adapter(
        RecordingHandler handler,
        ISecretResolver? secrets = null,
        StripePaymentOptions? paymentOptions = null)
    {
        global::Stripe.StripeConfiguration.ApiKey = null;
        bool useDefaultSecret = secrets is null;
        secrets ??= Substitute.For<ISecretResolver>();
        if (useDefaultSecret)
        {
            secrets.ResolveAsync(SecretDefinitionRegistry.Keys.Stripe.PlatformSecretKey, null, Arg.Any<CancellationToken>()).Returns(SecretResolutionResult.Resolved(new ResolvedSecret(SecretDefinitionRegistry.Keys.Stripe.PlatformSecretKey,
            "sk_test_platform",
            SecretSourceType.EnvironmentVariable,
            SecretScope.Instance,
            null,
            DateTimeOffset.UtcNow)));
        }
        return new StripeCheckoutAdapter(
            new SingleClientFactory(new HttpClient(handler) { BaseAddress = new Uri("https://api.stripe.example.test") }),
            secrets,
            Options.Create(paymentOptions ?? new StripePaymentOptions { AllowedCheckoutHosts = ["checkout.stripe.example.test"] }));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body, string requestId)
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent(body) };
        response.Headers.Add("Request-Id", requestId);
        return response;
    }

    private sealed record RecordedRequest(HttpMethod Method, string Path, string Body, string? StripeAccount, string? IdempotencyKey);

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new(
                request.Method,
                request.RequestUri!.AbsolutePath,
                body,
                request.Headers.TryGetValues("Stripe-Account", out IEnumerable<string>? accounts) ? accounts.Single() : null,
                request.Headers.TryGetValues("Idempotency-Key", out IEnumerable<string>? keys) ? keys.Single() : null));
            return responseFactory(request);
        }
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => name == StripeCheckoutAdapter.HttpClientName
            ? client
            : throw new InvalidOperationException("Unexpected HttpClient name.");
    }
}
