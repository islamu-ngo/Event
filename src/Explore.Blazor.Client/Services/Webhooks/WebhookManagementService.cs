using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Webhooks;
using Microsoft.Extensions.Logging;

namespace Explore.Blazor.Client.Services.Webhooks;

public sealed class WebhookManagementService(
    IWebhooksClient webhooksClient,
    IWebhookEndpointsClient endpointsClient,
    IWebhookMessagesClient messagesClient,
    ILogger<WebhookManagementService> logger) : IWebhookManagementService
{
    private const int SnapshotLimit = 200;
    private const int ActivityLimit = 100;

    public async Task<WebhookManagementSnapshot> GetSnapshotAsync(
        WebhookOwnerSelection owner,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner);

        try
        {
            var eventTypesTask = webhooksClient.GetWebhookEventTypesAsync(cancellationToken: cancellationToken);
            var consumersTask = webhooksClient.GetWebhookConsumersAsync(
                ownerKindId: owner.OwnerKindId,
                ownerId: owner.OwnerId,
                limit: SnapshotLimit,
                cancellationToken: cancellationToken);
            var endpointsTask = endpointsClient.GetWebhookEndpointsAsync(
                ownerKindId: owner.OwnerKindId,
                ownerId: owner.OwnerId,
                limit: SnapshotLimit,
                cancellationToken: cancellationToken);
            var messagesTask = messagesClient.GetWebhookMessagesAsync(
                ownerKindId: owner.OwnerKindId,
                ownerId: owner.OwnerId,
                limit: ActivityLimit,
                cancellationToken: cancellationToken);
            var attemptsTask = messagesClient.GetWebhookDeliveryAttemptsAsync(
                ownerKindId: owner.OwnerKindId,
                ownerId: owner.OwnerId,
                limit: ActivityLimit,
                cancellationToken: cancellationToken);

            await Task.WhenAll(eventTypesTask, consumersTask, endpointsTask, messagesTask, attemptsTask);

            var consumerCollection = await consumersTask;
            var endpointCollection = await endpointsTask;
            var messageCollection = await messagesTask;
            var attemptCollection = await attemptsTask;

            return new WebhookManagementSnapshot
            {
                EventTypes = (await eventTypesTask)
                    .OrderBy(eventType => eventType.GroupName, StringComparer.Ordinal)
                    .ThenBy(eventType => eventType.Name, StringComparer.Ordinal)
                    .ToList(),
                Consumers = ToList(consumerCollection._embedded?.Items),
                Endpoints = ToList(endpointCollection._embedded?.Items),
                Messages = ToList(messageCollection._embedded?.Items),
                DeliveryAttempts = ToList(attemptCollection._embedded?.Items),
                CanCreateConsumer = WebhookHal.HasLink(consumerCollection._links, WebhookClientLinkRelations.Create),
                CanCreateEndpoint = WebhookHal.HasLink(endpointCollection._links, WebhookClientLinkRelations.Create),
                CanViewProviderPublications = WebhookHal.HasLink(
                    messageCollection._links,
                    WebhookClientLinkRelations.ProviderPublications),
                CanUseBulkReplay = WebhookHal.HasLink(
                    messageCollection._links,
                    WebhookClientLinkRelations.BulkReplayPreview)
                    && WebhookHal.HasLink(messageCollection._links, WebhookClientLinkRelations.BulkReplays)
            };
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Webhook management snapshot failed with status {StatusCode}.", ex.StatusCode);
            return WebhookManagementSnapshot.Failed("Unable to load webhook management data.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Webhook management snapshot failed.");
            return WebhookManagementSnapshot.Failed("Unable to load webhook management data.");
        }
    }

    public async Task<IReadOnlyList<HalResourceOfWebhookDeliveryAttemptDto>> GetDeliveryAttemptsAsync(
        WebhookOwnerSelection owner,
        Guid? messageId = null,
        Guid? endpointId = null,
        int limit = ActivityLimit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner);

        try
        {
            var attempts = await messagesClient.GetWebhookDeliveryAttemptsAsync(
                ownerKindId: owner.OwnerKindId,
                ownerId: owner.OwnerId,
                messageId: messageId,
                endpointId: endpointId,
                limit: limit,
                cancellationToken: cancellationToken);
            return ToList(attempts._embedded?.Items);
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Webhook delivery attempt load failed with status {StatusCode}.", ex.StatusCode);
            return [];
        }
    }

    public async Task<WebhookActionResult> CreateConsumerAsync(
        CreateWebhookConsumerRequestDto request,
        CancellationToken cancellationToken = default) =>
        await ExecuteCommandAsync(
            () => webhooksClient.CreateWebhookConsumerAsync(request, cancellationToken: cancellationToken),
            "Webhook consumer created.",
            "Unable to create webhook consumer.");

    public async Task<WebhookActionResult> CreateEndpointAsync(
        CreateWebhookEndpointRequestDto request,
        CancellationToken cancellationToken = default) =>
        await ExecuteCommandAsync(
            () => endpointsClient.CreateWebhookEndpointAsync(request, cancellationToken: cancellationToken),
            "Webhook endpoint created.",
            "Unable to create webhook endpoint.");

    public async Task<WebhookActionResult> UpdateEndpointAsync(
        Guid endpointId,
        UpdateWebhookEndpointRequestDto request,
        CancellationToken cancellationToken = default) =>
        await ExecuteCommandAsync(
            () => endpointsClient.UpdateWebhookEndpointAsync(endpointId, request, cancellationToken: cancellationToken),
            "Webhook endpoint updated.",
            "Unable to update webhook endpoint.");

    public async Task<WebhookActionResult> DeleteEndpointAsync(
        Guid endpointId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await endpointsClient.DeleteWebhookEndpointAsync(endpointId, cancellationToken: cancellationToken);
            return WebhookActionResult.Succeeded("Webhook endpoint archived.", endpointId);
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Webhook endpoint archive failed with status {StatusCode}.", ex.StatusCode);
            return WebhookActionResult.Failed("Unable to archive webhook endpoint.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Webhook endpoint archive failed.");
            return WebhookActionResult.Failed("Unable to archive webhook endpoint.");
        }
    }

    public async Task<WebhookActionResult> RotateEndpointSecretAsync(
        Guid endpointId,
        RotateWebhookEndpointSecretRequestDto request,
        CancellationToken cancellationToken = default) =>
        await ExecuteCommandAsync(
            () => endpointsClient.RotateWebhookEndpointSecretAsync(endpointId, request, cancellationToken: cancellationToken),
            "Webhook endpoint secret rotated.",
            "Unable to rotate webhook endpoint secret.");

    public async Task<WebhookActionResult> TestEndpointAsync(
        Guid endpointId,
        CancellationToken cancellationToken = default) =>
        await ExecuteCommandAsync(
            () => endpointsClient.TestWebhookEndpointAsync(endpointId, cancellationToken: cancellationToken),
            "Webhook endpoint test scheduled.",
            "Unable to schedule webhook endpoint test.");

    public async Task<WebhookActionResult> RetryDeliveryAttemptAsync(
        Guid attemptId,
        CancellationToken cancellationToken = default) =>
        await ExecuteCommandAsync(
            () => messagesClient.RetryWebhookDeliveryAttemptAsync(attemptId, cancellationToken: cancellationToken),
            "Webhook delivery retry scheduled.",
            "Unable to retry webhook delivery.");

    public async Task<WebhookPortalResult> OpenProviderPortalAsync(
        Guid consumerId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var access = await webhooksClient.OpenSvixAppPortalAsync(
                new OpenSvixAppPortalRequestDto
                {
                    ConsumerId = consumerId,
                    ExpiresInSeconds = 3600
                },
                cancellationToken: cancellationToken);

            if (string.IsNullOrWhiteSpace(access.Url))
            {
                return new WebhookPortalResult(false, "Provider portal did not return a URL.");
            }

            return new WebhookPortalResult(true, "Provider portal opened.", access.Url);
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Webhook provider portal request failed with status {StatusCode}.", ex.StatusCode);
            return new WebhookPortalResult(false, "Unable to open provider portal.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Webhook provider portal request failed.");
            return new WebhookPortalResult(false, "Unable to open provider portal.");
        }
    }

    private static IReadOnlyList<T> ToList<T>(IEnumerable<T>? items) =>
        items?.ToList() ?? [];

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
            logger.LogWarning(ex, "Webhook command failed with status {StatusCode}.", ex.StatusCode);
            return FromCommandResponse(ex.Result, successMessage, fallbackFailureMessage);
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Webhook command failed with status {StatusCode}.", ex.StatusCode);
            return WebhookActionResult.Failed(fallbackFailureMessage);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Webhook command failed.");
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
}
