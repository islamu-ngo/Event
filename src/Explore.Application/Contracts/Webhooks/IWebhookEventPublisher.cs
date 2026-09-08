namespace Explore.Application.Contracts.Webhooks;

public interface IWebhookEventPublisher
{
    Task<WebhookEventPublishResult> PublishAsync(
        WebhookEventBuildContext context,
        CancellationToken cancellationToken);
}
