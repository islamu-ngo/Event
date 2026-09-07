namespace Explore.Application.Contracts.Webhooks;

public interface IWebhookEventSchemaProvider
{
    string CreateSchemaJson(WebhookEventTypeDescriptor descriptor);

    string CreateExamplePayloadJson(WebhookEventTypeDescriptor descriptor);
}

