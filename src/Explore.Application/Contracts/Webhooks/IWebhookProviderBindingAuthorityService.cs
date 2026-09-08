using Explore.Domain;

namespace Explore.Application.Contracts.Webhooks;

public sealed record WebhookProviderBindingProfile(
    WebhookProviderKind ProviderKind,
    string ProviderEnvironment,
    WebhookProviderCapabilityProfile CapabilityProfile,
    WebhookProviderCapability GovernanceAllowedCapabilities);

public sealed record WebhookProviderBindingProfileResult(
    WebhookProviderBindingProfile? Profile,
    string? FailureCategory)
{
    public bool Succeeded => Profile is not null;

    public static WebhookProviderBindingProfileResult Success(WebhookProviderBindingProfile profile) =>
        new(profile, null);

    public static WebhookProviderBindingProfileResult Failure(string failureCategory) =>
        new(null, failureCategory);
}

public sealed record WebhookProviderBindingOwnershipRequest(
    WebhookOwnershipScope Ownership,
    Guid WebhookConsumerId,
    string ApplicationUid,
    string ExternalApplicationId,
    WebhookProviderKind ProviderKind,
    string ProviderEnvironment,
    string ProviderVersion,
    string CapabilityPolicyVersion);

public sealed record WebhookProviderBindingOwnershipResult(
    bool Succeeded,
    bool IsRetryable,
    string? FailureCategory)
{
    public static WebhookProviderBindingOwnershipResult Success() =>
        new(true, false, null);

    public static WebhookProviderBindingOwnershipResult Failure(
        string failureCategory,
        bool isRetryable) =>
        new(false, isRetryable, failureCategory);
}

public interface IWebhookProviderBindingAuthorityService
{
    WebhookProviderBindingProfileResult ResolveCurrentProfile();

    Task<WebhookProviderBindingOwnershipResult> VerifyOwnershipAsync(
        WebhookProviderBindingOwnershipRequest request,
        CancellationToken cancellationToken);
}
