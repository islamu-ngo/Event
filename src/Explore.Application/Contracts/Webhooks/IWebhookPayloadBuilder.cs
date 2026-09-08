namespace Explore.Application.Contracts.Webhooks;

public interface IWebhookPayloadBuilder
{
    Task<WebhookPayloadBuildResult> BuildAsync(
        WebhookEventBuildContext context,
        CancellationToken cancellationToken);
}

