using System.Net;
using System.Text;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.Webhooks;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Infrastructure.Configuration;
using Explore.Infrastructure.Payments.Stripe;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Payments.Stripe;

public sealed class StripeConnectAccountAdapterTests
{
    private static readonly Guid TenantId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000001");
    private static readonly Guid OrganizerActorId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000020");
    private static readonly DateTime UtcNow = new(2026, 8, 14, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    [Arguments(null, true)]
    [Arguments("Test", true)]
    [Arguments("Live", true)]
    [Arguments("test", false)]
    [Arguments("", false)]
    [Arguments("Production", false)]
    public async Task StripePaymentOptionsValidator_AcceptsOnlyExactTestOrLive(string? mode, bool valid)
    {
        var options = new StripePaymentOptions();
        if (mode is not null)
        {
            options.Mode = mode;
        }

        ValidateOptionsResult result = new StripePaymentOptionsValidator().Validate(null, options);

        await Assert.That(result.Succeeded).IsEqualTo(valid);
    }

    [Test]
    public async Task ConfigureInfrastructureServices_BindsStripeModeAndRegistersStartupValidator()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Payments:Stripe:Mode"] = StripePaymentOptions.LiveMode
            })
            .Build();
        var services = new ServiceCollection();

        services.ConfigureInfrastructureServices(configuration);
        ServiceDescriptor validator = services.Single(descriptor =>
            descriptor.ServiceType == typeof(IValidateOptions<StripePaymentOptions>)
            && descriptor.ImplementationType == typeof(StripePaymentOptionsValidator));
        using ServiceProvider provider = services.BuildServiceProvider();

        await Assert.That(validator.Lifetime).IsEqualTo(ServiceLifetime.Singleton);
        await Assert.That(provider.GetRequiredService<IOptions<StripePaymentOptions>>().Value.Mode)
            .IsEqualTo(StripePaymentOptions.LiveMode);
    }

    [Test]
    public async Task CreateAccountAsync_PostsExpressAccountWithRequestedCardPaymentsTransfersAndStableIdempotency()
    {
        var handler = new RecordingStripeHandler(_ => Json(HttpStatusCode.OK, "{\"id\":\"acct_123\",\"object\":\"account\",\"livemode\":false}"));
        StripeConnectAccountAdapter adapter = Adapter(handler);
        var request = AccountRequest();

        OrganizerPaymentProviderAccountCreationResult first = await adapter.CreateAccountAsync(request, CancellationToken.None);
        OrganizerPaymentProviderAccountCreationResult second = await adapter.CreateAccountAsync(request, CancellationToken.None);

        await Assert.That(first.Status).IsEqualTo(OrganizerPaymentProviderAccountCreationStatus.Created);
        await Assert.That(first.ExternalAccountId).IsEqualTo("acct_123");
        await Assert.That(handler.Requests).Count().IsEqualTo(2);
        await Assert.That(handler.Requests[0].Method).IsEqualTo(HttpMethod.Post);
        await Assert.That(handler.Requests[0].RequestUri!.AbsolutePath).IsEqualTo("/v1/accounts");
        await Assert.That(handler.Bodies[0]).DoesNotContain("type=express");
        await Assert.That(handler.Bodies[0]).DoesNotContain("&type=");
        await Assert.That(handler.Bodies[0]).Contains("capabilities[card_payments][requested]=true");
        await Assert.That(handler.Bodies[0]).Contains("capabilities[transfers][requested]=true");
        await Assert.That(handler.Bodies[0]).Contains("controller[fees][payer]=application");
        await Assert.That(handler.Bodies[0]).Contains("controller[losses][payments]=application");
        await Assert.That(handler.Bodies[0]).Contains("controller[stripe_dashboard][type]=express");
        await Assert.That(handler.Bodies[0]).DoesNotContain("treasury");
        await Assert.That(handler.Requests[0].Headers.Authorization!.Scheme).IsEqualTo("Bearer");
        await Assert.That(handler.Requests[0].Headers.Authorization!.Parameter).IsEqualTo("sk_test_platform");
        await Assert.That(handler.IdempotencyKeys[0]).IsEqualTo("organizer-payment-account-018e4e5c7f0070008000000000000099");
        await Assert.That(handler.IdempotencyKeys[0]).IsEqualTo(handler.IdempotencyKeys[1]);
        await Assert.That(second.ExternalAccountId).IsEqualTo("acct_123");
        await Assert.That(global::Stripe.StripeConfiguration.ApiKey).IsNull();
    }

    [Test]
    public async Task CreateOnboardingLinkAsync_PostsAccountOnboardingUrlsForExternalAccount()
    {
        var handler = new RecordingStripeHandler(_ => Json(HttpStatusCode.OK, "{\"object\":\"account_link\",\"url\":\"https://connect.stripe.example.test/setup/acct_123\"}"));
        StripeConnectAccountAdapter adapter = Adapter(handler);

        OrganizerPaymentOnboardingLinkCreationResult result = await adapter.CreateOnboardingLinkAsync(
            new OrganizerPaymentOnboardingLinkRequest(
                "stripe",
                "platform-live-eu",
                "acct_123",
                new Uri("https://app.example.test/payment/return"),
                new Uri("https://app.example.test/payment/refresh"),
                OrganizerPaymentOnboardingType.AccountOnboarding),
            CancellationToken.None);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.OnboardingUrl).IsEqualTo(new Uri("https://connect.stripe.example.test/setup/acct_123"));
        await Assert.That(handler.Requests.Single().RequestUri!.AbsolutePath).IsEqualTo("/v1/account_links");
        await Assert.That(handler.Bodies.Single()).Contains("account=acct_123");
        await Assert.That(handler.Bodies.Single()).Contains("type=account_onboarding");
        await Assert.That(handler.Bodies.Single()).Contains("return_url=https%3A%2F%2Fapp.example.test%2Fpayment%2Freturn");
        await Assert.That(handler.Bodies.Single()).Contains("refresh_url=https%3A%2F%2Fapp.example.test%2Fpayment%2Frefresh");
    }

    [Test]
    public async Task GetReadinessAsync_MapsChargesCapabilitiesRequirementsCountryCurrencyAndRequestId()
    {
        var handler = new RecordingStripeHandler(_ => Json(HttpStatusCode.OK, """
            {
              "id":"acct_123",
              "object":"account",
              "livemode":false,
              "charges_enabled":true,
              "country":"be",
              "default_currency":"eur",
              "capabilities":{"card_payments":"active","transfers":"pending"},
              "requirements":{"currently_due":["business_profile.url"],"eventually_due":["representative.verification.document"],"past_due":[],"disabled_reason":null}
            }
            """, requestId: "req_ready"));
        StripeConnectAccountAdapter adapter = Adapter(handler);

        OrganizerPaymentProviderReadinessResult result = await adapter.GetReadinessAsync(
            new OrganizerPaymentProviderReadinessRequest("stripe", "platform-live-eu", "acct_123"),
            CancellationToken.None);

        OrganizerPaymentProviderReadiness readiness = result.Readiness!;
        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.ProviderRequestId).IsEqualTo("req_ready");
        await Assert.That(readiness.ChargesEnabled).IsTrue();
        await Assert.That(readiness.CardPaymentsCapabilityState).IsEqualTo(OrganizerPaymentProviderCapabilityState.Active);
        await Assert.That(readiness.TransfersCapabilityState).IsEqualTo(OrganizerPaymentProviderCapabilityState.Pending);
        await Assert.That(readiness.RequirementsState).IsEqualTo(OrganizerPaymentProviderRequirementsState.CurrentlyDue);
        await Assert.That(readiness.CurrentlyDueRequirementKeys).IsEquivalentTo(["business_profile.url"]);
        await Assert.That(readiness.EventuallyDueRequirementKeys).IsEquivalentTo(["representative.verification.document"]);
        await Assert.That(readiness.MerchantCountryCode).IsEqualTo("BE");
        await Assert.That(readiness.SupportedCurrencyCodes).IsEquivalentTo(["EUR"]);
        await Assert.That(readiness.ObservedAt).IsEqualTo(UtcNow);
        await Assert.That(readiness.EvidenceRevision).IsEqualTo("req_ready");
        await Assert.That(handler.Requests.Single().Method).IsEqualTo(HttpMethod.Get);
        await Assert.That(handler.Requests.Single().RequestUri!.AbsolutePath).IsEqualTo("/v1/accounts/acct_123");
    }

    [Test]
    public async Task GetReadinessAsync_FailsClosedWhenRequiredFactsAreAbsent()
    {
        var handler = new RecordingStripeHandler(_ => Json(HttpStatusCode.OK, """
            {"id":"acct_123","object":"account","livemode":false,"country":"be","default_currency":"eur","capabilities":{"card_payments":"active","transfers":"active"},"requirements":{}}
            """, requestId: "req_incomplete"));
        StripeConnectAccountAdapter adapter = Adapter(handler);

        OrganizerPaymentProviderReadinessResult result = await adapter.GetReadinessAsync(
            new OrganizerPaymentProviderReadinessRequest("stripe", "platform-live-eu", "acct_123"),
            CancellationToken.None);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Readiness!.ChargesEnabled).IsFalse();
        await Assert.That(result.Readiness.CardPaymentsCapabilityState).IsEqualTo(OrganizerPaymentProviderCapabilityState.Active);
        await Assert.That(result.ProviderRequestId).IsEqualTo("req_incomplete");
    }

    [Test]
    public async Task ProviderErrors_MapBoundedCodeKindAndRequestIdWithoutRawPayload()
    {
        var handler = new RecordingStripeHandler(_ => Json(HttpStatusCode.BadRequest, """
            {"error":{"type":"invalid_request_error","code":"account_invalid","message":"raw provider message that must not become the failure code"}}
            """, requestId: "req_bad"));
        StripeConnectAccountAdapter adapter = Adapter(handler);

        OrganizerPaymentProviderAccountCreationResult result = await adapter.CreateAccountAsync(AccountRequest(), CancellationToken.None);

        await Assert.That(result.Status).IsEqualTo(OrganizerPaymentProviderAccountCreationStatus.Failed);
        await Assert.That(result.FailureKind).IsEqualTo(OrganizerPaymentProviderFailureKind.ProviderRejected);
        await Assert.That(result.FailureCode).IsEqualTo("organizer_payment_provider_account_invalid");
        await Assert.That(result.FailureCode).DoesNotContain("raw provider message");
        await Assert.That(result.ProviderRequestId).IsEqualTo("req_bad");
    }

    [Test]
    public async Task CreateAccountAsync_NetworkTimeoutAndProviderUnknownReturnManualReconciliation()
    {
        var network = new RecordingStripeHandler(_ => throw new HttpRequestException("network down"));
        var rateLimited = new RecordingStripeHandler(_ => Json(HttpStatusCode.TooManyRequests, """
            {"error":{"type":"rate_limit_error","code":"rate_limit"}}
            """, requestId: "req_rate"));
        var serverError = new RecordingStripeHandler(_ => Json(HttpStatusCode.InternalServerError, """
            {"error":{"type":"api_error","code":"api_error"}}
            """, requestId: "req_500"));
        var timeout = new RecordingStripeHandler(_ => throw new OperationCanceledException());

        OrganizerPaymentProviderAccountCreationResult networkResult = await Adapter(network).CreateAccountAsync(AccountRequest(), CancellationToken.None);
        OrganizerPaymentProviderAccountCreationResult rateLimitedResult = await Adapter(rateLimited).CreateAccountAsync(AccountRequest(), CancellationToken.None);
        OrganizerPaymentProviderAccountCreationResult serverErrorResult = await Adapter(serverError).CreateAccountAsync(AccountRequest(), CancellationToken.None);
        OrganizerPaymentProviderAccountCreationResult timeoutResult = await Adapter(timeout).CreateAccountAsync(AccountRequest(), CancellationToken.None);

        await Assert.That(networkResult.Status).IsEqualTo(OrganizerPaymentProviderAccountCreationStatus.ManualReconciliationRequired);
        await Assert.That(networkResult.FailureKind).IsEqualTo(OrganizerPaymentProviderFailureKind.Network);
        await Assert.That(networkResult.FailureCode).IsEqualTo("organizer_payment_provider_network_failure");
        await Assert.That(rateLimitedResult.Status).IsEqualTo(OrganizerPaymentProviderAccountCreationStatus.ManualReconciliationRequired);
        await Assert.That(rateLimitedResult.FailureKind).IsEqualTo(OrganizerPaymentProviderFailureKind.ProviderUnknown);
        await Assert.That(rateLimitedResult.FailureCode).IsEqualTo("organizer_payment_provider_rate_limit");
        await Assert.That(rateLimitedResult.ProviderRequestId).IsEqualTo("req_rate");
        await Assert.That(serverErrorResult.Status).IsEqualTo(OrganizerPaymentProviderAccountCreationStatus.ManualReconciliationRequired);
        await Assert.That(serverErrorResult.FailureKind).IsEqualTo(OrganizerPaymentProviderFailureKind.ProviderUnknown);
        await Assert.That(serverErrorResult.FailureCode).IsEqualTo("organizer_payment_provider_api_error");
        await Assert.That(serverErrorResult.ProviderRequestId).IsEqualTo("req_500");
        await Assert.That(timeoutResult.Status).IsEqualTo(OrganizerPaymentProviderAccountCreationStatus.ManualReconciliationRequired);
        await Assert.That(timeoutResult.FailureKind).IsEqualTo(OrganizerPaymentProviderFailureKind.Network);
    }

    [Test]
    public async Task CreateAccountAsync_CallerCancellationRethrowsWithoutMapping()
    {
        var handler = new RecordingStripeHandler(_ => throw new InvalidOperationException("HTTP should not be called."));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.That(async () => await Adapter(handler).CreateAccountAsync(AccountRequest(), cts.Token))
            .Throws<OperationCanceledException>();
        await Assert.That(handler.Requests).IsEmpty();
    }

    [Test]
    public async Task UnsupportedProvider_FailsBeforeSecretOrNetworkForCreateLinkAndReadiness()
    {
        var handler = new RecordingStripeHandler(_ => throw new InvalidOperationException("HTTP should not be called."));
        ISecretResolver secretResolver = Substitute.For<ISecretResolver>();
        StripeConnectAccountAdapter adapter = Adapter(handler, secretResolver);

        OrganizerPaymentProviderAccountCreationResult create = await adapter.CreateAccountAsync(AccountRequest("Stripe"), CancellationToken.None);
        OrganizerPaymentOnboardingLinkCreationResult link = await adapter.CreateOnboardingLinkAsync(
            new OrganizerPaymentOnboardingLinkRequest(
                " stripe ",
                "platform-live-eu",
                "acct_123",
                new Uri("https://app.example.test/payment/return"),
                new Uri("https://app.example.test/payment/refresh"),
                OrganizerPaymentOnboardingType.AccountOnboarding),
            CancellationToken.None);
        OrganizerPaymentProviderReadinessResult readiness = await adapter.GetReadinessAsync(
            new OrganizerPaymentProviderReadinessRequest("STRIPE", "platform-live-eu", "acct_123"),
            CancellationToken.None);

        await Assert.That(create.Status).IsEqualTo(OrganizerPaymentProviderAccountCreationStatus.Failed);
        await Assert.That(create.FailureCode).IsEqualTo("organizer_payment_provider_unsupported");
        await Assert.That(link.Success).IsFalse();
        await Assert.That(link.FailureCode).IsEqualTo("organizer_payment_provider_unsupported");
        await Assert.That(readiness.Success).IsFalse();
        await Assert.That(readiness.FailureCode).IsEqualTo("organizer_payment_provider_unsupported");
        await Assert.That(handler.Requests).IsEmpty();
        await secretResolver.DidNotReceiveWithAnyArgs().ResolveAsync(default!, default, default);
    }

    [Test]
    public async Task MissingPlatformSecret_FailsBeforeNetworkAndResolvesInstanceScopeOnly()
    {
        var handler = new RecordingStripeHandler(_ => throw new InvalidOperationException("HTTP should not be called."));
        ISecretResolver secretResolver = Substitute.For<ISecretResolver>();
        StripeConnectAccountAdapter adapter = Adapter(handler, secretResolver);

        OrganizerPaymentProviderAccountCreationResult result = await adapter.CreateAccountAsync(AccountRequest(), CancellationToken.None);

        await Assert.That(result.Status).IsEqualTo(OrganizerPaymentProviderAccountCreationStatus.Failed);
        await Assert.That(result.FailureKind).IsEqualTo(OrganizerPaymentProviderFailureKind.Configuration);
        await Assert.That(result.FailureCode).IsEqualTo("organizer_payment_provider_secret_unavailable");
        await Assert.That(handler.Requests).IsEmpty();
        await secretResolver.Received(1).ResolveAsync(
            SecretDefinitionRegistry.Keys.Stripe.PlatformSecretKey,
            null,
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SecretModeMismatch_FailsBeforeNetwork()
    {
        var handler = new RecordingStripeHandler(_ => throw new InvalidOperationException("HTTP should not be called."));
        StripeConnectAccountAdapter adapter = Adapter(handler, SecretResolver("sk_live_platform"));

        OrganizerPaymentProviderAccountCreationResult result = await adapter.CreateAccountAsync(AccountRequest(), CancellationToken.None);

        await Assert.That(result.Status).IsEqualTo(OrganizerPaymentProviderAccountCreationStatus.Failed);
        await Assert.That(result.FailureKind).IsEqualTo(OrganizerPaymentProviderFailureKind.Configuration);
        await Assert.That(result.FailureCode).IsEqualTo("organizer_payment_provider_secret_mode_mismatch");
        await Assert.That(handler.Requests).IsEmpty();
    }

    [Test]
    public async Task CreateAccountAsync_WhenAccountModeMismatchesRequiresManualReconciliation()
    {
        var handler = new RecordingStripeHandler(_ => Json(HttpStatusCode.OK, "{\"id\":\"acct_123\",\"object\":\"account\",\"livemode\":true}"));
        StripeConnectAccountAdapter adapter = Adapter(handler);

        OrganizerPaymentProviderAccountCreationResult result = await adapter.CreateAccountAsync(AccountRequest(), CancellationToken.None);

        await Assert.That(result.Status).IsEqualTo(OrganizerPaymentProviderAccountCreationStatus.ManualReconciliationRequired);
        await Assert.That(result.FailureKind).IsEqualTo(OrganizerPaymentProviderFailureKind.ProviderDataIncomplete);
        await Assert.That(result.FailureCode).IsEqualTo("organizer_payment_provider_account_mode_mismatch");
        await Assert.That(handler.Requests).Count().IsEqualTo(1);
    }

    [Test]
    public async Task GetReadinessAsync_WhenAccountModeMismatchesFailsWithoutObservation()
    {
        var handler = new RecordingStripeHandler(_ => Json(HttpStatusCode.OK, "{\"id\":\"acct_123\",\"object\":\"account\",\"livemode\":true,\"charges_enabled\":true}"));
        StripeConnectAccountAdapter adapter = Adapter(handler);

        OrganizerPaymentProviderReadinessResult result = await adapter.GetReadinessAsync(
            new OrganizerPaymentProviderReadinessRequest("stripe", "platform-live-eu", "acct_123"),
            CancellationToken.None);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.Readiness).IsNull();
        await Assert.That(result.FailureKind).IsEqualTo(OrganizerPaymentProviderFailureKind.ProviderDataIncomplete);
        await Assert.That(result.FailureCode).IsEqualTo("organizer_payment_provider_account_mode_mismatch");
    }

    [Test]
    public async Task StripeConnectVerifier_WithSignedAccountUpdated_ReturnsTenantIdentity()
    {
        const string secret = "whsec_test_connect";
        const string accountId = "acct_123";
        var connection = OrganizerPaymentProviderConnection.Create(
            Guid.CreateVersion7(),
            TenantId,
            OrganizerActorId,
            "stripe",
            "platform-live-eu",
            accountId,
            UtcNow);
        var repository = Substitute.For<IOrganizerPaymentProviderConnectionRepository>();
        repository.ListHistoricalByExternalAccountAsync("stripe", accountId, 2, Arg.Any<CancellationToken>())
            .Returns([connection]);
        var verifier = CreateConnectVerifier(secret, repository);
        string payload = AccountUpdatedPayload("evt_signed", accountId);

        IncomingWebhookVerificationResult result = await verifier.VerifyAsync(
            new IncomingWebhookContext(
                StripeConnectIncomingWebhookVerifier.ProviderCode,
                payload,
                Encoding.UTF8.GetBytes(payload),
                StripeHeaders(payload, secret),
                DateTimeOffset.UtcNow),
            CancellationToken.None);

        await Assert.That(result.IsVerified).IsTrue();
        await Assert.That(result.TenantId).IsEqualTo(TenantId);
        await Assert.That(result.ProviderMessageId).IsEqualTo("evt_signed");
        await Assert.That(result.EventType).IsEqualTo(global::Stripe.EventTypes.AccountUpdated);
    }

    [Test]
    public async Task StripeConnectVerifier_WhenEventAndObjectAccountDisagree_Rejects()
    {
        const string secret = "whsec_test_connect";
        var verifier = CreateConnectVerifier(secret, Substitute.For<IOrganizerPaymentProviderConnectionRepository>());
        string payload = AccountUpdatedPayload("evt_mismatch", "acct_object", eventAccountId: "acct_event");

        IncomingWebhookVerificationResult result = await verifier.VerifyAsync(
            new IncomingWebhookContext(
                StripeConnectIncomingWebhookVerifier.ProviderCode,
                payload,
                Encoding.UTF8.GetBytes(payload),
                StripeHeaders(payload, secret),
                DateTimeOffset.UtcNow),
            CancellationToken.None);

        await Assert.That(result.IsVerified).IsFalse();
        await Assert.That(result.FailureCategory).IsEqualTo("stripe_connect_account_mismatch");
    }

    [Test]
    [Arguments(null, "stripe_connect_signature_missing")]
    [Arguments("wrong", "stripe_connect_signature_invalid")]
    public async Task StripeConnectVerifier_WhenSignatureMissingOrInvalid_Rejects(string? signatureMode, string failureCategory)
    {
        const string secret = "whsec_test_connect";
        const string accountId = "acct_123";
        var verifier = CreateConnectVerifier(secret, Substitute.For<IOrganizerPaymentProviderConnectionRepository>());
        string payload = AccountUpdatedPayload("evt_signature", accountId);
        Dictionary<string, string> headers = signatureMode is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : StripeHeaders(payload, "whsec_wrong");

        IncomingWebhookVerificationResult result = await verifier.VerifyAsync(
            new IncomingWebhookContext(
                StripeConnectIncomingWebhookVerifier.ProviderCode,
                payload,
                Encoding.UTF8.GetBytes(payload),
                headers,
                DateTimeOffset.UtcNow),
            CancellationToken.None);

        await Assert.That(result.IsVerified).IsFalse();
        await Assert.That(result.FailureCategory).IsEqualTo(failureCategory);
    }

    [Test]
    public async Task StripeConnectVerifier_WhenApiVersionDiffers_Rejects()
    {
        const string secret = "whsec_test_connect";
        const string accountId = "acct_123";
        var verifier = CreateConnectVerifier(secret, Substitute.For<IOrganizerPaymentProviderConnectionRepository>());
        string payload = AccountUpdatedPayload("evt_wrong_version", accountId, apiVersion: "2000-01-01");

        IncomingWebhookVerificationResult result = await verifier.VerifyAsync(
            new IncomingWebhookContext(
                StripeConnectIncomingWebhookVerifier.ProviderCode,
                payload,
                Encoding.UTF8.GetBytes(payload),
                StripeHeaders(payload, secret),
                DateTimeOffset.UtcNow),
            CancellationToken.None);

        await Assert.That(result.IsVerified).IsFalse();
        await Assert.That(result.FailureCategory).IsEqualTo("stripe_connect_signature_invalid");
    }

    [Test]
    public async Task StripeConnectVerifier_WhenSecretMissing_RejectsBeforeParsing()
    {
        const string accountId = "acct_123";
        var verifier = CreateConnectVerifier(null, Substitute.For<IOrganizerPaymentProviderConnectionRepository>());
        string payload = AccountUpdatedPayload("evt_missing_secret", accountId);

        IncomingWebhookVerificationResult result = await verifier.VerifyAsync(
            new IncomingWebhookContext(
                StripeConnectIncomingWebhookVerifier.ProviderCode,
                payload,
                Encoding.UTF8.GetBytes(payload),
                StripeHeaders(payload, "whsec_test_connect"),
                DateTimeOffset.UtcNow),
            CancellationToken.None);

        await Assert.That(result.IsVerified).IsFalse();
        await Assert.That(result.FailureCategory).IsEqualTo("stripe_connect_secret_unavailable");
    }

    [Test]
    [Arguments(0)]
    [Arguments(2)]
    public async Task StripeConnectVerifier_WhenLocalAccountMatchCountIsNotOne_Rejects(int matchCount)
    {
        const string secret = "whsec_test_connect";
        const string accountId = "acct_123";
        var matches = Enumerable.Range(0, matchCount)
            .Select(_ => OrganizerPaymentProviderConnection.Create(Guid.CreateVersion7(), TenantId, OrganizerActorId, "stripe", "platform-live-eu", accountId, UtcNow))
            .ToArray();
        var repository = Substitute.For<IOrganizerPaymentProviderConnectionRepository>();
        repository.ListHistoricalByExternalAccountAsync("stripe", accountId, 2, Arg.Any<CancellationToken>()).Returns(matches);
        var verifier = CreateConnectVerifier(secret, repository);
        string payload = AccountUpdatedPayload("evt_local_match", accountId);

        IncomingWebhookVerificationResult result = await verifier.VerifyAsync(
            new IncomingWebhookContext(
                StripeConnectIncomingWebhookVerifier.ProviderCode,
                payload,
                Encoding.UTF8.GetBytes(payload),
                StripeHeaders(payload, secret),
                DateTimeOffset.UtcNow),
            CancellationToken.None);

        await Assert.That(result.IsVerified).IsFalse();
        await Assert.That(result.FailureCategory).IsEqualTo("stripe_connect_account_not_unique");
    }

    [Test]
    public async Task StripeConnectVerifier_WhenSignedEventModeMismatches_RejectsBeforeTenantLookup()
    {
        const string secret = "whsec_test_connect";
        const string accountId = "acct_123";
        var repository = Substitute.For<IOrganizerPaymentProviderConnectionRepository>();
        var verifier = CreateConnectVerifier(secret, repository);
        string payload = AccountUpdatedPayload("evt_live", accountId, eventLivemode: true, accountLivemode: true);

        IncomingWebhookVerificationResult result = await verifier.VerifyAsync(
            new IncomingWebhookContext(
                StripeConnectIncomingWebhookVerifier.ProviderCode,
                payload,
                Encoding.UTF8.GetBytes(payload),
                StripeHeaders(payload, secret),
                DateTimeOffset.UtcNow),
            CancellationToken.None);

        await Assert.That(result.IsVerified).IsFalse();
        await Assert.That(result.FailureCategory).IsEqualTo("stripe_connect_event_mode_mismatch");
        await repository.DidNotReceiveWithAnyArgs().ListHistoricalByExternalAccountAsync(default!, default!, default, default);
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("evt_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task StripeConnectVerifier_WhenSignedEventIdIsMissingBlankOrOverlong_RejectsBeforeTenantLookup(string? eventId)
    {
        const string secret = "whsec_test_connect";
        const string accountId = "acct_123";
        var repository = Substitute.For<IOrganizerPaymentProviderConnectionRepository>();
        var verifier = CreateConnectVerifier(secret, repository);
        string payload = AccountUpdatedPayload(eventId, accountId);

        IncomingWebhookVerificationResult result = await verifier.VerifyAsync(
            new IncomingWebhookContext(
                StripeConnectIncomingWebhookVerifier.ProviderCode,
                payload,
                Encoding.UTF8.GetBytes(payload),
                StripeHeaders(payload, secret),
                DateTimeOffset.UtcNow),
            CancellationToken.None);

        await Assert.That(result.IsVerified).IsFalse();
        await Assert.That(result.FailureCategory).IsEqualTo("stripe_connect_event_id_invalid");
        await repository.DidNotReceiveWithAnyArgs().ListHistoricalByExternalAccountAsync(default!, default!, default, default);
    }

    [Test]
    public async Task StripeConnectHandler_AppliesReadinessFromPersistedAccountUpdatedPayload()
    {
        const string accountId = "acct_123";
        var connection = OrganizerPaymentProviderConnection.Create(
            Guid.CreateVersion7(),
            TenantId,
            OrganizerActorId,
            "stripe",
            "platform-live-eu",
            accountId,
            UtcNow);
        var repository = Substitute.For<IOrganizerPaymentProviderConnectionRepository>();
        repository.GetByTenantProviderAndExternalAccountForUpdateAsync(TenantId, "stripe", accountId, Arg.Any<CancellationToken>())
            .Returns(connection);
        var handler = new StripeConnectIncomingWebhookHandler(repository, StripeOptions());
        string payload = AccountUpdatedPayload("evt_ready", accountId);
        IncomingWebhookProcessingContext context = ProcessingContext(payload, "evt_ready");

        IncomingWebhookProcessingResult result = await handler.HandleAsync(context, CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(IncomingWebhookProcessingOutcome.Processed);
        await Assert.That(connection.StatusId).IsEqualTo((int)OrganizerPaymentProviderConnectionStatusEnum.Ready);
        await Assert.That(connection.MerchantCountryCode).IsEqualTo("BE");
        await Assert.That(connection.SupportedCurrencyCodes).IsEquivalentTo(["EUR"]);
        await Assert.That(connection.LastReadinessObservedAt).IsEqualTo(UtcNow.AddMinutes(1));
        await Assert.That(connection.LastReadinessEvidenceRevision).IsEqualTo("evt_ready");
        await repository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    [Arguments("evt_other", null)]
    [Arguments("evt_ready", "account.application.deauthorized")]
    public async Task StripeConnectHandler_WhenPersistedContextDiffersFromSignedEvent_Rejects(string providerMessageId, string? eventType)
    {
        const string accountId = "acct_123";
        var repository = Substitute.For<IOrganizerPaymentProviderConnectionRepository>();
        var handler = new StripeConnectIncomingWebhookHandler(repository, StripeOptions());
        string payload = AccountUpdatedPayload("evt_ready", accountId);

        IncomingWebhookProcessingResult result = await handler.HandleAsync(
            ProcessingContext(payload, providerMessageId, eventType ?? global::Stripe.EventTypes.AccountUpdated),
            CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(IncomingWebhookProcessingOutcome.RejectedPermanent);
        await Assert.That(result.FailureCategory).IsEqualTo("stripe_connect_context_mismatch");
        await repository.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Test]
    [Arguments(false, "active", OrganizerPaymentProviderConnectionStatusEnum.Restricted)]
    [Arguments(true, "pending", OrganizerPaymentProviderConnectionStatusEnum.Restricted)]
    [Arguments(true, "inactive", OrganizerPaymentProviderConnectionStatusEnum.Restricted)]
    public async Task StripeConnectHandler_WhenChargesOrTransfersAreNotReady_Restricts(bool chargesEnabled, string transfers, OrganizerPaymentProviderConnectionStatusEnum expectedStatus)
    {
        const string accountId = "acct_123";
        OrganizerPaymentProviderConnection connection = Connection(accountId);
        var repository = RepositoryForConnection(accountId, connection);
        var handler = new StripeConnectIncomingWebhookHandler(repository, StripeOptions());
        string payload = AccountUpdatedPayload("evt_restricted", accountId, chargesEnabled: chargesEnabled, transfers: transfers);

        IncomingWebhookProcessingResult result = await handler.HandleAsync(ProcessingContext(payload, "evt_restricted"), CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(IncomingWebhookProcessingOutcome.Processed);
        await Assert.That(connection.StatusId).IsEqualTo((int)expectedStatus);
        await repository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task StripeConnectHandler_WhenIncompleteAccountPayloadArrivesAfterReady_ClearsReadinessToRestricted()
    {
        const string accountId = "acct_123";
        OrganizerPaymentProviderConnection connection = Connection(accountId);
        connection.ApplyReadiness(OrganizerPaymentProviderReadinessObservation.Create("BE", ChargeCapabilityState.Active, ProviderRequirementsState.Satisfied, ["EUR"], UtcNow, "evt_previous"));
        var repository = RepositoryForConnection(accountId, connection);
        var handler = new StripeConnectIncomingWebhookHandler(repository, StripeOptions());
        string payload = AccountUpdatedPayload("evt_incomplete", accountId, includeCountryCurrencyCapabilitiesRequirements: false);

        IncomingWebhookProcessingResult result = await handler.HandleAsync(ProcessingContext(payload, "evt_incomplete"), CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(IncomingWebhookProcessingOutcome.Processed);
        await Assert.That(connection.StatusId).IsEqualTo((int)OrganizerPaymentProviderConnectionStatusEnum.Restricted);
        await Assert.That(connection.MerchantCountryCode).IsNull();
        await Assert.That(connection.SupportedCurrencyCodes).IsEmpty();
        await repository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task StripeConnectHandler_WhenSignedEventIsStale_IgnoresWithoutMutating()
    {
        const string accountId = "acct_123";
        OrganizerPaymentProviderConnection connection = Connection(accountId);
        connection.ApplyReadiness(OrganizerPaymentProviderReadinessObservation.Create("BE", ChargeCapabilityState.Active, ProviderRequirementsState.Satisfied, ["EUR"], UtcNow.AddMinutes(2), "evt_newer"));
        var repository = RepositoryForConnection(accountId, connection);
        var handler = new StripeConnectIncomingWebhookHandler(repository, StripeOptions());
        string payload = AccountUpdatedPayload("evt_stale", accountId);

        IncomingWebhookProcessingResult result = await handler.HandleAsync(ProcessingContext(payload, "evt_stale"), CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(IncomingWebhookProcessingOutcome.Ignored);
        await Assert.That(result.FailureCategory).IsEqualTo("stripe_connect_readiness_stale");
        await Assert.That(connection.LastReadinessEvidenceRevision).IsEqualTo("evt_newer");
        await repository.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Test]
    public async Task StripeConnectHandler_WhenAccountApplicationDeauthorized_AppliesRestrictedReadiness()
    {
        const string accountId = "acct_123";
        OrganizerPaymentProviderConnection connection = Connection(accountId);
        connection.ApplyReadiness(OrganizerPaymentProviderReadinessObservation.Create("BE", ChargeCapabilityState.Active, ProviderRequirementsState.Satisfied, ["EUR"], UtcNow, "evt_previous"));
        var repository = RepositoryForConnection(accountId, connection);
        var handler = new StripeConnectIncomingWebhookHandler(repository, StripeOptions());
        string payload = AccountApplicationDeauthorizedPayload("evt_deauthorized", accountId);

        IncomingWebhookProcessingResult result = await handler.HandleAsync(
            ProcessingContext(payload, "evt_deauthorized", global::Stripe.EventTypes.AccountApplicationDeauthorized),
            CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(IncomingWebhookProcessingOutcome.Processed);
        await Assert.That(connection.StatusId).IsEqualTo((int)OrganizerPaymentProviderConnectionStatusEnum.Restricted);
        await Assert.That(connection.MerchantCountryCode).IsNull();
        await Assert.That(connection.SupportedCurrencyCodes).IsEmpty();
        await Assert.That(connection.LastReadinessEvidenceRevision).IsEqualTo("evt_deauthorized");
        await repository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    [Arguments("disabled")]
    [Arguments("replaced")]
    public async Task StripeConnectHandler_WhenHistoricalConnectionIsTerminal_IgnoresWithoutMutating(string terminalState)
    {
        const string accountId = "acct_123";
        OrganizerPaymentProviderConnection connection = Connection(accountId);
        if (terminalState == "disabled")
        {
            connection.Disable("operator-disabled", UtcNow.AddSeconds(1));
        }
        else
        {
            connection.ReplaceWith(Guid.CreateVersion7(), "acct_replacement", UtcNow.AddSeconds(1));
        }

        var repository = RepositoryForConnection(accountId, connection);
        var handler = new StripeConnectIncomingWebhookHandler(repository, StripeOptions());
        string payload = AccountUpdatedPayload("evt_terminal", accountId);

        IncomingWebhookProcessingResult result = await handler.HandleAsync(ProcessingContext(payload, "evt_terminal"), CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(IncomingWebhookProcessingOutcome.Ignored);
        await Assert.That(result.FailureCategory).IsEqualTo("stripe_connect_connection_terminal");
        await repository.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Test]
    public async Task StripeConnectHandler_WhenPersistedPayloadIsMalformed_RejectsPermanently()
    {
        var repository = Substitute.For<IOrganizerPaymentProviderConnectionRepository>();
        var handler = new StripeConnectIncomingWebhookHandler(repository, StripeOptions());

        IncomingWebhookProcessingResult result = await handler.HandleAsync(
            ProcessingContext("{", "evt_malformed"),
            CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(IncomingWebhookProcessingOutcome.RejectedPermanent);
        await Assert.That(result.FailureCategory).IsEqualTo("stripe_connect_payload_invalid");
        await repository.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Test]
    public async Task StripeConnectHandler_WhenPersistedEventModeMismatches_RejectsPermanentlyBeforeLookup()
    {
        const string accountId = "acct_123";
        var repository = Substitute.For<IOrganizerPaymentProviderConnectionRepository>();
        var handler = new StripeConnectIncomingWebhookHandler(repository, StripeOptions());
        string payload = AccountUpdatedPayload("evt_live_persisted", accountId, eventLivemode: true, accountLivemode: true);

        IncomingWebhookProcessingResult result = await handler.HandleAsync(
            ProcessingContext(payload, "evt_live_persisted"),
            CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(IncomingWebhookProcessingOutcome.RejectedPermanent);
        await Assert.That(result.FailureCategory).IsEqualTo("stripe_connect_event_mode_mismatch");
        await repository.DidNotReceiveWithAnyArgs().GetByTenantProviderAndExternalAccountForUpdateAsync(default, default!, default!, default);
        await repository.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Test]
    public async Task StripeConnectHandler_WhenPersistedAccountUpdatedObjectModeIsMissing_RejectsPermanently()
    {
        const string accountId = "acct_123";
        var repository = Substitute.For<IOrganizerPaymentProviderConnectionRepository>();
        var handler = new StripeConnectIncomingWebhookHandler(repository, StripeOptions());
        string payload = AccountUpdatedPayload("evt_account_mode_missing", accountId, includeAccountLivemode: false);

        IncomingWebhookProcessingResult result = await handler.HandleAsync(
            ProcessingContext(payload, "evt_account_mode_missing"),
            CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(IncomingWebhookProcessingOutcome.RejectedPermanent);
        await Assert.That(result.FailureCategory).IsEqualTo("stripe_connect_account_mode_missing");
        await repository.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    private static OrganizerPaymentProviderAccountCreationRequest AccountRequest(string providerCode = "stripe") =>
        new(TenantId, OrganizerActorId, providerCode, "platform-live-eu", "organizer-payment-account-018e4e5c7f0070008000000000000099");

    private static StripeConnectAccountAdapter Adapter(RecordingStripeHandler handler, ISecretResolver? secretResolver = null)
    {
        global::Stripe.StripeConfiguration.ApiKey = null;
        secretResolver ??= SecretResolver();
        return new StripeConnectAccountAdapter(
            new SingleClientFactory(new HttpClient(handler) { BaseAddress = new Uri("https://api.stripe.example.test") }),
            secretResolver,
            new FixedTimeProvider(UtcNow),
            StripeOptions());
    }

    private static ISecretResolver SecretResolver(string value = "sk_test_platform")
    {
        ISecretResolver secretResolver = Substitute.For<ISecretResolver>();
        secretResolver.ResolveAsync(
                SecretDefinitionRegistry.Keys.Stripe.PlatformSecretKey,
                null,
                Arg.Any<CancellationToken>()).Returns(SecretResolutionResult.Resolved(new ResolvedSecret(SecretDefinitionRegistry.Keys.Stripe.PlatformSecretKey,
        value,
        SecretSourceType.EnvironmentVariable,
        SecretScope.Instance,
        null,
        DateTimeOffset.UtcNow)));
        return secretResolver;
    }

    private static StripeConnectIncomingWebhookVerifier CreateConnectVerifier(
        string? secret,
        IOrganizerPaymentProviderConnectionRepository repository)
    {
        ISecretResolver secretResolver = Substitute.For<ISecretResolver>();
        secretResolver.ResolveAsync(SecretDefinitionRegistry.Keys.Stripe.WebhookSecret, null, Arg.Any<CancellationToken>())
            .Returns(secret is null
                ? SecretResolutionResult.Unconfigured
                : SecretResolutionResult.Resolved(new ResolvedSecret(
                    SecretDefinitionRegistry.Keys.Stripe.WebhookSecret,
                    secret,
                    SecretSourceType.EnvironmentVariable,
                    SecretScope.Instance,
                    null,
                    DateTimeOffset.UtcNow)));
        return new StripeConnectIncomingWebhookVerifier(
            new StaticOptionsMonitor<WebhookOptions>(new WebhookOptions()),
            StripeOptions(),
            secretResolver,
            repository,
            NullLogger<StripeConnectIncomingWebhookVerifier>.Instance);
    }

    private static IOptions<StripePaymentOptions> StripeOptions(string mode = StripePaymentOptions.TestMode) =>
        Options.Create(new StripePaymentOptions { Mode = mode });

    private static Dictionary<string, string> StripeHeaders(string payload, string secret) => new(StringComparer.OrdinalIgnoreCase)
    {
        ["Stripe-Signature"] = global::Stripe.EventUtility.GenerateSignatureHeader(payload, secret)
    };

    private static string AccountUpdatedPayload(
        string? eventId,
        string accountId,
        string? eventAccountId = null,
        string? apiVersion = null,
        bool chargesEnabled = true,
        string transfers = "active",
        bool includeCountryCurrencyCapabilitiesRequirements = true,
        bool eventLivemode = false,
        bool accountLivemode = false,
        bool includeEventLivemode = true,
        bool includeAccountLivemode = true)
    {
        string resolvedApiVersion = apiVersion ?? global::Stripe.StripeConfiguration.ApiVersion;
        string eventIdField = eventId is null ? string.Empty : $"  \"id\":\"{eventId}\",\n";
        string eventLivemodeField = includeEventLivemode ? $"  \"livemode\":{eventLivemode.ToString().ToLowerInvariant()},\n" : string.Empty;
        string accountLivemodeField = includeAccountLivemode ? $"\"livemode\":{accountLivemode.ToString().ToLowerInvariant()}," : string.Empty;
        string accountFields = includeCountryCurrencyCapabilitiesRequirements
            ? $$"""
              "country":"be",
              "default_currency":"eur",
              "capabilities":{"card_payments":"active","transfers":"{{transfers}}"},
              "requirements":{"currently_due":[],"eventually_due":[],"past_due":[],"disabled_reason":null}
            """
            : "";
        return $$"""
        {
        {{eventIdField}}
        {{eventLivemodeField}}
          "object":"event",
          "api_version":"{{resolvedApiVersion}}",
          "created":{{new DateTimeOffset(UtcNow.AddMinutes(1)).ToUnixTimeSeconds()}},
          "type":"account.updated",
          "account":"{{eventAccountId ?? accountId}}",
          "data":{
            "object":{
              "id":"{{accountId}}",
              "object":"account",
              {{accountLivemodeField}}
              "charges_enabled":{{chargesEnabled.ToString().ToLowerInvariant()}}{{(accountFields.Length > 0 ? "," : string.Empty)}}
              {{accountFields}}
            }
          }
        }
        """;
    }

    private static string AccountApplicationDeauthorizedPayload(string eventId, string accountId)
    {
        string apiVersion = global::Stripe.StripeConfiguration.ApiVersion;
        return $$$"""
        {
          "id":"{{{eventId}}}",
          "object":"event",
          "livemode":false,
          "api_version":"{{{apiVersion}}}",
          "created":{{{new DateTimeOffset(UtcNow.AddMinutes(1)).ToUnixTimeSeconds()}}},
          "type":"account.application.deauthorized",
          "account":"{{{accountId}}}",
          "data":{"object":{"object":"application","id":"ca_test"}}
        }
        """;
    }

    private static IncomingWebhookProcessingContext ProcessingContext(
        string payload,
        string providerMessageId,
        string eventType = global::Stripe.EventTypes.AccountUpdated)
    {
        DateTime now = UtcNow;
        var message = IncomingWebhookMessage.CreateVerified(
            TenantId,
            StripeConnectIncomingWebhookVerifier.ProviderCode,
            providerMessageId,
            providerMessageId,
            eventType,
            Encoding.UTF8.GetBytes(payload),
            "sha256:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant(),
            "application/json",
            "utf-8",
            headersJson: null,
            now,
            now,
            now.AddDays(14),
            "webhook-retention-test-v1",
            now.AddDays(30),
            now.AddDays(90),
            now.AddDays(14),
            now.AddDays(30));
        Guid leaseToken = Guid.CreateVersion7();
        message.Claim("test", leaseToken, now.AddMinutes(5), now.AddSeconds(1));
        return IncomingWebhookProcessingContext.FromClaimedMessage(
            message,
            leaseToken,
            message.ProcessingFence,
            message.ProcessingGeneration,
            now.AddSeconds(2));
    }

    private static OrganizerPaymentProviderConnection Connection(string accountId) => OrganizerPaymentProviderConnection.Create(
        Guid.CreateVersion7(),
        TenantId,
        OrganizerActorId,
        "stripe",
        "platform-live-eu",
        accountId,
        UtcNow);

    private static IOrganizerPaymentProviderConnectionRepository RepositoryForConnection(
        string accountId,
        OrganizerPaymentProviderConnection connection)
    {
        var repository = Substitute.For<IOrganizerPaymentProviderConnectionRepository>();
        repository.GetByTenantProviderAndExternalAccountForUpdateAsync(TenantId, "stripe", accountId, Arg.Any<CancellationToken>())
            .Returns(connection);
        return repository;
    }

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string body, string requestId = "req_default")
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        };
        response.Headers.Add("Request-Id", requestId);
        return response;
    }

    private sealed class RecordingStripeHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string> Bodies { get; } = [];
        public List<string?> IdempotencyKeys { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            IdempotencyKeys.Add(request.Headers.TryGetValues("Idempotency-Key", out IEnumerable<string>? values) ? values.Single() : null);
            return responseFactory(request);
        }
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            if (name != StripeConnectAccountAdapter.HttpClientName)
            {
                throw new InvalidOperationException("Unexpected HttpClient name.");
            }

            return client;
        }
    }

    private sealed class FixedTimeProvider(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class StaticOptionsMonitor<TOptions>(TOptions currentValue) : IOptionsMonitor<TOptions>
    {
        public TOptions CurrentValue => currentValue;

        public TOptions Get(string? name) => currentValue;

        public IDisposable? OnChange(Action<TOptions, string?> listener) => null;
    }
}
