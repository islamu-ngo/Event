namespace Explore.Application.Contracts.Payments;

public interface IRefundCreator
{
    Task<RefundProviderResult> CreateAsync(RefundCreateRequest request, CancellationToken cancellationToken);
}

public interface IRefundRetriever
{
    Task<RefundProviderResult> RetrieveAsync(RefundRetrieveRequest request, CancellationToken cancellationToken);
}

public sealed record RefundCreateRequest(
    Guid RefundAttemptId,
    string ProviderCode,
    string ExternalAccountId,
    string ProviderPaymentId,
    string ProviderIdempotencyKey,
    long AmountMinor,
    string CurrencyCode,
    long ApplicationFeeRefundAmountMinor)
{
    public static RefundCreateRequest Create(
        Guid refundAttemptId,
        string providerCode,
        string externalAccountId,
        string providerPaymentId,
        string providerIdempotencyKey,
        long amountMinor,
        string currencyCode,
        long applicationFeeRefundAmountMinor)
    {
        if (refundAttemptId == Guid.Empty || amountMinor <= 0 ||
            applicationFeeRefundAmountMinor < 0 || applicationFeeRefundAmountMinor > amountMinor)
        {
            throw new ArgumentException("Valid refund identity and amount are required.");
        }

        return new(
            refundAttemptId,
            Normalize(providerCode, nameof(providerCode), 80),
            Normalize(externalAccountId, nameof(externalAccountId), 200),
            Normalize(providerPaymentId, nameof(providerPaymentId), 200),
            Normalize(providerIdempotencyKey, nameof(providerIdempotencyKey), 160),
            amountMinor,
            NormalizeCurrency(currencyCode),
            applicationFeeRefundAmountMinor);
    }

    internal static string Normalize(string value, string parameterName, int maxLength)
    {
        string normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is 0 || normalized.Length > maxLength || normalized.Any(char.IsControl))
        {
            throw new ArgumentException($"Value must be non-blank and at most {maxLength} characters.", parameterName);
        }

        return normalized;
    }

    internal static string NormalizeCurrency(string value)
    {
        string normalized = value?.Trim().ToUpperInvariant() ?? string.Empty;
        if (normalized.Length != 3 || normalized.Any(character => character is < 'A' or > 'Z'))
        {
            throw new ArgumentException("Currency must be a three-letter ISO code.", nameof(value));
        }

        return normalized;
    }
}

public sealed record RefundRetrieveRequest(
    string ProviderCode,
    string ExternalAccountId,
    string ProviderPaymentId,
    string ProviderRefundId,
    string ProviderIdempotencyKey,
    long ExpectedAmountMinor,
    string ExpectedCurrencyCode,
    long ExpectedApplicationFeeRefundAmountMinor)
{
    public static RefundRetrieveRequest Create(
        string providerCode,
        string externalAccountId,
        string providerPaymentId,
        string providerRefundId,
        string providerIdempotencyKey,
        long expectedAmountMinor,
        string expectedCurrencyCode,
        long expectedApplicationFeeRefundAmountMinor)
    {
        if (expectedAmountMinor <= 0 || expectedApplicationFeeRefundAmountMinor < 0 ||
            expectedApplicationFeeRefundAmountMinor > expectedAmountMinor)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedAmountMinor));
        }

        return new(
            RefundCreateRequest.Normalize(providerCode, nameof(providerCode), 80),
            RefundCreateRequest.Normalize(externalAccountId, nameof(externalAccountId), 200),
            RefundCreateRequest.Normalize(providerPaymentId, nameof(providerPaymentId), 200),
            RefundCreateRequest.Normalize(providerRefundId, nameof(providerRefundId), 200),
            RefundCreateRequest.Normalize(providerIdempotencyKey, nameof(providerIdempotencyKey), 160),
            expectedAmountMinor,
            RefundCreateRequest.NormalizeCurrency(expectedCurrencyCode),
            expectedApplicationFeeRefundAmountMinor);
    }
}

public sealed record RefundProviderObservation(
    string ProviderRefundId,
    string ProviderPaymentId,
    RefundProviderStatus Status,
    long AmountMinor,
    string CurrencyCode,
    long? ApplicationFeeRefundAmountMinor,
    string? ApplicationFeeRefundFailureCode = null);

public sealed record RefundProviderFailure(
    string Code,
    RefundProviderFailureKind Kind,
    string? ProviderRequestId = null,
    bool ProviderHandoffStarted = true);

public sealed record RefundProviderResult(
    RefundProviderOutcome Outcome,
    RefundProviderObservation? Observation,
    RefundProviderFailure? Failure,
    string? ProviderRequestId)
{
    public static RefundProviderResult Observed(RefundProviderObservation observation, string? providerRequestId) =>
        new(RefundProviderOutcome.Observed, observation, null, providerRequestId);

    public static RefundProviderResult Failed(RefundProviderFailure failure) =>
        new(RefundProviderOutcome.Failed, null, failure, failure.ProviderRequestId);

    public static RefundProviderResult Unknown(RefundProviderFailure failure) =>
        new(RefundProviderOutcome.Unknown, null, failure, failure.ProviderRequestId);
}

public enum RefundProviderOutcome
{
    Failed = 0,
    Observed = 1,
    Unknown = 2
}

public enum RefundProviderStatus
{
    Unknown = 0,
    Pending = 1,
    RequiresAction = 2,
    Succeeded = 3,
    Failed = 4,
    Cancelled = 5
}

public enum RefundProviderFailureKind
{
    Configuration = 0,
    ProviderRejected = 1,
    ProviderUnknown = 2,
    Network = 3,
    ProviderDataIncomplete = 4
}
