using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.Services.Webhooks;
using Explore.Persistence.Database;
using Microsoft.Extensions.DependencyInjection;

using Explore.Persistence.Repositories;

namespace Explore.Persistence.Database.ProviderPrimitives;

public sealed class WebhookLocalDeliveryExecutionScopeFactory(
    ITenantExploreDbContextFactory tenantFactory,
    IServiceScopeFactory scopeFactory) : IWebhookLocalDeliveryExecutionScopeFactory
{
    public async Task<IWebhookLocalDeliveryExecutionScope> CreateAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var context = await tenantFactory.CreateDbContextAsync(tenantId, cancellationToken);
        var services = scopeFactory.CreateAsyncScope();
        try
        {
            return new ExecutionScope(context, services,
                services.ServiceProvider.GetRequiredService<ICurrentUserService>(),
                services.ServiceProvider.GetRequiredService<IMachinePrincipalAccessor>(),
                services.ServiceProvider.GetRequiredService<IWebhookRetentionPolicyResolver>(),
                services.ServiceProvider.GetRequiredService<TimeProvider>());
        }
        catch
        {
            await context.DisposeAsync();
            await services.DisposeAsync();
            throw;
        }
    }

    private sealed class ExecutionScope(
        ExploreDbContext context, AsyncServiceScope services, ICurrentUserService currentUserService,
        IMachinePrincipalAccessor machinePrincipalAccessor, IWebhookRetentionPolicyResolver retentionPolicyResolver,
        TimeProvider timeProvider) : IWebhookLocalDeliveryExecutionScope
    {
        public IWebhookLocalTargetRepository Targets { get; } = new WebhookLocalTargetRepository(context);
        public IWebhookDeliveryAttemptRepository Attempts { get; } = new WebhookDeliveryAttemptRepository(context);
        public IWebhookEndpointRepository Endpoints { get; } = new WebhookEndpointRepository(context);
        public IWebhookAuditEventWriter Audit { get; } = new WebhookAuditEventWriter(
            new WebhookAuditEventRepository(context), currentUserService, machinePrincipalAccessor,
            retentionPolicyResolver, timeProvider);
        public IUnitOfWork UnitOfWork { get; } = new EfCoreUnitOfWork(context);
        public async ValueTask DisposeAsync()
        {
            try { await context.DisposeAsync(); }
            finally { await services.DisposeAsync(); }
        }
    }
}
