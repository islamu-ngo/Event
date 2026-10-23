using System.Net;
using System.Security.Cryptography;
using Explore.Application.Contracts.Payments;
using Explore.Application.Contracts.Secrets;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Infrastructure.Payments.Stripe;
using Explore.Infrastructure.Payments.Stripe.Refunds;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Payments.Stripe;

public sealed class StripeRefundAdapterTests
{
    private static readonly Guid AttemptId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000101");
    private static readonly DateTime Now = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
    private static readonly string PlatformSecret = $"sk_test_{Convert.ToHexString(RandomNumberGenerator.GetBytes(24))}";

    [Test]
    public async Task CreateAsync_InsufficientBalancePendingUsesOriginalAccountStableIdempotencyAndPinnedPayment()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK,
            """{"id":"re_123","object":"refund","payment_intent":"pi_original","charge":"ch_original","amount":500,"currency":"eur","livemode":false,"status":"pending"}""",
            "req_create"));

        RefundProviderResult result = await Adapter(handler).CreateAsync(CreateRequest(), CancellationToken.None);

        RecordedRequest request = handler.Requests.Single();
        string body = WebUtility.UrlDecode(request.Body);
        await Assert.That(result.Outcome).IsEqualTo(RefundProviderOutcome.Observed);
        await Assert.That(result.Observation!.Status).IsEqualTo(RefundProviderStatus.Pending);
        await Assert.That(request.Method).IsEqualTo(HttpMethod.Post);
        await Assert.That(request.Path).IsEqualTo("/v1/refunds");
        await Assert.That(request.StripeAccount).IsEqualTo("acct_original");
        await Assert.That(request.IdempotencyKey).IsEqualTo("refund:stable");
        await Assert.That(body).Contains("payment_intent=pi_original");
        await Assert.That(body).Contains("amount=500");
        await Assert.That(body).Contains("refund_application_fee=false");
        await Assert.That(body).Contains($"metadata[islamu_refund_attempt_id]={AttemptId:D}");
    }

    [Test]
    [Arguments("pending", RefundProviderStatus.Pending)]
    [Arguments("requires_action", RefundProviderStatus.RequiresAction)]
    [Arguments("succeeded", RefundProviderStatus.Succeeded)]
    [Arguments("failed", RefundProviderStatus.Failed)]
    [Arguments("canceled", RefundProviderStatus.Cancelled)]
    public async Task CreateAsync_MapsOnlyAllowlistedProviderStatuses(string status, RefundProviderStatus expected)
    {
        var handler = new RecordingHandler(request => RefundLifecycleJson(request, status));

        RefundProviderResult result = await Adapter(handler).CreateAsync(CreateRequest(), CancellationToken.None);

        await Assert.That(result.Observation!.Status).IsEqualTo(expected);
    }

    [Test]
    public async Task CreateAsync_TimeoutAfterPossibleHandoffIsUnknownWithoutSdkRetry()
    {
        var handler = new RecordingHandler(_ => throw new OperationCanceledException());

        RefundProviderResult result = await Adapter(handler).CreateAsync(CreateRequest(), CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(RefundProviderOutcome.Unknown);
        await Assert.That(result.Failure!.ProviderHandoffStarted).IsTrue();
        await Assert.That(handler.Requests).Count().IsEqualTo(1);
        await Assert.That(handler.Requests.Select(request => request.IdempotencyKey!).Distinct()).IsEquivalentTo(["refund:stable"]);
    }

    [Test]
    public async Task RetrieveAsync_UsesOriginalConnectedAccountWithoutIdempotencyKey()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK,
            """{"id":"re_123","object":"refund","payment_intent":"pi_original","charge":"ch_original","amount":500,"currency":"eur","livemode":false,"status":"pending"}""",
            "req_get"));

        RefundProviderResult result = await Adapter(handler).RetrieveAsync(
            RefundRetrieveRequest.Create(
                "stripe", "acct_original", "pi_original", "re_123", "refund:stable", 500, "EUR", 38),
            CancellationToken.None);

        RecordedRequest request = handler.Requests.Single();
        await Assert.That(result.Observation!.Status).IsEqualTo(RefundProviderStatus.Pending);
        await Assert.That(request.Method).IsEqualTo(HttpMethod.Get);
        await Assert.That(request.Path).IsEqualTo("/v1/refunds/re_123");
        await Assert.That(request.StripeAccount).IsEqualTo("acct_original");
        await Assert.That(request.IdempotencyKey).IsNull();
    }

    [Test]
    public async Task CreateAsync_MissingSecretFailsBeforeProviderHandoff()
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("HTTP must not run."));
        var secrets = Substitute.For<ISecretResolver>();

        RefundProviderResult result = await Adapter(handler, secrets).CreateAsync(CreateRequest(), CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(RefundProviderOutcome.Failed);
        await Assert.That(result.Failure!.ProviderHandoffStarted).IsFalse();
        await Assert.That(handler.Requests).IsEmpty();
    }

    [Test]
    [Arguments(500L)]
    [Arguments(1_000L)]
    public async Task CreateAsync_SupportsPartialAndFullMinorUnitAmounts(long amountMinor)
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK,
            $$"""{"id":"re_amount","object":"refund","payment_intent":"pi_original","charge":"ch_original","amount":{{amountMinor}},"currency":"eur","livemode":false,"status":"pending"}""",
            "req_amount"));

        RefundProviderResult result = await Adapter(handler).CreateAsync(CreateRequest(amountMinor), CancellationToken.None);

        await Assert.That(result.Observation!.AmountMinor).IsEqualTo(amountMinor);
        await Assert.That(WebUtility.UrlDecode(handler.Requests.Single().Body)).Contains($"amount={amountMinor}");
    }

    [Test]
    public async Task CreateAsync_RateLimitIsAmbiguousAndRetainsStableIdempotency()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.TooManyRequests,
            """{"error":{"type":"api_error","code":"rate_limit"}}""", "req_rate"));

        RefundProviderResult result = await Adapter(handler).CreateAsync(CreateRequest(), CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(RefundProviderOutcome.Unknown);
        await Assert.That(result.Failure!.Code).IsEqualTo("refund_provider_rejected");
        await Assert.That(handler.Requests.Select(request => request.IdempotencyKey).Distinct())
            .IsEquivalentTo(new string?[] { "refund:stable" });
    }

    [Test]
    public async Task CreateAsync_AccountRestrictionFailsWithoutSdkOrProviderErrorLeakage()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.Forbidden,
            """{"error":{"type":"invalid_request_error","code":"account_invalid","message":"sensitive provider detail"}}""",
            "req_account"));

        RefundProviderResult result = await Adapter(handler).CreateAsync(CreateRequest(), CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(RefundProviderOutcome.Failed);
        await Assert.That(result.Failure!.Kind).IsEqualTo(RefundProviderFailureKind.Configuration);
        await Assert.That(result.Failure.Code).IsEqualTo("refund_provider_rejected");
        await Assert.That(result.Failure.Code).DoesNotContain("sensitive");
    }

    [Test]
    public async Task CreateAsync_TimeoutBeforeProviderClientCreationIsUnknownWithoutHandoff()
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("HTTP must not run."));
        var secrets = Substitute.For<ISecretResolver>();
        secrets.ResolveAsync(SecretDefinitionRegistry.Keys.Stripe.PlatformSecretKey, null, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<SecretResolutionResult>(new TimeoutException()));

        RefundProviderResult result = await Adapter(handler, secrets).CreateAsync(CreateRequest(), CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(RefundProviderOutcome.Unknown);
        await Assert.That(result.Failure!.ProviderHandoffStarted).IsFalse();
        await Assert.That(handler.Requests).IsEmpty();
    }

    [Test]
    public async Task OwnedFeeHandoff_DefinitiveFailureStillReturnsProvenBuyerRefundEvidence()
    {
        var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/v1/charges/ch_original" => Json(HttpStatusCode.OK,
                """{"id":"ch_original","object":"charge","payment_intent":"pi_original","application_fee":"fee_original"}""", "req_charge"),
            "/v1/application_fees/fee_original/refunds" when request.Method == HttpMethod.Get => Json(HttpStatusCode.OK,
                """{"object":"list","data":[],"has_more":false}""", "req_fee_list"),
            "/v1/application_fees/fee_original/refunds" => Json(HttpStatusCode.BadRequest,
                """{"error":{"type":"invalid_request_error","code":"fee_refund_invalid"}}""", "req_fee"),
            _ => Json(HttpStatusCode.OK,
                """{"id":"re_123","object":"refund","payment_intent":"pi_original","charge":"ch_original","amount":500,"currency":"eur","livemode":false,"status":"succeeded"}""",
                "req_refund")
        });

        RefundProviderResult result = await Adapter(handler).RetrieveAsync(FeeRequest(), CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(RefundProviderOutcome.Observed);
        await Assert.That(result.Observation!.Status).IsEqualTo(RefundProviderStatus.Succeeded);
        await Assert.That(result.Observation.ProviderRefundId).IsEqualTo("re_123");
        await Assert.That(result.Observation.ApplicationFeeRefundAmountMinor).IsNull();
        await Assert.That(result.Observation.ApplicationFeeRefundFailureCode)
            .IsEqualTo("refund_provider_fee_rejected");
    }

    [Test]
    public async Task BuyerHandoffDoesNotAlsoDispatchApplicationFee()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK,
            """{"id":"re_123","object":"refund","payment_intent":"pi_original","charge":"ch_original","amount":500,"currency":"eur","status":"succeeded"}""",
            "req_buyer"));
        RefundProviderResult result = await Adapter(handler).CreateAsync(CreateRequest(), CancellationToken.None);
        await Assert.That(result.Observation!.Status).IsEqualTo(RefundProviderStatus.Succeeded);
        await Assert.That(result.Observation.ApplicationFeeRefundAmountMinor).IsNull();
        await Assert.That(handler.Requests.Count).IsEqualTo(1);
        await Assert.That(handler.Requests.Single().Path).IsEqualTo("/v1/refunds");
    }

    [Test]
    public async Task ExpiredMutationWindowFailsBeforeAnyBuyerHandoff()
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("No provider request is permitted."));
        RefundProviderResult result = await Adapter(handler).CreateAsync(
            CreateRequest() with { MutationExpiresAt = Now }, CancellationToken.None);
        await Assert.That(result.Failure!.Code).IsEqualTo("refund_provider_retention_expired");
        await Assert.That(result.Failure.ProviderHandoffStarted).IsFalse();
        await Assert.That(handler.Requests).IsEmpty();
    }

    [Test]
    public async Task LostBuyerAcknowledgementUsesNativeMetadataObservationWithoutMutation()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK,
            $$$"""{"object":"list","data":[{"id":"re_unrelated","object":"refund","payment_intent":"pi_original","amount":500,"currency":"eur","status":"pending","metadata":{"islamu_refund_attempt_id":"another"}},{"id":"re_recovered","object":"refund","payment_intent":"pi_original","amount":500,"currency":"eur","status":"pending","metadata":{"islamu_refund_attempt_id":"{{{AttemptId:D}}}"}}],"has_more":false}""",
            "req_recovery"));
        RefundProviderResult result = await Adapter(handler).RetrieveAsync(
            RefundRetrieveRequest.Create("stripe", "acct_original", "pi_original", string.Empty, "refund:stable", 500, "EUR", 38)
                with
            { RefundAttemptId = AttemptId, MutationExpiresAt = Now.AddHours(23) },
            CancellationToken.None);
        await Assert.That(result.Observation!.ProviderRefundId).IsEqualTo("re_recovered");
        await Assert.That(handler.Requests.Single().Method).IsEqualTo(HttpMethod.Get);
        await Assert.That(handler.Requests.Single().StripeAccount).IsEqualTo("acct_original");
        await Assert.That(handler.Requests.Single().IdempotencyKey).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task FeeRecoveryCannotMutateWithoutFreshOwnershipEvenWhenProviderListIsEmpty(bool expired)
    {
        var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/v1/charges/ch_original" => Json(HttpStatusCode.OK,
                """{"id":"ch_original","object":"charge","payment_intent":"pi_original","application_fee":"fee_original"}""", "req_charge"),
            "/v1/application_fees/fee_original/refunds" => Json(HttpStatusCode.OK,
                """{"object":"list","data":[],"has_more":false}""", "req_list"),
            _ => Json(HttpStatusCode.OK,
                """{"id":"re_123","object":"refund","payment_intent":"pi_original","charge":"ch_original","amount":500,"currency":"eur","status":"succeeded"}""", "req_refund")
        });
        RefundProviderResult result = await Adapter(handler).RetrieveAsync(
            FeeRequest() with { AllowApplicationFeeMutation = false, MutationExpiresAt = expired ? Now : Now.AddHours(23) },
            CancellationToken.None);
        await Assert.That(handler.Requests.All(request => request.Method == HttpMethod.Get)).IsTrue();
        await Assert.That(result.Observation!.Status).IsEqualTo(RefundProviderStatus.Succeeded);
        await Assert.That(result.Observation.ApplicationFeeRefundAmountMinor).IsNull();
        await Assert.That(result.Observation.ApplicationFeeRefundFailureCode)
            .IsEqualTo(expired ? "refund_provider_retention_expired" : null);
    }

    [Test]
    public async Task ExistingFeeEvidenceRecoversAfterRetentionWithoutRepeatingMutation()
    {
        var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/v1/charges/ch_original" => Json(HttpStatusCode.OK,
                """{"id":"ch_original","object":"charge","payment_intent":"pi_original","application_fee":"fee_original"}""", "req_charge"),
            "/v1/application_fees/fee_original/refunds" => Json(HttpStatusCode.OK,
                """{"object":"list","data":[{"id":"fr_original","object":"fee_refund","amount":38,"currency":"eur","fee":"fee_original","metadata":{"islamu_refund_id":"re_123"}}],"has_more":false}""", "req_list"),
            _ => Json(HttpStatusCode.OK,
                """{"id":"re_123","object":"refund","payment_intent":"pi_original","charge":"ch_original","amount":500,"currency":"eur","status":"succeeded"}""", "req_refund")
        });
        RefundProviderResult result = await Adapter(handler).RetrieveAsync(
            FeeRequest() with { AllowApplicationFeeMutation = false, MutationExpiresAt = Now }, CancellationToken.None);
        await Assert.That(result.Observation!.ApplicationFeeRefundAmountMinor).IsEqualTo(38L);
        await Assert.That(result.Observation.ApplicationFeeRefundFailureCode).IsNull();
        await Assert.That(handler.Requests.All(request => request.Method == HttpMethod.Get)).IsTrue();
    }

    [Test]
    public async Task WrongReturnedRefundIdentityCannotReachApplicationFeeMutation()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK,
            """{"id":"re_someone_else","object":"refund","payment_intent":"pi_original","charge":"ch_original","amount":500,"currency":"eur","status":"succeeded"}""",
            "req_wrong_identity"));
        RefundProviderResult result = await Adapter(handler).RetrieveAsync(FeeRequest(), CancellationToken.None);
        await Assert.That(result.Outcome).IsEqualTo(RefundProviderOutcome.Unknown);
        await Assert.That(result.Failure!.Code).IsEqualTo("refund_provider_identity_mismatch");
        await Assert.That(handler.Requests.Single().Method).IsEqualTo(HttpMethod.Get);
    }

    private static RefundRetrieveRequest FeeRequest() =>
        RefundRetrieveRequest.Create("stripe", "acct_original", "pi_original", "re_123", "refund:stable", 500, "EUR", 38)
            with
        { RefundAttemptId = AttemptId, AllowApplicationFeeMutation = true, MutationExpiresAt = Now.AddHours(23) };

    private static RefundCreateRequest CreateRequest(long amountMinor = 500) => RefundCreateRequest.Create(
        AttemptId, "stripe", "acct_original", "pi_original", "refund:stable", amountMinor, "EUR",
        applicationFeeRefundAmountMinor: 38) with
    { MutationExpiresAt = Now.AddHours(23) };

    private static StripeRefundAdapter Adapter(RecordingHandler handler, ISecretResolver? secrets = null)
    {
        bool useDefaultSecret = secrets is null;
        secrets ??= Substitute.For<ISecretResolver>();
        if (useDefaultSecret)
        {
            secrets.ResolveAsync(SecretDefinitionRegistry.Keys.Stripe.PlatformSecretKey, null, Arg.Any<CancellationToken>()).Returns(SecretResolutionResult.Resolved(new ResolvedSecret(SecretDefinitionRegistry.Keys.Stripe.PlatformSecretKey,
            PlatformSecret,
            SecretSourceType.EnvironmentVariable,
            SecretScope.Instance,
            null,
            DateTimeOffset.UtcNow)));
        }
        return new(
            new SingleClientFactory(new HttpClient(handler) { BaseAddress = new Uri("https://api.stripe.example.test") }),
            secrets,
            Options.Create(new StripePaymentOptions()), new FixedClock());
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body, string requestId)
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent(body) };
        response.Headers.Add("Request-Id", requestId);
        return response;
    }

    private static HttpResponseMessage RefundLifecycleJson(HttpRequestMessage request, string status) =>
        request.RequestUri!.AbsolutePath switch
        {
            "/v1/charges/ch_original" => Json(HttpStatusCode.OK,
                """{"id":"ch_original","object":"charge","payment_intent":"pi_original","application_fee":"fee_original"}""", "req_charge"),
            "/v1/application_fees/fee_original/refunds" => Json(HttpStatusCode.OK,
                """{"id":"fr_123","object":"fee_refund","amount":38,"currency":"eur","fee":"fee_original"}""", "req_fee"),
            _ => Json(HttpStatusCode.OK,
                $$"""{"id":"re_status","object":"refund","payment_intent":"pi_original","charge":"ch_original","amount":500,"currency":"eur","livemode":false,"status":"{{status}}"}""",
                "req_status")
        };

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
        public HttpClient CreateClient(string name) => name == StripeRefundAdapter.HttpClientName
            ? client
            : throw new InvalidOperationException("Unexpected HttpClient name.");
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(Now);
    }
}
