namespace Explore.Application.Contracts.Webhooks;

public interface IWebhookProviderPortalService
{
    Task<WebhookProviderPortalAccessResult> CreateAccessAsync(
        WebhookProviderPortalAccessInput input,
        CancellationToken cancellationToken);
}

public sealed record WebhookProviderPortalAccessInput(
    Guid ConsumerId,
    string SessionId,
    TimeSpan? ExpiresIn);

public sealed record WebhookProviderPortalAccessResult(
    bool Succeeded,
    string? Url,
    string? Token,
    DateTimeOffset? ExpiresAt,
    Guid? ProviderBindingId,
    string? CapabilityPolicyVersion,
    bool IsRetryable,
    string? FailureCategory,
    string? SafeDetail)
{
    public static WebhookProviderPortalAccessResult Success(
        string url,
        string? token,
        DateTimeOffset expiresAt,
        Guid providerBindingId,
        string capabilityPolicyVersion) =>
        new(true, url, token, expiresAt, providerBindingId, capabilityPolicyVersion, false, null, null);

    public static WebhookProviderPortalAccessResult Failure(
        string failureCategory,
        bool isRetryable,
        string? safeDetail = null) =>
        new(false, null, null, null, null, null, isRetryable, failureCategory, safeDetail);
}
