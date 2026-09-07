namespace Explore.Application.Contracts.Payments;

public interface IHostedCheckoutSessionCreator
{
    Task<HostedCheckoutCreateResult> CreateAsync(HostedCheckoutCreateRequest request, CancellationToken cancellationToken);
}

public interface IHostedCheckoutSessionRetriever
{
    Task<HostedCheckoutRetrieveResult> RetrieveAsync(HostedCheckoutRetrieveRequest request, CancellationToken cancellationToken);
}

public interface IPaymentIntentRetriever
{
    Task<PaymentIntentRetrieveResult> RetrievePaymentIntentAsync(
        PaymentIntentRetrieveRequest request,
        CancellationToken cancellationToken);
}

public interface IPaymentCancellationProvider
{
    Task<PaymentCancellationProviderResult> CancelAsync(
        PaymentCancellationRequest request,
        CancellationToken cancellationToken);
}

public sealed record PaymentCancellationRequest(
    string ProviderCode,
    string ExternalAccountId,
    string? ProviderCheckoutSessionId,
    string? ProviderPaymentId,
    string ProviderIdempotencyKey)
{
    public static PaymentCancellationRequest Create(
        string providerCode,
        string externalAccountId,
        string? providerCheckoutSessionId,
        string? providerPaymentId,
        string providerIdempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(providerCheckoutSessionId) && string.IsNullOrWhiteSpace(providerPaymentId))
        {
            throw new ArgumentException("Provider checkout or payment identity is required.");
        }
        return new(
            HostedCheckoutCreateRequest.NormalizeIdentifier(providerCode, nameof(providerCode), 80),
            HostedCheckoutCreateRequest.NormalizeIdentifier(externalAccountId, nameof(externalAccountId), 200),
            NormalizeOptional(providerCheckoutSessionId, nameof(providerCheckoutSessionId)),
            NormalizeOptional(providerPaymentId, nameof(providerPaymentId)),
            HostedCheckoutCreateRequest.NormalizeIdentifier(providerIdempotencyKey, nameof(providerIdempotencyKey), 160));
    }

    private static string? NormalizeOptional(string? value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : HostedCheckoutCreateRequest.NormalizeIdentifier(value, parameterName, 200);
}

public enum PaymentCancellationProviderOutcome
{
    Cancelled = 0,
    Captured = 1,
    Failed = 2,
    Unknown = 3
}

public sealed record PaymentCancellationProviderResult(
    PaymentCancellationProviderOutcome Outcome,
    string? ProviderRequestId,
    string? FailureCode = null,
    bool ProviderHandoffStarted = true);

public interface IPaymentProviderDescriptor
{
    PaymentProviderDescriptor Describe();
}

public sealed record PaymentProviderDescriptor(
    string ProviderCode,
    string ProfileCode,
    string ApiRevision,
    string Environment,
    string CredentialOwner);

public sealed class HostedCheckoutCreateRequest
{
    private HostedCheckoutCreateRequest(
        Guid paymentAttemptId,
        Guid registrationOrderId,
        string providerCode,
        string externalAccountId,
        string providerIdempotencyKey,
        string currencyCode,
        long totalMinor,
        long applicationFeeMinor,
        DateTime expiresAt,
        Uri successUrl,
        Uri cancelUrl)
    {
        PaymentAttemptId = paymentAttemptId;
        RegistrationOrderId = registrationOrderId;
        ProviderCode = providerCode;
        ExternalAccountId = externalAccountId;
        ProviderIdempotencyKey = providerIdempotencyKey;
        CurrencyCode = currencyCode;
        TotalMinor = totalMinor;
        ApplicationFeeMinor = applicationFeeMinor;
        ExpiresAt = expiresAt;
        SuccessUrl = successUrl;
        CancelUrl = cancelUrl;
    }

