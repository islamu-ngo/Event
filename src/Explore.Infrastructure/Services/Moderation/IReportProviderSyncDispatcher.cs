using Explore.Domain;

namespace Explore.Infrastructure.Services.Moderation;

public interface IReportProviderSyncDispatcher
{
    Task DispatchAsync(OutboxMessage message, CancellationToken cancellationToken = default);
}
