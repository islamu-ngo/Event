namespace Explore.Application.Contracts.Webhooks;

public interface IWebhookEventTypeRegistry
{
    IReadOnlyCollection<WebhookEventTypeDescriptor> GetAll();

    WebhookEventTypeDescriptor? FindByName(string name);

    bool IsKnownEventType(string name);
}

