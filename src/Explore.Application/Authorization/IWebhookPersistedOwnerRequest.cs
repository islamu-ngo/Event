namespace Explore.Application.Authorization;

public interface IWebhookPersistedOwnerRequest
{
    WebhookOwnedResourceKind OwnedResourceKind { get; }
    Guid OwnedResourceId { get; }
}

public enum WebhookOwnedResourceKind
{
    Consumer = 1,
    Endpoint = 2,
    Message = 3,
    DeliveryAttempt = 4
}