    public Guid PaymentAttemptId { get; }
    public Guid RegistrationOrderId { get; }
    public string ProviderCode { get; }
    public string ExternalAccountId { get; }
    public string ProviderIdempotencyKey { get; }
    public string CurrencyCode { get; }
    public long TotalMinor { get; }
    public long ApplicationFeeMinor { get; }
    public DateTime ExpiresAt { get; }
    public Uri SuccessUrl { get; }
    public Uri CancelUrl { get; }

    public static HostedCheckoutCreateRequest Create(
        Guid paymentAttemptId,
        Guid registrationOrderId,
        string providerCode,
        string externalAccountId,
        string providerIdempotencyKey,
        string currencyCode,
        long totalMinor,
        long applicationFeeMinor,
        DateTime expiresAt,
        Uri allowedReturnOrigin,
        Uri successUrl,
        Uri cancelUrl)
    {
        if (paymentAttemptId == Guid.Empty || registrationOrderId == Guid.Empty)
        {
            throw new ArgumentException("Checkout identities are required.");
        }

        string provider = NormalizeIdentifier(providerCode, nameof(providerCode), 80);
        string account = NormalizeIdentifier(externalAccountId, nameof(externalAccountId), 200);
        string idempotency = NormalizeIdentifier(providerIdempotencyKey, nameof(providerIdempotencyKey), 160);
        string currency = NormalizeCurrency(currencyCode);
        if (totalMinor <= 0 || applicationFeeMinor < 0 || applicationFeeMinor > totalMinor)
        {
            throw new ArgumentException("Checkout money composition is invalid.");
        }

        if (expiresAt.Kind == DateTimeKind.Local)
        {
            throw new ArgumentException("Checkout expiry must be UTC.", nameof(expiresAt));
        }

        DateTime utcExpiresAt = DateTime.SpecifyKind(expiresAt, DateTimeKind.Utc);

        HostedCheckoutReturnUrls returnUrls = HostedCheckoutReturnUrls.Create(allowedReturnOrigin, successUrl, cancelUrl);
        return new(
            paymentAttemptId,
            registrationOrderId,
            provider,
            account,
            idempotency,
            currency,
            totalMinor,
            applicationFeeMinor,
            utcExpiresAt,
            returnUrls.SuccessUrl,
            returnUrls.CancelUrl);
    }

    internal static string NormalizeIdentifier(string value, string parameterName, int maxLength)
    {
        string normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is 0 || normalized.Length > maxLength || normalized.Any(char.IsControl))
        {
            throw new ArgumentException($"Value must be non-blank and at most {maxLength} characters.", parameterName);
        }

        return normalized;
    }

    private static string NormalizeCurrency(string value)
    {
        string normalized = value?.Trim().ToUpperInvariant() ?? string.Empty;
        if (normalized.Length != 3 || normalized.Any(character => character is < 'A' or > 'Z'))
        {
            throw new ArgumentException("Currency must be a three-letter ISO code.", nameof(value));
        }

        return normalized;
    }
}

public sealed class HostedCheckoutReturnUrls
{
    private HostedCheckoutReturnUrls(Uri successUrl, Uri cancelUrl)
    {
        SuccessUrl = successUrl;
        CancelUrl = cancelUrl;
    }

    public Uri SuccessUrl { get; }
    public Uri CancelUrl { get; }

    public static HostedCheckoutReturnUrls Create(Uri allowedOrigin, Uri successUrl, Uri cancelUrl)
    {
        ArgumentNullException.ThrowIfNull(allowedOrigin);
        ArgumentNullException.ThrowIfNull(successUrl);
        ArgumentNullException.ThrowIfNull(cancelUrl);
        if (!IsHttpsOrigin(allowedOrigin) || !IsSameOrigin(allowedOrigin, successUrl) || !IsSameOrigin(allowedOrigin, cancelUrl))
        {
            throw new ArgumentException("Checkout return URLs must use the allowed HTTPS origin.");
        }

        return new(successUrl, cancelUrl);
    }

