using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Webhooks;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Webhooks;

public sealed class GovernedWebhookDeliveryPlanResolver(
    IWebhookConsumerRepository consumerRepository,
    IWebhookEndpointRepository endpointRepository,
    IWebhookEventTypeRepository eventTypeRepository,
    ISecretBindingRepository secretBindingRepository,
    IWebhookProviderCapabilityResolver capabilityResolver,
    IWebhookRetentionPolicyResolver retentionPolicyResolver,
    IOptionsMonitor<WebhookOptions> options,
    TimeProvider timeProvider) : IWebhookDeliveryPlanResolver
{
    public async Task<WebhookDeliveryPlanResolution> ResolveAsync(
        WebhookEventBuildContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (context.TenantId == Guid.Empty || context.ConsumerId is not { } consumerId || consumerId == Guid.Empty)
        {
            return Unavailable("webhook_delivery_authority_invalid");
        }

        var consumer = await consumerRepository.GetByIdForOwnerOperationAsync(
            consumerId,
            forUpdate: false,
            cancellationToken);
        if (consumer is null ||
            consumer.Status != WebhookConsumerStatus.Active ||
            consumer.ConfigurationVersion < 1)
        {
            return Unavailable("webhook_consumer_unavailable");
        }

        WebhookOwnershipScope ownership;
        try
        {
            ownership = consumer.Ownership;
        }
        catch (InvalidOperationException)
        {
            return Unavailable("webhook_consumer_ownership_invalid");
        }

        if (ownership.Kind != WebhookConsumerKind.Instance &&
            ownership.TenantId != context.TenantId)
        {
            return Unavailable("webhook_consumer_source_tenant_mismatch");
        }

        var capabilityResolution = capabilityResolver.Resolve(consumer.ProviderMode);
        if (!capabilityResolution.IsProviderModeAvailable)
        {
            return Unavailable(
                capabilityResolution.UnavailableReasonCode ?? "webhook_provider_mode_unavailable");
        }

        if (consumer.ProviderMode == WebhookProviderMode.Disabled)
        {
            return Unavailable("webhook_consumer_disabled");
        }

        var eventType = await eventTypeRepository.GetByNameAsync(context.EventType, cancellationToken);
        if (eventType is null ||
            !eventType.IsEnabled ||
            eventType.SchemaVersion < 1 ||
            eventType.PayloadRetentionDays < 1)
        {
            return Unavailable("webhook_event_contract_unavailable");
        }

        var localTargets = await ResolveLocalTargetsAsync(
            consumer,
            context.EventType,
            cancellationToken);
        if (localTargets is null)
        {
            return Unavailable("webhook_local_targets_unavailable");
        }

        var providerTargets = await ResolveProviderTargetsAsync(
            consumer,
            capabilityResolution,
            cancellationToken);
        if (providerTargets is null)
        {
            return Unavailable("webhook_provider_binding_unavailable");
        }

        var now = timeProvider.GetUtcNow();
        var retention = retentionPolicyResolver.Resolve(
            context.OccurredAt,
            now,
            eventType.PayloadRetentionDays);
        return WebhookDeliveryPlanResolution.Success(
            consumer.Id,
            consumer.ProviderMode,
            $"consumer-v{consumer.ConfigurationVersion}:{capabilityResolution.ResolutionVersion}",
            eventType.SchemaVersion,
            $"retain-{eventType.PayloadRetentionDays}-days",
            retention.PolicyVersion,
            retention.OutboundPayloadRetentionUntil,
            retention.ProcessingAttemptRetentionUntil,
            retention.DeadLetterEvidenceRetentionUntil,
            retention.ProviderPublicationRetentionUntil.UtcDateTime,
            retention.OperationalLogRetentionUntil,
            localTargets,
            providerTargets);
    }

    private async Task<IReadOnlyCollection<WebhookLocalTargetResolution>?> ResolveLocalTargetsAsync(
        WebhookConsumer consumer,
        string eventType,
        CancellationToken cancellationToken)
    {
        if (consumer.ProviderMode is not (WebhookProviderMode.Local or WebhookProviderMode.Composite))
        {
            return [];
        }

        var endpoints = await endpointRepository.GetActiveSubscribedEndpointsByConsumerAsync(
            consumer.TenantId,
            consumer.Id,
            eventType,
            cancellationToken);
        if (consumer.ProviderMode == WebhookProviderMode.Local && endpoints.Count == 0)
        {
            return null;
        }

        if (endpoints.Any(endpoint =>
            endpoint.ConfigurationVersion < 1 ||
            endpoint.SecretVersion < 1 ||
            endpoint.SecretActivatedAt == default ||
            endpoint.SecretActivatedAt.Kind != DateTimeKind.Utc))
        {
            return null;
        }

        return endpoints
            .Select(endpoint => new WebhookLocalTargetResolution(
                endpoint,
                endpoint.ConfigurationVersion,
                new DateTimeOffset(endpoint.SecretActivatedAt),
                null))
            .ToArray();
    }

    private async Task<IReadOnlyCollection<WebhookProviderTargetResolution>?> ResolveProviderTargetsAsync(
        WebhookConsumer consumer,
        WebhookProviderModeCapabilityResolution capabilityResolution,
        CancellationToken cancellationToken)
    {
        if (consumer.ProviderMode is not (WebhookProviderMode.Svix or WebhookProviderMode.Composite))
        {
            return [];
        }

        var binding = consumer.GetVerifiedProviderBinding(WebhookProviderKind.Svix);
        if (binding is null ||
            !string.Equals(
                binding.ProviderEnvironment,
                capabilityResolution.ProviderEnvironment,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                binding.ProviderVersion,
                capabilityResolution.ProviderVersion,
                StringComparison.Ordinal) ||
            !string.Equals(
                binding.CapabilityResolutionVersion,
                capabilityResolution.ResolutionVersion,
                StringComparison.Ordinal))
        {
            return null;
        }

        var credentialReference = options.CurrentValue.Svix.AuthTokenSecretRef?.Trim();
        if (!string.Equals(
                credentialReference,
                SecretDefinitionRegistry.Keys.Webhooks.SvixAuthToken,
                StringComparison.Ordinal))
        {
            return null;
        }

        var credentialBinding = await secretBindingRepository.GetByKeyAndScopeAsync(
            SecretDefinitionRegistry.Keys.Webhooks.SvixAuthToken,
            SecretScope.Instance,
            null,
            cancellationToken);
        if (credentialBinding is null)
        {
            return null;
        }

        var credentialChangedAt = credentialBinding.UpdatedAt ?? credentialBinding.CreatedAt;
        var credentialVersion = $"{credentialBinding.Id:N}:{credentialChangedAt.Ticks}";
        return
        [
            new WebhookProviderTargetResolution(
                binding,
                SecretDefinitionRegistry.Keys.Webhooks.SvixAuthToken,
                credentialVersion,
                timeProvider.GetUtcNow().UtcDateTime.Add(WebhookProviderPublication.MaximumIdempotencyValidity))
        ];
    }

    private static WebhookDeliveryPlanResolution Unavailable(string failureCategory) =>
        WebhookDeliveryPlanResolution.Unavailable(
            failureCategory,
            "Authoritative webhook delivery-plan facts are unavailable.");
}
