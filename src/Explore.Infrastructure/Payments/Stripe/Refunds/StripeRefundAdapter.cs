using System.Net;
using Explore.Application.Contracts.Payments;
using Explore.Application.Contracts.Secrets;
using Explore.Domain.Secrets;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Payments.Stripe.Refunds;

public sealed class StripeRefundAdapter(
    IHttpClientFactory httpClientFactory,
    ISecretResolver secretResolver,
    IOptions<StripePaymentOptions> options,
    TimeProvider? timeProvider = null) : IRefundCreator, IRefundRetriever
{
    public const string HttpClientName = "Payments.StripeRefund";
    // Mutation retry authority belongs to the durable business operation, not SDK transport.
    private const int MaxNetworkRetries = 0;
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public async Task<RefundProviderResult> CreateAsync(
        RefundCreateRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsStripe(request.ProviderCode))
        {
            return RefundProviderResult.Failed(ConfigurationFailure("refund_provider_unsupported"));
        }
        if (!CanMutate(request.MutationExpiresAt))
            return RefundProviderResult.Failed(ConfigurationFailure("refund_provider_retention_expired"));

        bool handedOff = false;
        try
        {
            global::Stripe.StripeClient client = await CreateClientAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!CanMutate(request.MutationExpiresAt))
                return RefundProviderResult.Failed(ConfigurationFailure("refund_provider_retention_expired"));
            handedOff = true;
            global::Stripe.Refund refund = await client.V1.Refunds.CreateAsync(
                new global::Stripe.RefundCreateOptions
                {
                    PaymentIntent = request.ProviderPaymentId,
                    Amount = request.AmountMinor,
                    RefundApplicationFee = false,
                    Metadata = new Dictionary<string, string>
                    {
                        ["islamu_refund_attempt_id"] = request.RefundAttemptId.ToString("D")
                    }
                },
                new global::Stripe.RequestOptions
                {
                    StripeAccount = request.ExternalAccountId,
                    IdempotencyKey = request.ProviderIdempotencyKey
                },
                cancellationToken);
            return await MapAsync(
                client,
                refund,
                request.ProviderPaymentId,
                request.ExternalAccountId,
                request.ProviderIdempotencyKey,
                request.ApplicationFeeRefundAmountMinor,
                request.AmountMinor,
                request.CurrencyCode,
                observeApplicationFee: false,
                allowApplicationFeeMutation: false,
                request.MutationExpiresAt,
                cancellationToken);
        }
        catch (OperationCanceledException) when (!handedOff && cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (MapFailure(exception) is { } failure)
        {
            RefundProviderFailure mapped = failure with { ProviderHandoffStarted = handedOff };
            return IsAmbiguous(mapped)
                ? RefundProviderResult.Unknown(mapped)
                : RefundProviderResult.Failed(mapped);
        }
    }

    public async Task<RefundProviderResult> RetrieveAsync(
        RefundRetrieveRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsStripe(request.ProviderCode))
        {
            return RefundProviderResult.Failed(ConfigurationFailure("refund_provider_unsupported"));
        }

        try
        {
            global::Stripe.StripeClient client = await CreateClientAsync(cancellationToken);
            global::Stripe.Refund? refund;
            if (request.ProviderRefundId.Length == 0)
            {
                if (request.RefundAttemptId == Guid.Empty)
                    return RefundProviderResult.Failed(ConfigurationFailure("refund_recovery_identity_missing"));
                refund = null;
                await foreach (global::Stripe.Refund candidate in client.V1.Refunds.ListAutoPagingAsync(
                    new global::Stripe.RefundListOptions { PaymentIntent = request.ProviderPaymentId, Limit = 100 },
                    new global::Stripe.RequestOptions { StripeAccount = request.ExternalAccountId }, cancellationToken))
                {
                    if (candidate.Metadata?.GetValueOrDefault("islamu_refund_attempt_id") != request.RefundAttemptId.ToString("D"))
                        continue;
                    if (refund is not null)
                        return RefundProviderResult.Unknown(new("refund_provider_identity_ambiguous", RefundProviderFailureKind.ProviderDataIncomplete));
                    refund = candidate;
                }
                if (refund is null)
                    return CanMutate(request.MutationExpiresAt)
                        ? RefundProviderResult.Unknown(new("refund_provider_not_observed", RefundProviderFailureKind.ProviderDataIncomplete))
                        : RefundProviderResult.Failed(ConfigurationFailure("refund_provider_retention_expired"));
            }
            else
            {
                refund = await client.V1.Refunds.GetAsync(
                    request.ProviderRefundId, options: null,
                    new global::Stripe.RequestOptions { StripeAccount = request.ExternalAccountId }, cancellationToken);
                if (refund.Id != request.ProviderRefundId)
                    return RefundProviderResult.Unknown(new(
                        "refund_provider_identity_mismatch", RefundProviderFailureKind.ProviderDataIncomplete));
            }
            return await MapAsync(
                client,
                refund,
                request.ProviderPaymentId,
                request.ExternalAccountId,
                request.ProviderIdempotencyKey,
                request.ExpectedApplicationFeeRefundAmountMinor,
                request.ExpectedAmountMinor,
                request.ExpectedCurrencyCode,
                observeApplicationFee: true,
                request.AllowApplicationFeeMutation,
                request.MutationExpiresAt,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (MapFailure(exception) is { } failure)
        {
            // Any fee mutation is caught inside MapAsync. Failures reaching here
            // occurred during read/client preparation, before the fee handoff.
            RefundProviderFailure beforeFee = failure with { ProviderHandoffStarted = false };
            return IsAmbiguous(beforeFee)
                ? RefundProviderResult.Unknown(beforeFee)
                : RefundProviderResult.Failed(beforeFee);
        }
    }

    private async Task<RefundProviderResult> MapAsync(
        global::Stripe.StripeClient client,
        global::Stripe.Refund refund,
        string expectedProviderPaymentId,
        string externalAccountId,
        string providerIdempotencyKey,
        long expectedApplicationFeeRefundAmountMinor,
        long expectedAmountMinor,
        string expectedCurrencyCode,
        bool observeApplicationFee,
        bool allowApplicationFeeMutation,
        DateTime? mutationExpiresAt,
        CancellationToken cancellationToken)
    {
        string? requestId = RequestId(refund.StripeResponse);
        string? refundId = BoundedText(refund.Id, 200);
        string? paymentId = BoundedText(refund.PaymentIntentId, 200);
        string? currency = NormalizeCurrency(refund.Currency);
        RefundProviderStatus status = MapStatus(refund.Status);
        if (refundId is null || paymentId is null ||
            !string.Equals(paymentId, expectedProviderPaymentId, StringComparison.Ordinal) ||
            currency is null || refund.Amount != expectedAmountMinor ||
            !string.Equals(currency, expectedCurrencyCode, StringComparison.Ordinal) || status == RefundProviderStatus.Unknown)
        {
            return RefundProviderResult.Unknown(new(
                "refund_provider_response_incomplete",
                RefundProviderFailureKind.ProviderDataIncomplete,
                requestId));
        }

        long? refundedApplicationFeeMinor = null;
        string? applicationFeeFailureCode = null;
        bool feeHandoffStarted = false;
        if (status == RefundProviderStatus.Succeeded)
        {
            try
            {
                if (expectedApplicationFeeRefundAmountMinor == 0)
                {
                    refundedApplicationFeeMinor = 0;
                }
                else if (observeApplicationFee && BoundedText(refund.ChargeId, 200) is { } chargeId)
                {
                    global::Stripe.Charge charge = await client.V1.Charges.GetAsync(
                        chargeId,
                        options: null,
                        new global::Stripe.RequestOptions { StripeAccount = externalAccountId },
                        cancellationToken);
                    if (charge.Id != chargeId || charge.PaymentIntentId != expectedProviderPaymentId)
                        applicationFeeFailureCode = "refund_provider_charge_identity_mismatch";
                    else if (BoundedText(charge.ApplicationFeeId, 200) is { } applicationFeeId)
                    {
                        var feeRefunds = new global::Stripe.ApplicationFeeRefundService(client);
                        global::Stripe.ApplicationFeeRefund? feeRefund = null;
                        await foreach (global::Stripe.ApplicationFeeRefund candidate in feeRefunds.ListAutoPagingAsync(
                            applicationFeeId,
                            new global::Stripe.ApplicationFeeRefundListOptions { Limit = 100 },
                            new global::Stripe.RequestOptions(), cancellationToken))
                        {
                            if (candidate.Metadata?.GetValueOrDefault("islamu_refund_id") != refundId)
                                continue;
                            if (feeRefund is not null ||
                                candidate.Amount != expectedApplicationFeeRefundAmountMinor ||
                                NormalizeCurrency(candidate.Currency) != expectedCurrencyCode ||
                                candidate.FeeId != applicationFeeId)
                            {
                                applicationFeeFailureCode = "refund_provider_fee_identity_mismatch";
                                break;
                            }
                            feeRefund = candidate;
                        }
                        if (applicationFeeFailureCode is null && feeRefund is null &&
                            allowApplicationFeeMutation && CanMutate(mutationExpiresAt))
                        {
                            feeHandoffStarted = true;
                            feeRefund = await feeRefunds.CreateAsync(
                                applicationFeeId,
                                new global::Stripe.ApplicationFeeRefundCreateOptions
                                {
                                    Amount = expectedApplicationFeeRefundAmountMinor,
                                    Metadata = new Dictionary<string, string>
                                    {
                                        ["islamu_refund_id"] = refundId
                                    }
                                },
                                new global::Stripe.RequestOptions
                                {
                                    IdempotencyKey = $"{providerIdempotencyKey}:fee"
                                },
                                cancellationToken);
                        }
                        string? feeCurrency = NormalizeCurrency(feeRefund?.Currency);
                        if (applicationFeeFailureCode is null && feeRefund is not null &&
                            feeRefund.Amount == expectedApplicationFeeRefundAmountMinor &&
                            string.Equals(feeCurrency, expectedCurrencyCode, StringComparison.Ordinal) &&
                            string.Equals(feeRefund.FeeId, applicationFeeId, StringComparison.Ordinal))
                        {
                            refundedApplicationFeeMinor = feeRefund.Amount;
                        }
                        else if (applicationFeeFailureCode is null && !CanMutate(mutationExpiresAt))
                            applicationFeeFailureCode = "refund_provider_retention_expired";
                    }
                }
            }
            catch (Exception exception) when (MapFailure(exception) is { } feeFailure)
            {
                applicationFeeFailureCode = IsAmbiguous(feeFailure)
                    ? null
                    : "refund_provider_fee_rejected";
            }
            if (observeApplicationFee && expectedApplicationFeeRefundAmountMinor > 0 &&
                !refundedApplicationFeeMinor.HasValue && applicationFeeFailureCode is null && !CanMutate(mutationExpiresAt))
                applicationFeeFailureCode = "refund_provider_retention_expired";
        }

        return RefundProviderResult.Observed(
            new(
                refundId,
                paymentId,
                status,
                refund.Amount,
                currency,
                refundedApplicationFeeMinor,
                applicationFeeFailureCode,
                allowApplicationFeeMutation ? feeHandoffStarted : null),
            requestId);
    }

    private async Task<global::Stripe.StripeClient> CreateClientAsync(CancellationToken cancellationToken)
    {
        SecretResolutionResult secret = await secretResolver.ResolveAsync(
            SecretDefinitionRegistry.Keys.Stripe.PlatformSecretKey,
            tenantId: null,
            cancellationToken);
        if (string.IsNullOrWhiteSpace(secret?.Value))
        {
            throw new RefundAdapterFailure(ConfigurationFailure("refund_provider_secret_unavailable"));
        }
        if (!secret.Value.StartsWith(options.Value.ExpectedSecretKeyPrefix, StringComparison.Ordinal))
        {
            throw new RefundAdapterFailure(ConfigurationFailure("refund_provider_secret_mode_mismatch"));
        }

        return new global::Stripe.StripeClient(new global::Stripe.StripeClientOptions
        {
            ApiKey = secret.Value,
            HttpClient = new global::Stripe.SystemNetHttpClient(
                httpClientFactory.CreateClient(HttpClientName),
                MaxNetworkRetries,
                appInfo: null,
                enableTelemetry: false)
        });
    }

    private static RefundProviderFailure? MapFailure(Exception exception) => exception switch
    {
        RefundAdapterFailure failure => failure.Failure,
        global::Stripe.StripeException stripeException => new(
            "refund_provider_rejected",
            MapFailureKind(stripeException.HttpStatusCode),
            RequestId(stripeException.StripeResponse)),
        OperationCanceledException or HttpRequestException or TimeoutException => new(
            "refund_provider_network_ambiguous",
            RefundProviderFailureKind.Network),
        _ => null
    };

    internal static RefundProviderFailureKind MapFailureKind(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
            ? RefundProviderFailureKind.Configuration
            : (int)statusCode is >= 400 and <= 499 and not (408 or 409 or 429)
                ? RefundProviderFailureKind.ProviderRejected
                : RefundProviderFailureKind.ProviderUnknown;

    private static bool IsAmbiguous(RefundProviderFailure failure) =>
        failure.Kind is RefundProviderFailureKind.Network or RefundProviderFailureKind.ProviderUnknown or RefundProviderFailureKind.ProviderDataIncomplete;

    private static RefundProviderFailure ConfigurationFailure(string code) =>
        new(code, RefundProviderFailureKind.Configuration, ProviderHandoffStarted: false);

    private static RefundProviderStatus MapStatus(string? status) => status switch
    {
        "pending" => RefundProviderStatus.Pending,
        "requires_action" => RefundProviderStatus.RequiresAction,
        "succeeded" => RefundProviderStatus.Succeeded,
        "failed" => RefundProviderStatus.Failed,
        "canceled" => RefundProviderStatus.Cancelled,
        _ => RefundProviderStatus.Unknown
    };

    private static string? NormalizeCurrency(string? value)
    {
        string normalized = value?.Trim().ToUpperInvariant() ?? string.Empty;
        return normalized.Length == 3 && normalized.All(character => character is >= 'A' and <= 'Z')
            ? normalized
            : null;
    }

    private static bool IsStripe(string providerCode) => string.Equals(providerCode, "stripe", StringComparison.Ordinal);

    private bool CanMutate(DateTime? expiresAt) =>
        expiresAt.HasValue && expiresAt.Value.Kind == DateTimeKind.Utc &&
        _time.GetUtcNow().UtcDateTime < expiresAt.Value;

    private static string? RequestId(global::Stripe.StripeResponse? response) => BoundedText(response?.RequestId, 120);

    private static string? BoundedText(string? value, int maxLength)
    {
        string normalized = value?.Trim() ?? string.Empty;
        return normalized.Length is 0 || normalized.Length > maxLength || normalized.Any(char.IsControl)
            ? null
            : normalized;
    }

    private sealed class RefundAdapterFailure(RefundProviderFailure failure) : Exception(failure.Code)
    {
        public RefundProviderFailure Failure { get; } = failure;
    }
}
