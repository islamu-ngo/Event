using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Persistence.Database;
using Explore.Persistence.Services;

using Explore.Persistence.Repositories;

namespace Explore.Persistence.Database.ProviderPrimitives;

public sealed class WebPushDispatchExecutionScopeFactory(ITenantExploreDbContextFactory tenantFactory)
    : IWebPushDispatchExecutionScopeFactory
{
    public async Task<IWebPushDispatchExecutionScope> CreateAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var context = await tenantFactory.CreateDbContextAsync(tenantId, cancellationToken);
        return new ExecutionScope(context);
    }

    private sealed class ExecutionScope(ExploreDbContext context) : IWebPushDispatchExecutionScope
    {
        public IWebPushDispatchOutboxRepository DispatchRepository { get; } = new WebPushDispatchOutboxRepository(context);
        public IWebPushSubscriptionRepository SubscriptionRepository { get; } = new WebPushSubscriptionRepository(context);
        public IPrivacyErasureStateRepository PrivacyErasureStateRepository { get; } = new PrivacyErasureStateRepository(context);
        public INotificationPreferenceResolver PreferenceResolver { get; } = new NotificationPreferenceResolver(context);

        public ValueTask DisposeAsync() => context.DisposeAsync();
    }
}
