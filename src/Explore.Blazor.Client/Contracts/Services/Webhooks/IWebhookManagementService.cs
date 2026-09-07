using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Contracts.Services.Webhooks;

public interface IWebhookManagementService
{
    Task<WebhookManagementSnapshot> GetSnapshotAsync(
        WebhookOwnerSelection owner,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HalResourceOfWebhookDeliveryAttemptDto>> GetDeliveryAttemptsAsync(
        WebhookOwnerSelection owner,
        Guid? messageId = null,
        Guid? endpointId = null,
        int limit = 100,
        CancellationToken cancellationToken = default);

    Task<WebhookActionResult> CreateConsumerAsync(
        CreateWebhookConsumerRequestDto request,
        CancellationToken cancellationToken = default);

    Task<WebhookActionResult> CreateEndpointAsync(
        CreateWebhookEndpointRequestDto request,
        CancellationToken cancellationToken = default);

    Task<WebhookActionResult> UpdateEndpointAsync(
        Guid endpointId,
        UpdateWebhookEndpointRequestDto request,
        CancellationToken cancellationToken = default);

    Task<WebhookActionResult> DeleteEndpointAsync(
        Guid endpointId,
        CancellationToken cancellationToken = default);

    Task<WebhookActionResult> RotateEndpointSecretAsync(
        Guid endpointId,
        RotateWebhookEndpointSecretRequestDto request,
        CancellationToken cancellationToken = default);

    Task<WebhookActionResult> TestEndpointAsync(
        Guid endpointId,
        CancellationToken cancellationToken = default);

    Task<WebhookActionResult> RetryDeliveryAttemptAsync(
        Guid attemptId,
        CancellationToken cancellationToken = default);

    Task<WebhookPortalResult> OpenProviderPortalAsync(
        Guid consumerId,
        CancellationToken cancellationToken = default);
}
