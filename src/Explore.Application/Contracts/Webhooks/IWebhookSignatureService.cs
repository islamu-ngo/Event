namespace Explore.Application.Contracts.Webhooks;

public interface IWebhookSignatureService
{
    WebhookSignatureHeaders Sign(
        string messageId,
        DateTimeOffset timestamp,
        ReadOnlySpan<byte> rawPayload,
        WebhookSecretMaterial secret);

    WebhookVerificationResult Verify(
        ReadOnlySpan<byte> rawPayload,
        IReadOnlyDictionary<string, string> headers,
        WebhookSecretMaterial secret);
}

public sealed record WebhookSecretMaterial(
    string CurrentSecret,
    int CurrentSecretVersion,
    string? PreviousSecret = null,
    DateTimeOffset? PreviousSecretValidUntil = null);

public sealed record WebhookSignatureHeaders(
    string SvixId,
    string SvixTimestamp,
    string SvixSignature);

public sealed record WebhookVerificationResult(
    bool IsValid,
    string? FailureCategory,
    DateTimeOffset? Timestamp)
{
    public static WebhookVerificationResult Success(DateTimeOffset timestamp) =>
        new(true, null, timestamp);

    public static WebhookVerificationResult Failure(string failureCategory) =>
        new(false, failureCategory, null);
}
