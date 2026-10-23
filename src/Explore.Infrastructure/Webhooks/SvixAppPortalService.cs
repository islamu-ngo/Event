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
    IOptionsMonitor<WebhookOptions> options,
    TimeProvider clock) : IWebhookProviderPortalService
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

        var optionsCustody = CaptureOptions(currentOptions);
        var consumer = await ResolveConsumerAsync(input, cancellationToken);
        if (consumer is null)
        {
            return WebhookProviderPortalAccessResult.Failure(
                "webhook_consumer_not_found",
                isRetryable: false);
        }

        if (WebhookPortalConsumerCustody.From(consumer) != input.ExpectedConsumer)
        {
            return CustodyLost();
        }

        if (consumer.Status != WebhookConsumerStatus.Active)
        {
            return WebhookProviderPortalAccessResult.Failure(
                "webhook_consumer_disabled",
                isRetryable: false);
        }

        if (consumer.ProviderMode is not (WebhookProviderMode.Svix or WebhookProviderMode.Composite))
        {
            return CustodyLost();
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

        var bindingCustody = WebhookPortalBindingCustody.From(binding);
        if (!await IsCurrentAsync(input, bindingCustody, optionsCustody, null, cancellationToken))
        {
            return CustodyLost();
        }

        var (app, applicationFailure) = await CallRemoteAsync(
            () => svixClient.GetApplicationAsync(providerApplicationId, cancellationToken));
        if (!await IsCurrentAsync(input, bindingCustody, optionsCustody, null, cancellationToken))
        {
            return CustodyLost();
        }
        if (applicationFailure is not null)
        {
            return applicationFailure;
        }
        if (!SvixWebhookApplicationMapper.IsVerifiedConsumerBinding(app!, consumer, binding))
        {
            return CustodyLost();
        }

        var expiresIn = NormalizeExpiry(input.ExpiresIn);
        var readOnly = !binding.SupportsGoverned(WebhookProviderCapability.EndpointManagement);
        var featureFlags = ResolveFeatureFlags(binding);
        // Conservatively anchor the requested lifetime before remote issuance. A slow
        // response must never extend it by adding the duration at response arrival.
        var expiresAt = clock.GetUtcNow().Add(expiresIn);
        var (portal, portalFailure) = await CallRemoteAsync(() => svixClient.CreateAppPortalAccessAsync(
            new SvixAppPortalAccessRequest(
                providerApplicationId,
                input.SessionId.Trim(),
                readOnly,
                expiresIn,
                featureFlags,
                $"svix-portal:{providerApplicationId}:{input.SessionId.Trim()}"),
            cancellationToken));

        if (!await IsCurrentAsync(input, bindingCustody, optionsCustody, expiresAt, cancellationToken))
        {
            return CustodyLost();
        }
        if (portalFailure is not null)
        {
            return portalFailure;
        }

        return WebhookProviderPortalAccessResult.Success(
            portal!.Url,
            portal.Token,
            expiresAt,
            binding.Id,
            binding.CapabilityResolutionVersion) with
        {
            BindingCustody = bindingCustody,
            OptionsCustody = optionsCustody
        };
    }

    private static async Task<(T? Value, WebhookProviderPortalAccessResult? Failure)> CallRemoteAsync<T>(
        Func<Task<T>> remote) where T : class
    {
        try
        {
            return (await remote(), null);
        }
        catch (SvixWebhookConfigurationException ex)
        {
            return (null, WebhookProviderPortalAccessResult.Failure(ex.FailureCategory, isRetryable: false, ex.FailureCategory));
        }
        catch (ApiException ex) when (ex.ErrorCode == 404)
        {
            return (null, WebhookProviderPortalAccessResult.Failure(
                "webhook_provider_binding_mismatched",
                isRetryable: false));
        }
        catch (ApiException ex)
        {
            var failure = SvixWebhookFailureClassifier.Classify(ex);
            return (null, WebhookProviderPortalAccessResult.Failure(
                failure.Category,
                failure.IsRetryable,
                failure.SafeDetail));
        }
    }

    public Task<bool> IsCurrentAsync(
        WebhookProviderPortalAccessInput input,
        WebhookProviderPortalAccessResult result,
        CancellationToken cancellationToken) =>
        result is { Succeeded: true, BindingCustody: { } binding, OptionsCustody: { } retainedOptions, ExpiresAt: { } expiresAt }
            ? IsCurrentAsync(input, binding, retainedOptions, expiresAt, cancellationToken)
            : Task.FromResult(false);

    private async Task<bool> IsCurrentAsync(
        WebhookProviderPortalAccessInput input,
        WebhookPortalBindingCustody expectedBinding,
        WebhookPortalOptionsCustody expectedOptions,
        DateTimeOffset? expiresAt,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (input.ConsumerId != input.ExpectedConsumer.ConsumerId)
        {
            return false;
        }

        WebhookConsumer? consumer = null;
        WebhookConsumerProviderBinding? binding = null;
        bool current = false;
        async Task ReadProvider(CancellationToken token)
        {
            // No tracking, transactions, source leases or remote I/O in this boundary.
            consumer = await ResolveConsumerAsync(input, token);
            binding = consumer?.ProviderBindings.SingleOrDefault(candidate => candidate.Id == expectedBinding.BindingId);
        }

        void ValidateProvider(DateTimeOffset now)
        {
            var currentOptions = options.CurrentValue;
            current = consumer is not null && consumer.Status == WebhookConsumerStatus.Active &&
                      WebhookPortalConsumerCustody.From(consumer) == input.ExpectedConsumer &&
                      binding is not null && WebhookPortalBindingCustody.From(binding) == expectedBinding &&
                      CaptureOptions(currentOptions) == expectedOptions &&
                      string.Equals(currentOptions.Svix.Environment.Trim(), expectedBinding.Environment, StringComparison.Ordinal) &&
                      SvixPortalAuthorityPolicy.AllowsBinding(binding, currentOptions, consumer.TenantId, consumer.Id) &&
                      (expiresAt is null || now < expiresAt);
        }

        if (input.OriginalRequester is { } requester)
        {
            var command = input.OriginalCommand
                ?? throw new UnauthorizedAccessException("The original portal command is required.");
            if (command.ConsumerId != input.ConsumerId || command.SessionId != input.SessionId
                || (command.ExpiresInSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : (TimeSpan?)null) != input.ExpiresIn
                || requester.Consumer != input.ExpectedConsumer)
                throw new UnauthorizedAccessException("The original portal request changed.");
            await requester.ValidateAsync(command, ReadProvider, ValidateProvider, cancellationToken);
        }
        else
        {
            if (input.OriginalCommand is not null)
                throw new UnauthorizedAccessException("The original portal requester is required.");
            await ReadProvider(cancellationToken);
            ValidateProvider(clock.GetUtcNow());
        }
        return current;
    }

    private static WebhookPortalOptionsCustody CaptureOptions(WebhookOptions value) =>
        new(value.Enabled, value.Provider, value.Svix.AppPortalEnabled, value.Svix.Environment,
            value.Svix.ProviderVersion, value.Svix.CapabilityPolicyVersion, value.Svix.BaseUrl,
            value.Svix.AuthTokenSecretRef);

    private static WebhookProviderPortalAccessResult CustodyLost() =>
        WebhookProviderPortalAccessResult.Failure("webhook_provider_binding_mismatched", isRetryable: false);

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
