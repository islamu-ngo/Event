using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Webhooks;

namespace Explore.Application.Contracts.Infrastructure;

/// <summary>Acquires fresh ordinary Runtime authority sealed to the persisted local-target tenant.</summary>
public interface IWebhookLocalDeliveryExecutionScopeFactory
{
    Task<IWebhookLocalDeliveryExecutionScope> CreateAsync(Guid tenantId, CancellationToken cancellationToken);
}

public interface IWebhookLocalDeliveryExecutionScope : IAsyncDisposable
{
    IWebhookLocalTargetRepository Targets { get; }
    IWebhookDeliveryAttemptRepository Attempts { get; }
    IWebhookEndpointRepository Endpoints { get; }
    IWebhookAuditEventWriter Audit { get; }
    IUnitOfWork UnitOfWork { get; }
}