    public static bool TryNormalizePublicBaseUrl(string? value, out Uri normalized)
    {
        normalized = null!;
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        var builder = new UriBuilder(uri)
        {
            Path = uri.AbsolutePath == "/" ? "/" : uri.AbsolutePath.TrimEnd('/') + "/",
            Query = string.Empty,
            Fragment = string.Empty
        };
        normalized = builder.Uri;
        return true;
    }

    private static bool IsHttpsOrigin(Uri value) =>
        value.IsAbsoluteUri &&
        value.Scheme == Uri.UriSchemeHttps &&
        string.IsNullOrEmpty(value.UserInfo) &&
        string.IsNullOrEmpty(value.Query) &&
        string.IsNullOrEmpty(value.Fragment) &&
        value.AbsolutePath.EndsWith("/", StringComparison.Ordinal);

    private static bool IsSameOrigin(Uri origin, Uri value) =>
        value.IsAbsoluteUri &&
        value.Scheme == Uri.UriSchemeHttps &&
        string.IsNullOrEmpty(value.UserInfo) &&
        string.IsNullOrEmpty(value.Query) &&
        string.IsNullOrEmpty(value.Fragment) &&
        string.Equals(origin.Scheme, value.Scheme, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(origin.IdnHost, value.IdnHost, StringComparison.OrdinalIgnoreCase) &&
        origin.Port == value.Port &&
        value.AbsolutePath.StartsWith(origin.AbsolutePath, StringComparison.Ordinal);
}

public sealed class HostedCheckoutRetrieveRequest
{
    private HostedCheckoutRetrieveRequest(string providerCode, string externalAccountId, string providerCheckoutSessionId)
    {
        ProviderCode = providerCode;
        ExternalAccountId = externalAccountId;
        ProviderCheckoutSessionId = providerCheckoutSessionId;
    }

    public string ProviderCode { get; }
    public string ExternalAccountId { get; }
    public string ProviderCheckoutSessionId { get; }

    public static HostedCheckoutRetrieveRequest Create(
        string providerCode,
        string externalAccountId,
        string providerCheckoutSessionId) =>
        new(
            HostedCheckoutCreateRequest.NormalizeIdentifier(providerCode, nameof(providerCode), 80),
            HostedCheckoutCreateRequest.NormalizeIdentifier(externalAccountId, nameof(externalAccountId), 200),
            HostedCheckoutCreateRequest.NormalizeIdentifier(providerCheckoutSessionId, nameof(providerCheckoutSessionId), 200));
}

public sealed record HostedCheckoutSession(
    string SessionId,
    Uri? HostedUrl,
    HostedCheckoutSessionStatus Status,
    HostedCheckoutPaymentStatus PaymentStatus,
    string? PaymentId,
    DateTime? ExpiresAt,
    long? AmountTotalMinor = null,
    string? CurrencyCode = null);

public sealed record HostedCheckoutFailure(
    string Code,
    HostedCheckoutFailureKind Kind,
    string? Type = null,
    string? ProviderCode = null,
    int? HttpStatusCode = null,
    string? ProviderRequestId = null,
    bool ProviderHandoffStarted = true,
    HostedCheckoutPreHandoffDisposition PreHandoffDisposition = HostedCheckoutPreHandoffDisposition.None);

public sealed class PaymentIntentRetrieveRequest
{
    private PaymentIntentRetrieveRequest(string providerCode, string externalAccountId, string paymentIntentId)
    {
        ProviderCode = providerCode;
        ExternalAccountId = externalAccountId;
        PaymentIntentId = paymentIntentId;
    }

    public string ProviderCode { get; }
    public string ExternalAccountId { get; }
    public string PaymentIntentId { get; }

