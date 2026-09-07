using Explore.Application.DTOs.Webhooks;
using Explore.Application.Lookups;
using Explore.Domain;

namespace Explore.Application.Features.Webhooks.Handlers;

internal static class WebhookEndpointDtoMapper
{
    public static WebhookEndpointDto Map(WebhookEndpoint endpoint)
    {
        var providerMode = NormalizedLookupMetadata.WebhookProviderMode(
            endpoint.Consumer?.ProviderModeId ?? (int)WebhookProviderMode.Local);
        var status = NormalizedLookupMetadata.WebhookEndpointStatus(endpoint.StatusId);
        var ownership = endpoint.Consumer?.Ownership
            ?? throw new InvalidOperationException("Webhook endpoint mapping requires its consumer ownership.");
        var ownerKind = NormalizedLookupMetadata.WebhookConsumerKind((int)ownership.Kind);
        return new()
        {
            Id = endpoint.Id,
            TenantId = endpoint.TenantId,
            InstanceId = endpoint.InstanceId,
            OwnerId = ownership.OwnerId,
            OwnerKindId = ownerKind.Id,
            OwnerKindCode = ownerKind.Code,
            OwnerKindName = ownerKind.Name,
            ConsumerId = endpoint.ConsumerId,
            ConsumerName = endpoint.Consumer?.Name,
            ProviderModeId = providerMode.Id,
            ProviderModeCode = providerMode.Code,
            ProviderModeName = providerMode.Name,
            DestinationHost = GetDestinationHost(endpoint.Url),
            Description = endpoint.Description,
            StatusId = status.Id,
            StatusCode = status.Code,
            StatusName = status.Name,
            SecretVersion = endpoint.SecretVersion,
            ConfigurationVersion = endpoint.ConfigurationVersion,
            ProviderEndpointId = endpoint.ProviderEndpointId,
            MaxAttempts = endpoint.MaxAttempts,
            TimeoutSeconds = endpoint.TimeoutSeconds,
            RateLimitPerMinute = endpoint.RateLimitPerMinute,
            LastSuccessAt = endpoint.LastSuccessAt,
            LastFailureAt = endpoint.LastFailureAt,
            ConsecutiveFailureCount = endpoint.ConsecutiveFailureCount,
            CircuitOpenedAt = endpoint.CircuitOpenedAt,
            AutoPausedAt = endpoint.AutoPausedAt,
            AutoPauseReason = endpoint.AutoPauseReason,
            LastResumedAt = endpoint.LastResumedAt,
            DeliveryStateVersion = endpoint.DeliveryStateVersion,
            CreatedAt = endpoint.CreatedAt,
            UpdatedAt = endpoint.UpdatedAt,
            Subscriptions = endpoint.Subscriptions
                .OrderBy(subscription => subscription.EventType?.GroupName)
                .ThenBy(subscription => subscription.EventType?.Name)
                .Select(MapSubscription)
                .ToArray()
        };
    }

    private static string GetDestinationHost(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.IdnHost : "unknown";

    private static WebhookEndpointSubscriptionDto MapSubscription(WebhookEndpointSubscription subscription) =>
        new()
        {
            Id = subscription.Id,
            EventTypeId = subscription.EventTypeId,
            EventTypeName = subscription.EventType?.Name ?? string.Empty,
            EventTypeGroupName = subscription.EventType?.GroupName ?? string.Empty,
            IsEnabled = subscription.IsEnabled,
            CreatedAt = subscription.CreatedAt
        };
}
