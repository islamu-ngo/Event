namespace Explore.Application.Authorization;

public interface IWebhookOwnerScopedRequest
{
    int OwnerKindId { get; }
    Guid? OwnerId { get; }
}