    public static PaymentIntentRetrieveRequest Create(string providerCode, string externalAccountId, string paymentIntentId) => new(
        HostedCheckoutCreateRequest.NormalizeIdentifier(providerCode, nameof(providerCode), 80),
        HostedCheckoutCreateRequest.NormalizeIdentifier(externalAccountId, nameof(externalAccountId), 200),
        HostedCheckoutCreateRequest.NormalizeIdentifier(paymentIntentId, nameof(paymentIntentId), 200));
}

public sealed record PaymentIntentObservation(
    string PaymentIntentId,
    long AmountMinor,
    string CurrencyCode,
    long? ApplicationFeeMinor,
    PaymentIntentStatus Status);

public sealed record PaymentIntentRetrieveResult(
    HostedCheckoutOperationOutcome Outcome,
    PaymentIntentObservation? PaymentIntent,
    HostedCheckoutFailure? Failure,
    string? ProviderRequestId)
{
    public static PaymentIntentRetrieveResult Succeeded(PaymentIntentObservation paymentIntent, string? providerRequestId) =>
        new(HostedCheckoutOperationOutcome.Succeeded, paymentIntent, null, providerRequestId);

    public static PaymentIntentRetrieveResult Failed(HostedCheckoutFailure failure) =>
        new(HostedCheckoutOperationOutcome.Failed, null, failure, failure.ProviderRequestId);

    public static PaymentIntentRetrieveResult Unknown(HostedCheckoutFailure failure) =>
        new(HostedCheckoutOperationOutcome.Unknown, null, failure, failure.ProviderRequestId);
}

public sealed record HostedCheckoutCreateResult(
    HostedCheckoutOperationOutcome Outcome,
    HostedCheckoutSession? Session,
    HostedCheckoutFailure? Failure,
    string? ProviderRequestId)
{
    public static HostedCheckoutCreateResult Succeeded(HostedCheckoutSession session, string? providerRequestId) =>
        new(HostedCheckoutOperationOutcome.Succeeded, session, null, providerRequestId);

    public static HostedCheckoutCreateResult Failed(HostedCheckoutFailure failure) =>
        new(HostedCheckoutOperationOutcome.Failed, null, failure, failure.ProviderRequestId);

    public static HostedCheckoutCreateResult Unknown(HostedCheckoutFailure failure) =>
        new(HostedCheckoutOperationOutcome.Unknown, null, failure, failure.ProviderRequestId);
}

public sealed record HostedCheckoutRetrieveResult(
    HostedCheckoutOperationOutcome Outcome,
    HostedCheckoutSession? Session,
    HostedCheckoutFailure? Failure,
    string? ProviderRequestId)
{
    public static HostedCheckoutRetrieveResult Succeeded(HostedCheckoutSession session, string? providerRequestId) =>
        new(HostedCheckoutOperationOutcome.Succeeded, session, null, providerRequestId);

    public static HostedCheckoutRetrieveResult Failed(HostedCheckoutFailure failure) =>
        new(HostedCheckoutOperationOutcome.Failed, null, failure, failure.ProviderRequestId);

    public static HostedCheckoutRetrieveResult Unknown(HostedCheckoutFailure failure) =>
        new(HostedCheckoutOperationOutcome.Unknown, null, failure, failure.ProviderRequestId);
}

public enum HostedCheckoutOperationOutcome
{
    Failed = 0,
    Succeeded = 1,
    Unknown = 2
}

public enum HostedCheckoutFailureKind
{
    Configuration = 0,
    ProviderRejected = 1,
    ProviderUnknown = 2,
    Network = 3,
    ProviderDataIncomplete = 4
}

public enum HostedCheckoutPreHandoffDisposition
{
    None = 0,
    Transient = 1,
    Permanent = 2
}

public enum HostedCheckoutSessionStatus
{
    Unknown = 0,
    Open = 1,
    Complete = 2,
    Expired = 3
}

public enum HostedCheckoutPaymentStatus
{
    Unknown = 0,
    Unpaid = 1,
    Paid = 2,
    NoPaymentRequired = 3
}

public enum PaymentIntentStatus
{
    Unknown = 0,
    RequiresPaymentMethod = 1,
    RequiresConfirmation = 2,
    RequiresAction = 3,
    Processing = 4,
    RequiresCapture = 5,
    Canceled = 6,
    Succeeded = 7
}
