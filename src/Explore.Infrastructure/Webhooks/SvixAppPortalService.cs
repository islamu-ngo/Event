using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Webhooks;
using Explore.Domain;
using Explore.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Svix;

namespace Explore.Infrastructure.Webhooks;

public sealed class SvixAppPortalService(
    ISvixWebhookClient svixClient,
    IWebhookConsumerRepository consumerRepository,
    IWebhookConsumerProviderBindingRepository bindingRepository,
    IOptionsMonitor<WebhookOptions> options) : IWebhookProviderPortalService
{
    private static readonly TimeSpan DefaultExpiry = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan MinExpiry = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan MaxExpiry = TimeSpan.FromHours(1);

    public async Task<WebhookProviderPortalAccessResult> CreateAccessAsync(
        WebhookProviderPortalAccessInput input,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var currentOptions = options.CurrentValue;
        if (currentOptions.IsDisabled
            || !(currentOptions.IsProvider(WebhookOptions.ProviderSvix)
                 || currentOptions.IsProvider(WebhookOptions.ProviderComposite)))
        {
            return WebhookProviderPortalAccessResult.Failure(
                "svix_provider_not_enabled",
                isRetryable: false);
        }

        if (!currentOptions.Svix.AppPortalEnabled)
        {
            return WebhookProviderPortalAccessResult.Failure(
                "svix_app_portal_disabled",
                isRetryable: false);
        }

        if (string.IsNullOrWhiteSpace(input.SessionId))
        {
            return WebhookProviderPortalAccessResult.Failure(
                "webhook_portal_session_required",
                isRetryable: false);
        }

        try
        {
            var consumer = await ResolveConsumerAsync(input, cancellationToken);
            if (consumer is null)
            {
                return WebhookProviderPortalAccessResult.Failure(
                    "webhook_consumer_not_found",
                    isRetryable: false);
            }

            if (consumer.Status != WebhookConsumerStatus.Active)
            {
                return WebhookProviderPortalAccessResult.Failure(
                    "webhook_consumer_disabled",
                    isRetryable: false);
            }

            if (consumer.ProviderMode is not (WebhookProviderMode.Svix or WebhookProviderMode.Composite))
            {
                return WebhookProviderPortalAccessResult.Failure(
                    "webhook_provider_binding_mismatched",
                    isRetryable: false);
            }

            var binding = await bindingRepository.GetVerifiedByConsumerAsync(
                consumer.TenantId,
                input.ConsumerId,
                WebhookProviderKind.Svix,
                currentOptions.Svix.Environment,
                cancellationToken);
            if (binding is null)
            {
                return WebhookProviderPortalAccessResult.Failure(
                    "webhook_provider_binding_unverified",
                    isRetryable: false);
            }

            if (!SvixPortalAuthorityPolicy.AllowsBinding(
                    binding,
                    currentOptions,
                    consumer.TenantId,
                    input.ConsumerId))
            {
                return WebhookProviderPortalAccessResult.Failure(
                    "webhook_provider_capability_unavailable",
                    isRetryable: false);
            }

            var providerApplicationId = binding.ExternalApplicationId;
            if (string.IsNullOrWhiteSpace(providerApplicationId))
            {
                return WebhookProviderPortalAccessResult.Failure(
                    "webhook_provider_binding_unverified",
                    isRetryable: false);
            }

            var app = await svixClient.GetApplicationAsync(providerApplicationId, cancellationToken);
            if (!SvixWebhookApplicationMapper.IsVerifiedConsumerBinding(app, consumer, binding))
            {
                return WebhookProviderPortalAccessResult.Failure(
                    "webhook_provider_binding_mismatched",
                    isRetryable: false);
            }

            var expiresIn = NormalizeExpiry(input.ExpiresIn);
            var readOnly = !binding.SupportsGoverned(WebhookProviderCapability.EndpointManagement);
            var featureFlags = ResolveFeatureFlags(binding);
            var portal = await svixClient.CreateAppPortalAccessAsync(
                new SvixAppPortalAccessRequest(
                    providerApplicationId,
                    input.SessionId.Trim(),
                    readOnly,
                    expiresIn,
                    featureFlags,
                    $"svix-portal:{providerApplicationId}:{input.SessionId.Trim()}"),
                cancellationToken);

            return WebhookProviderPortalAccessResult.Success(
                portal.Url,
                portal.Token,
                DateTimeOffset.UtcNow.Add(expiresIn),
                binding.Id,
                binding.CapabilityResolutionVersion);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (SvixWebhookConfigurationException ex)
        {
            return WebhookProviderPortalAccessResult.Failure(ex.FailureCategory, isRetryable: false, ex.FailureCategory);
        }
        catch (ApiException ex) when (ex.ErrorCode == 404)
        {
            return WebhookProviderPortalAccessResult.Failure(
                "webhook_provider_binding_mismatched",
                isRetryable: false);
        }
        catch (ApiException ex)
        {
            var failure = SvixWebhookFailureClassifier.Classify(ex);
            return WebhookProviderPortalAccessResult.Failure(
                failure.Category,
                failure.IsRetryable,
                failure.SafeDetail);
        }
        catch (Exception ex)
        {
            return WebhookProviderPortalAccessResult.Failure(
                "svix_app_portal_failed",
                isRetryable: true,
                ex.GetType().Name);
        }
    }

    private async Task<WebhookConsumer?> ResolveConsumerAsync(
        WebhookProviderPortalAccessInput input,
        CancellationToken cancellationToken)
    {
        return await consumerRepository.GetByIdForOwnerOperationAsync(
            input.ConsumerId,
            forUpdate: false,
            cancellationToken);
    }

    private static TimeSpan NormalizeExpiry(TimeSpan? expiresIn)
    {
        var requested = expiresIn ?? DefaultExpiry;
        if (requested < MinExpiry)
        {
            return MinExpiry;
        }

        return requested > MaxExpiry ? MaxExpiry : requested;
    }

    private static List<string> ResolveFeatureFlags(WebhookConsumerProviderBinding binding)
    {
        var flags = new List<string> { "ViewBase" };
        if (binding.SupportsGoverned(WebhookProviderCapability.EndpointManagement))
        {
            flags.Add("ManageEndpoint");
        }

        if (binding.SupportsGoverned(WebhookProviderCapability.Replay))
        {
            flags.Add("CreateAttempts");
        }

        if (binding.SupportsGoverned(WebhookProviderCapability.Transformations))
        {
            flags.Add("ManageTransformations");
        }

        return flags;
    }
}
