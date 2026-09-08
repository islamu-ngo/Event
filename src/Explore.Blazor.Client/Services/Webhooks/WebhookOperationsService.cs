using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Webhooks;

namespace Explore.Blazor.Client.Services.Webhooks;

public sealed class WebhookOperationsService(
    IWebhookBulkReplaysClient bulkReplaysClient,
    IWebhookEndpointOperationsClient endpointOperationsClient,
    IWebhookMessagesClient messagesClient,
    IWebhookProviderPublicationsClient publicationsClient,
    ILogger<WebhookOperationsService> logger) : IWebhookOperationsService
{
    private const int BadRequestStatusCode = 400;
    private const int GoneStatusCode = 410;
    private const int OperationsLimit = 100;

    public async Task<WebhookPayloadResult> GetMessagePayloadAsync(
        Guid messageId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = await messagesClient.GetWebhookMessagePayloadAsync(
                messageId,
                cancellationToken: cancellationToken);
            return new WebhookPayloadResult(true, "Webhook payload loaded.", payload);
        }
        catch (ApiException ex) when (ex.StatusCode == GoneStatusCode)
        {
            logger.LogInformation("Webhook payload {MessageId} is no longer retained.", messageId);
            return new WebhookPayloadResult(false, "The webhook payload is no longer retained.");
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Webhook payload read failed with status {StatusCode}.", ex.StatusCode);
            return new WebhookPayloadResult(false, "Webhook payload access is unavailable.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Webhook payload read failed.");
            return new WebhookPayloadResult(false, "Webhook payload access is unavailable.");
        }
    }

    public Task<WebhookActionResult> PauseEndpointAsync(
        Guid endpointId,
        PauseWebhookEndpointRequestDto request,
        CancellationToken cancellationToken = default) =>
        ExecuteCommandAsync(
            () => endpointOperationsClient.PauseWebhookEndpointAsync(endpointId, request, cancellationToken: cancellationToken),
            "Webhook endpoint paused.",
            "Unable to pause webhook endpoint.");

    public Task<WebhookActionResult> ResumeEndpointAsync(
        Guid endpointId,
        ResumeWebhookEndpointRequestDto request,
        CancellationToken cancellationToken = default) =>
        ExecuteCommandAsync(
            () => endpointOperationsClient.ResumeWebhookEndpointAsync(endpointId, request, cancellationToken: cancellationToken),
            "Webhook endpoint resumed.",
            "Unable to resume webhook endpoint.");

    public async Task<WebhookProviderPublicationSnapshot> GetProviderPublicationsAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var collection = await publicationsClient.GetWebhookProviderPublicationsAsync(
                limit: OperationsLimit,
                cancellationToken: cancellationToken);
            return new WebhookProviderPublicationSnapshot
            {
                Publications = ToList(collection._embedded?.Items)
            };
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Webhook provider publication load failed with status {StatusCode}.", ex.StatusCode);
            return WebhookProviderPublicationSnapshot.Failed("Unable to load provider publications.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Webhook provider publication load failed.");
            return WebhookProviderPublicationSnapshot.Failed("Unable to load provider publications.");
        }
    }

    public Task<WebhookActionResult> ReconcileProviderPublicationAsync(
        Guid publicationId,
        ReconcileWebhookProviderPublicationRequestDto request,
        CancellationToken cancellationToken = default) =>
        ExecuteCommandAsync(
            () => publicationsClient.ReconcileWebhookProviderPublicationAsync(
                publicationId,
                request,
                cancellationToken: cancellationToken),
            "Provider publication reconciled.",
            "Unable to reconcile provider publication.");

    public Task<WebhookActionResult> AbandonProviderPublicationAsync(
        Guid publicationId,
        AbandonWebhookProviderPublicationRequestDto request,
        CancellationToken cancellationToken = default) =>
        ExecuteCommandAsync(
            () => publicationsClient.AbandonWebhookProviderPublicationAsync(
                publicationId,
                request,
                cancellationToken: cancellationToken),
            "Provider publication abandoned.",
            "Unable to abandon provider publication.");

    public async Task<WebhookBulkReplaySnapshot> GetBulkReplaysAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var collection = await bulkReplaysClient.GetWebhookBulkReplaysAsync(
                limit: OperationsLimit,
                cancellationToken: cancellationToken);
            return new WebhookBulkReplaySnapshot
            {
                Operations = ToList(collection._embedded?.Items),
                CanPreview = WebhookHal.HasLink(collection._links, WebhookClientLinkRelations.BulkReplayPreview),
                CanSchedule = WebhookHal.HasLink(collection._links, WebhookClientLinkRelations.BulkReplays)
            };
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Webhook bulk replay load failed with status {StatusCode}.", ex.StatusCode);
            return WebhookBulkReplaySnapshot.Failed("Unable to load bulk replay operations.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Webhook bulk replay load failed.");
            return WebhookBulkReplaySnapshot.Failed("Unable to load bulk replay operations.");
        }
    }

    public async Task<WebhookBulkReplayPreviewResult> PreviewBulkReplayAsync(
        WebhookBulkReplayFilterDto filter,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var preview = await bulkReplaysClient.PreviewWebhookBulkReplayAsync(
                filter.FromUtc,
                filter.ToUtc,
                filter.WebhookConsumerId,
                filter.WebhookEndpointId,
                filter.EventType,
                filter.MaxItems,
                cancellationToken: cancellationToken);
            return new WebhookBulkReplayPreviewResult(true, "Bulk replay preview loaded.", preview);
        }
        catch (ApiException ex) when (ex.StatusCode == BadRequestStatusCode)
        {
            logger.LogInformation("Webhook bulk replay preview rejected invalid filters.");
            return new WebhookBulkReplayPreviewResult(false, "The bulk replay filters are invalid.");
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Webhook bulk replay preview failed with status {StatusCode}.", ex.StatusCode);
            return new WebhookBulkReplayPreviewResult(false, "Unable to preview webhook bulk replay.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Webhook bulk replay preview failed.");
            return new WebhookBulkReplayPreviewResult(false, "Unable to preview webhook bulk replay.");
        }
    }

    public Task<WebhookActionResult> ScheduleBulkReplayAsync(
        ScheduleWebhookBulkReplayRequestDto request,
        CancellationToken cancellationToken = default) =>
        ExecuteCommandAsync(
            () => bulkReplaysClient.ScheduleWebhookBulkReplayAsync(request, cancellationToken: cancellationToken),
            "Webhook bulk replay scheduled.",
            "Unable to schedule webhook bulk replay.");

    public Task<WebhookActionResult> CancelBulkReplayAsync(
        Guid operationId,
        CancelWebhookBulkReplayRequestDto request,
        CancellationToken cancellationToken = default) =>
        ExecuteCommandAsync(
            () => bulkReplaysClient.CancelWebhookBulkReplayAsync(
                operationId,
                request,
                cancellationToken: cancellationToken),
            "Webhook bulk replay cancelled.",
            "Unable to cancel webhook bulk replay.");

    private async Task<WebhookActionResult> ExecuteCommandAsync(
        Func<Task<BaseCommandResponseOfGuid>> action,
        string successMessage,
        string fallbackFailureMessage)
    {
        try
        {
            return FromCommandResponse(await action(), successMessage, fallbackFailureMessage);
        }
        catch (ApiException<BaseCommandResponseOfGuid> ex)
        {
            logger.LogWarning(ex, "Webhook operation failed with status {StatusCode}.", ex.StatusCode);
            return FromCommandResponse(ex.Result, successMessage, fallbackFailureMessage);
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Webhook operation failed with status {StatusCode}.", ex.StatusCode);
            return WebhookActionResult.Failed(fallbackFailureMessage);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Webhook operation failed.");
            return WebhookActionResult.Failed(fallbackFailureMessage);
        }
    }

    private static WebhookActionResult FromCommandResponse(
        BaseCommandResponseOfGuid? response,
        string successMessage,
        string fallbackFailureMessage)
    {
        if (response?.Success == true)
        {
            return WebhookActionResult.Succeeded(response.Message ?? successMessage, response.Id);
        }

        var errors = response?.Errors is { Count: > 0 }
            ? string.Join(" ", response.Errors)
            : null;
        return WebhookActionResult.Failed(errors ?? response?.Message ?? fallbackFailureMessage);
    }

    private static IReadOnlyList<T> ToList<T>(IEnumerable<T>? items) =>
        items?.ToList() ?? [];
}
