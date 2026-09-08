using Explore.Application.Contracts.Webhooks;
using Explore.Domain;
using Explore.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Svix;

namespace Explore.Infrastructure.Webhooks;

public sealed class SvixWebhookProviderBindingAuthorityService(
    ISvixWebhookClient svixClient,
    IOptionsMonitor<WebhookOptions> options,
    TimeProvider timeProvider) : IWebhookProviderBindingAuthorityService
{
    public WebhookProviderBindingProfileResult ResolveCurrentProfile()
    {
        var current = options.CurrentValue;
        if (current.IsDisabled ||
            !current.IsProvider(WebhookOptions.ProviderSvix) &&
            !current.IsProvider(WebhookOptions.ProviderComposite))
        {
            return WebhookProviderBindingProfileResult.Failure("svix_provider_not_enabled");
        }

        if (string.IsNullOrWhiteSpace(current.Svix.BaseUrl) ||
            !SvixConformanceProfileRegistry.TryResolve(
                current.Svix.Environment,
                current.Svix.ProviderVersion,
                current.Svix.CapabilityPolicyVersion,
                baseUrlConfigured: true,
                out var conformanceProfile) ||
            conformanceProfile is not { IsVerified: true, DeploymentKind: SvixDeploymentKind.SelfHosted })
        {
            return WebhookProviderBindingProfileResult.Failure("svix_self_hosted_profile_unsupported");
        }

        var capabilityProfile = WebhookProviderCapabilityProfile.Create(
            WebhookProviderKind.Svix,
            conformanceProfile.ProviderVersion,
            conformanceProfile.Capabilities,
            conformanceProfile.CapabilityPolicyVersion,
            timeProvider.GetUtcNow());
        return WebhookProviderBindingProfileResult.Success(new WebhookProviderBindingProfile(
            WebhookProviderKind.Svix,
            conformanceProfile.Environment,
            capabilityProfile,
            conformanceProfile.Capabilities));
    }

    public async Task<WebhookProviderBindingOwnershipResult> VerifyOwnershipAsync(
        WebhookProviderBindingOwnershipRequest request,
        CancellationToken cancellationToken)
    {
        var profileResult = ResolveCurrentProfile();
        if (profileResult.Profile is not { } profile || !MatchesProfile(request, profile))
        {
            return WebhookProviderBindingOwnershipResult.Failure(
                profileResult.FailureCategory ?? "svix_binding_profile_mismatch",
                isRetryable: false);
        }

        try
        {
            var application = await svixClient.GetApplicationAsync(
                request.ExternalApplicationId,
                cancellationToken);
            var matches = string.Equals(
                    application.AppId,
                    request.ExternalApplicationId,
                    StringComparison.Ordinal) &&
                string.Equals(application.AppUid, request.ApplicationUid, StringComparison.Ordinal) &&
                SvixWebhookOwnershipMetadata.Matches(
                    application.Metadata,
                    request.Ownership,
                    request.WebhookConsumerId);

            return matches
                ? WebhookProviderBindingOwnershipResult.Success()
                : WebhookProviderBindingOwnershipResult.Failure(
                    "webhook_provider_binding_mismatched",
                    isRetryable: false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (SvixWebhookConfigurationException exception)
        {
            return WebhookProviderBindingOwnershipResult.Failure(
                exception.FailureCategory,
                isRetryable: false);
        }
        catch (ApiException exception) when (exception.ErrorCode == 404)
        {
            return WebhookProviderBindingOwnershipResult.Failure(
                "webhook_provider_application_not_found",
                isRetryable: false);
        }
        catch (ApiException exception)
        {
            var failure = SvixWebhookFailureClassifier.Classify(exception);
            return WebhookProviderBindingOwnershipResult.Failure(
                failure.Category,
                failure.IsRetryable);
        }
        catch (Exception)
        {
            return WebhookProviderBindingOwnershipResult.Failure(
                "svix_provider_unavailable",
                isRetryable: true);
        }
    }

    private static bool MatchesProfile(
        WebhookProviderBindingOwnershipRequest request,
        WebhookProviderBindingProfile profile) =>
        request.ProviderKind == profile.ProviderKind &&
        string.Equals(
            request.ProviderEnvironment.Trim(),
            profile.ProviderEnvironment,
            StringComparison.OrdinalIgnoreCase) &&
        string.Equals(
            request.ProviderVersion.Trim(),
            profile.CapabilityProfile.ProviderVersion,
            StringComparison.Ordinal) &&
        string.Equals(
            request.CapabilityPolicyVersion.Trim(),
            profile.CapabilityProfile.ResolutionVersion,
            StringComparison.Ordinal);

}
