using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IWebhookEventTypeRepository
{
    Task<WebhookEventType> CreateAsync(WebhookEventType eventType, CancellationToken cancellationToken);

    Task<WebhookEventType?> GetByNameAsync(string name, CancellationToken cancellationToken);

    Task<IReadOnlyList<WebhookEventType>> GetByIdsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<WebhookEventType>> GetByNamesAsync(
        IReadOnlyCollection<string> names,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<WebhookEventType>> GetEnabledAsync(CancellationToken cancellationToken);

    Task UpdateAsync(WebhookEventType eventType, CancellationToken cancellationToken);
}
