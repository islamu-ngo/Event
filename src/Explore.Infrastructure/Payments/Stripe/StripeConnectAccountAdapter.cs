using System.Net;
using System.Text.Json;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Contracts.Services;
using Explore.Domain.Secrets;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Payments.Stripe;

public sealed class StripeConnectAccountAdapter(
    IHttpClientFactory httpClientFactory,
    ISecretResolver secretResolver,
    TimeProvider timeProvider,
    IOptions<StripePaymentOptions> options) : IOrganizerPaymentOnboardingProvider
{
    public const string HttpClientName = "Payments.StripeConnect";
    private const int MaxNetworkRetries = 2;

    public async Task<OrganizerPaymentProviderAccountCreationResult> CreateAccountAsync(
        OrganizerPaymentProviderAccountCreationRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsStripeProvider(request.ProviderCode))
        {
            return OrganizerPaymentProviderAccountCreationResult.Failed(
                "organizer_payment_provider_unsupported",
                OrganizerPaymentProviderFailureKind.Configuration);
        }

        try
        {
            global::Stripe.Account account = await CreateAccountService(await CreateClientAsync(cancellationToken))
                .CreateAsync(
                    new global::Stripe.AccountCreateOptions
                    {
                        Capabilities = new global::Stripe.AccountCapabilitiesOptions
                        {
                            CardPayments = new global::Stripe.AccountCapabilitiesCardPaymentsOptions { Requested = true },
                            Transfers = new global::Stripe.AccountCapabilitiesTransfersOptions { Requested = true }
                        },
                        Controller = new global::Stripe.AccountControllerOptions
                        {
                            Fees = new global::Stripe.AccountControllerFeesOptions { Payer = "application" },
                            Losses = new global::Stripe.AccountControllerLossesOptions { Payments = "application" },
                            StripeDashboard = new global::Stripe.AccountControllerStripeDashboardOptions { Type = "express" }
                        },
                        Metadata = new Dictionary<string, string>
                        {
                            ["islamu_tenant_id"] = request.TenantId.ToString("D"),
                            ["islamu_organizer_actor_id"] = request.OrganizerActorId.ToString("D"),
                            ["islamu_connect_platform_id"] = request.ConnectPlatformId
                        }
                    },
                    new global::Stripe.RequestOptions
                    {
                        IdempotencyKey = NormalizeProviderIdempotencyKey(request.ProviderIdempotencyKey)
                    },
                    cancellationToken);

            string? requestId = RequestId(account.StripeResponse);
            if (!TryValidateAccountMode(account, out string? modeFailureCode))
            {
                return OrganizerPaymentProviderAccountCreationResult.ManualReconciliationRequired(
                    modeFailureCode,
                    OrganizerPaymentProviderFailureKind.ProviderDataIncomplete,
                    requestId);
            }

            return string.IsNullOrWhiteSpace(account.Id)
                ? OrganizerPaymentProviderAccountCreationResult.ManualReconciliationRequired()
                : OrganizerPaymentProviderAccountCreationResult.Created(account.Id);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (MapException(exception) is { } failure)
        {
            return IsAmbiguousAccountCreation(failure.Kind)
                ? OrganizerPaymentProviderAccountCreationResult.ManualReconciliationRequired(failure.Code, failure.Kind, failure.ProviderRequestId)
                : OrganizerPaymentProviderAccountCreationResult.Failed(failure.Code, failure.Kind, failure.ProviderRequestId);
        }
    }

    public async Task<OrganizerPaymentOnboardingLinkCreationResult> CreateOnboardingLinkAsync(
        OrganizerPaymentOnboardingLinkRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsStripeProvider(request.ProviderCode))
        {
            return OrganizerPaymentOnboardingLinkCreationResult.Failed(
                "organizer_payment_provider_unsupported",
                OrganizerPaymentProviderFailureKind.Configuration);
        }

        try
        {
            global::Stripe.AccountLink link = await CreateAccountLinkService(await CreateClientAsync(cancellationToken))
                .CreateAsync(
                    new global::Stripe.AccountLinkCreateOptions
                    {
                        Account = request.ExternalAccountId,
                        ReturnUrl = request.ReturnUrl.ToString(),
                        RefreshUrl = request.RefreshUrl.ToString(),
                        Type = "account_onboarding"
                    },
                    requestOptions: null,
                    cancellationToken);

            return Uri.TryCreate(link.Url, UriKind.Absolute, out Uri? onboardingUrl)
                ? OrganizerPaymentOnboardingLinkCreationResult.Created(onboardingUrl)
                : OrganizerPaymentOnboardingLinkCreationResult.Failed(
                    "organizer_payment_provider_link_missing_url",
                    OrganizerPaymentProviderFailureKind.ProviderDataIncomplete,
                    RequestId(link.StripeResponse));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (MapException(exception) is { } failure)
        {
            return OrganizerPaymentOnboardingLinkCreationResult.Failed(failure.Code, failure.Kind, failure.ProviderRequestId);
        }
    }

    public async Task<OrganizerPaymentProviderReadinessResult> GetReadinessAsync(
        OrganizerPaymentProviderReadinessRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsStripeProvider(request.ProviderCode))
        {
            return OrganizerPaymentProviderReadinessResult.Failed(
                "organizer_payment_provider_unsupported",
                OrganizerPaymentProviderFailureKind.Configuration);
        }

        try
        {
            global::Stripe.Account account = await CreateAccountService(await CreateClientAsync(cancellationToken))
                .GetAsync(request.ExternalAccountId, options: null, requestOptions: null, cancellationToken);
            string? requestId = RequestId(account.StripeResponse);

            if (!TryValidateAccountMode(account, out string? modeFailureCode))
            {
                return OrganizerPaymentProviderReadinessResult.Failed(
                    modeFailureCode,
                    OrganizerPaymentProviderFailureKind.ProviderDataIncomplete,
                    requestId);
            }

            return OrganizerPaymentProviderReadinessResult.Retrieved(
                StripeConnectReadinessMapper.MapAccountUpdated(account, requestId, timeProvider.GetUtcNow().UtcDateTime),
                requestId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (MapException(exception) is { } failure)
        {
            return OrganizerPaymentProviderReadinessResult.Failed(failure.Code, failure.Kind, failure.ProviderRequestId);
        }
    }

    private async Task<global::Stripe.StripeClient> CreateClientAsync(CancellationToken cancellationToken)
    {
        SecretResolutionResult secret = await secretResolver.ResolveAsync(
            SecretDefinitionRegistry.Keys.Stripe.PlatformSecretKey,
            tenantId: null,
            cancellationToken);
        if (string.IsNullOrWhiteSpace(secret?.Value))
        {
            throw new StripeAdapterFailure(
                "organizer_payment_provider_secret_unavailable",
                OrganizerPaymentProviderFailureKind.Configuration);
        }

        if (!secret.Value.StartsWith(options.Value.ExpectedSecretKeyPrefix, StringComparison.Ordinal))
        {
            throw new StripeAdapterFailure(
                "organizer_payment_provider_secret_mode_mismatch",
                OrganizerPaymentProviderFailureKind.Configuration);
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

    private static global::Stripe.AccountService CreateAccountService(global::Stripe.StripeClient client) => new(client);

    private static global::Stripe.AccountLinkService CreateAccountLinkService(global::Stripe.StripeClient client) => new(client);

    private static bool IsStripeProvider(string providerCode) => string.Equals(providerCode, "stripe", StringComparison.Ordinal);

    private static bool IsAmbiguousAccountCreation(OrganizerPaymentProviderFailureKind kind) =>
        kind is OrganizerPaymentProviderFailureKind.Network or OrganizerPaymentProviderFailureKind.ProviderUnknown;

    private static string NormalizeProviderIdempotencyKey(string providerIdempotencyKey)
    {
        string normalized = providerIdempotencyKey?.Trim() ?? string.Empty;
        return normalized.Length is > 0 and <= 80 && !normalized.Any(char.IsControl)
            ? normalized
            : throw new StripeAdapterFailure(
                "organizer_payment_provider_idempotency_key_invalid",
                OrganizerPaymentProviderFailureKind.Configuration);
    }

    internal static string? BoundedText(string? value, int maxLength)
    {
        string normalized = value?.Trim() ?? string.Empty;
        return normalized.Length is 0 || normalized.Length > maxLength || normalized.Any(char.IsControl)
            ? null
            : normalized;
    }

    private static StripeAdapterFailure? MapException(Exception exception)
    {
        return exception switch
        {
            StripeAdapterFailure failure => failure,
            OperationCanceledException => new StripeAdapterFailure(
                "organizer_payment_provider_network_failure",
                OrganizerPaymentProviderFailureKind.Network),
            global::Stripe.StripeException stripeException => new StripeAdapterFailure(
                MapStripeFailureCode(stripeException),
                MapStripeFailureKind(stripeException.HttpStatusCode),
                RequestId(stripeException.StripeResponse)),
            HttpRequestException or TimeoutException => new StripeAdapterFailure(
                "organizer_payment_provider_network_failure",
                OrganizerPaymentProviderFailureKind.Network),
            _ => null
        };
    }

    private static string MapStripeFailureCode(global::Stripe.StripeException exception)
    {
        string? providerCode = BoundedText(exception.StripeError?.Code, 80);
        return providerCode is null
            ? "organizer_payment_provider_rejected"
            : "organizer_payment_provider_" + providerCode.Replace('-', '_');
    }

    private static OrganizerPaymentProviderFailureKind MapStripeFailureKind(HttpStatusCode statusCode) =>
        (int)statusCode is 408 or 429 || (int)statusCode >= 500
            ? OrganizerPaymentProviderFailureKind.ProviderUnknown
            : OrganizerPaymentProviderFailureKind.ProviderRejected;

    private static string? RequestId(global::Stripe.StripeResponse? response) => BoundedText(response?.RequestId, 120);

    private bool TryValidateAccountMode(global::Stripe.Account account, out string failureCode)
    {
        if (!StripeModeEvidence.TryReadLivemode(account.RawJsonElement, out bool livemode))
        {
            failureCode = "organizer_payment_provider_account_mode_missing";
            return false;
        }

        if (!StripeModeEvidence.Matches(options.Value, livemode))
        {
            failureCode = "organizer_payment_provider_account_mode_mismatch";
            return false;
        }

        failureCode = string.Empty;
        return true;
    }

    private sealed class StripeAdapterFailure(
        string code,
        OrganizerPaymentProviderFailureKind kind,
        string? providerRequestId = null) : Exception(code)
    {
        public string Code { get; } = code;
        public OrganizerPaymentProviderFailureKind Kind { get; } = kind;
        public string? ProviderRequestId { get; } = providerRequestId;
    }
}
