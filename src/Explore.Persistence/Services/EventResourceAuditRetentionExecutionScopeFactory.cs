using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Services;
using Explore.Persistence.Database;

using Explore.Persistence.Repositories;

namespace Explore.Persistence.Services;

public sealed class EventResourceAuditRetentionExecutionScopeFactory(ITenantExploreDbContextFactory tenantFactory)
    : IEventResourceAuditRetentionExecutionScopeFactory
{
    public async Task<IEventResourceAuditRetentionExecutionScope> CreateAsync(
        Guid tenantId, CancellationToken cancellationToken)
    {
        var context = await tenantFactory.CreateDbContextAsync(tenantId, cancellationToken);
        return new ExecutionScope(context);
    }

    private sealed class ExecutionScope : IEventResourceAuditRetentionExecutionScope
    {
        private readonly ExploreDbContext _context;

        public ExecutionScope(ExploreDbContext context)
        {
            _context = context;
            UnitOfWork = new EfCoreUnitOfWork(context);
            Repository = new EventResourceAuditRetentionRepository(context);
            var mutationLock = new RelationalSettingMutationLock(context, UnitOfWork);
            Governance = new EventResourceGovernancePolicyReader(
                new SystemSettingRepository(context, mutationLock),
                new TenantSettingRepository(context, mutationLock));
        }

        public IEventResourceAuditRetentionRepository Repository { get; }
        public IEventResourceGovernancePolicyReader Governance { get; }
        public IUnitOfWork UnitOfWork { get; }

        public ValueTask DisposeAsync() => _context.DisposeAsync();
    }
}
