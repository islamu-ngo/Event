namespace Explore.Application.Contracts.Webhooks;

public interface IWebhookEndpointManager
{
    Task<WebhookEndpointResult> CreateEndpointAsync(
        CreateWebhookEndpointInput input,
        CancellationToken cancellationToken);

    Task<WebhookEndpointResult> UpdateEndpointAsync(
        UpdateWebhookEndpointInput input,
        CancellationToken cancellationToken);

    Task DisableEndpointAsync(Guid endpointId, CancellationToken cancellationToken);
}
