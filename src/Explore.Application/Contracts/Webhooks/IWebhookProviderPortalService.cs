using Explore.Domain;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Webhooks.Requests.Commands;

namespace Explore.Application.Contracts.Webhooks;

public interface IWebhookProviderPortalService
{
    Task<WebhookProviderPortalAccessResult> CreateAccessAsync(
        WebhookProviderPortalAccessInput input,
        CancellationToken cancellationToken);

    /// <summary>Revalidates retained persisted custody before releasing a prepared portal result.</summary>
    Task<bool> IsCurrentAsync(
        WebhookProviderPortalAccessInput input,
        WebhookProviderPortalAccessResult result,
        CancellationToken cancellationToken);
}

public sealed record WebhookProviderPortalAccessInput(
    Guid ConsumerId,
    string SessionId,
    TimeSpan? ExpiresIn,
    WebhookPortalConsumerCustody ExpectedConsumer)
{
    /// <summary>The handler supplies the actual original command and prepared human, never audit attribution.</summary>
    public OpenSvixAppPortalCommand? OriginalCommand { get; init; }
    public IPreparedHumanWebhookPortalRequester? OriginalRequester { get; init; }
}

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
    public WebhookPortalBindingCustody? BindingCustody { get; init; }
    public WebhookPortalOptionsCustody? OptionsCustody { get; init; }

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
