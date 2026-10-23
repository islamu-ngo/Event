using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;

namespace Explore.Application.Contracts.Infrastructure;

/// <summary>Acquires independent ordinary authority for one exact tenant retention batch.</summary>
public interface IEventResourceAuditRetentionExecutionScopeFactory
{
    Task<IEventResourceAuditRetentionExecutionScope> CreateAsync(
        Guid tenantId, CancellationToken cancellationToken);
}

public interface IEventResourceAuditRetentionExecutionScope : IAsyncDisposable
{
    IEventResourceAuditRetentionRepository Repository { get; }
    IEventResourceGovernancePolicyReader Governance { get; }
    IUnitOfWork UnitOfWork { get; }
}
