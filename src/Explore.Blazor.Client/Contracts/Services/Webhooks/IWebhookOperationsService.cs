using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Contracts.Services.Webhooks;

public interface IWebhookOperationsService
{
    Task<WebhookPayloadResult> GetMessagePayloadAsync(
        Guid messageId,
        CancellationToken cancellationToken = default);

    Task<WebhookActionResult> PauseEndpointAsync(
        Guid endpointId,
        PauseWebhookEndpointRequestDto request,
        CancellationToken cancellationToken = default);

    Task<WebhookActionResult> ResumeEndpointAsync(
        Guid endpointId,
        ResumeWebhookEndpointRequestDto request,
        CancellationToken cancellationToken = default);

    Task<WebhookProviderPublicationSnapshot> GetProviderPublicationsAsync(
        CancellationToken cancellationToken = default);

    Task<WebhookActionResult> ReconcileProviderPublicationAsync(
        Guid publicationId,
        ReconcileWebhookProviderPublicationRequestDto request,
        CancellationToken cancellationToken = default);

    Task<WebhookActionResult> AbandonProviderPublicationAsync(
        Guid publicationId,
        AbandonWebhookProviderPublicationRequestDto request,
        CancellationToken cancellationToken = default);

    Task<WebhookBulkReplaySnapshot> GetBulkReplaysAsync(
        CancellationToken cancellationToken = default);

    Task<WebhookBulkReplayPreviewResult> PreviewBulkReplayAsync(
        WebhookBulkReplayFilterDto filter,
        CancellationToken cancellationToken = default);

    Task<WebhookActionResult> ScheduleBulkReplayAsync(
        ScheduleWebhookBulkReplayRequestDto request,
        CancellationToken cancellationToken = default);

    Task<WebhookActionResult> CancelBulkReplayAsync(
        Guid operationId,
        CancelWebhookBulkReplayRequestDto request,
        CancellationToken cancellationToken = default);
}
