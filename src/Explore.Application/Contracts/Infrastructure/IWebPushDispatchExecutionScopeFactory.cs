using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;

namespace Explore.Application.Contracts.Infrastructure;

/// <summary>Acquires fresh ordinary persistence authority sealed to the persisted dispatch tenant.</summary>
public interface IWebPushDispatchExecutionScopeFactory
{
    Task<IWebPushDispatchExecutionScope> CreateAsync(Guid tenantId, CancellationToken cancellationToken);
}

public interface IWebPushDispatchExecutionScope : IAsyncDisposable
{
    IWebPushDispatchOutboxRepository DispatchRepository { get; }
    IWebPushSubscriptionRepository SubscriptionRepository { get; }
    IPrivacyErasureStateRepository PrivacyErasureStateRepository { get; }
    INotificationPreferenceResolver PreferenceResolver { get; }
}
