using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Persistence.Database;

public sealed class InventoryHoldExpiryExecutionScopeFactory(
    IServiceScopeFactory scopes, ITenantExploreDbContextFactory tenantFactory)
    : IInventoryHoldExpiryExecutionScopeFactory
{
    public static ExploreDbContext? GetSelectedRuntimeContext(IServiceProvider services) =>
        services.GetService<InventoryHoldExpiryExecutionLease>()?.Context;

    public async ValueTask<IInventoryHoldExpiryExecutionScope> CreateAsync(
        Guid tenantId, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(tenantId, Guid.Empty);
        var scope = scopes.CreateAsyncScope();
        ExploreDbContext? context = null;
        bool captured = false;
        try
        {
            scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().BindExecutionTenant(tenantId);
            context = await tenantFactory.CreateDbContextAsync(tenantId, cancellationToken);
            scope.ServiceProvider.GetRequiredService<InventoryHoldExpiryExecutionLease>().Bind(context, tenantId);
            if (!ReferenceEquals(scope.ServiceProvider.GetRequiredService<ExploreDbContext>(), context))
                throw new InvalidOperationException("hold_expiry_runtime_context_not_selected");
            captured = true;
            return new ExecutionScope(scope);
        }
        catch
        {
            if (!captured && context is not null)
                await context.DisposeAsync();
            await scope.DisposeAsync();
            throw;
        }
    }

    private sealed class ExecutionScope(AsyncServiceScope scope) : IInventoryHoldExpiryExecutionScope
    {
        public IServiceProvider Services => scope.ServiceProvider;
        public ValueTask DisposeAsync() => scope.DisposeAsync();
    }
}

internal sealed class InventoryHoldExpiryExecutionLease
{
    internal ExploreDbContext? Context { get; private set; }

    internal void Bind(ExploreDbContext context, Guid tenantId)
    {
        context.ValidateTenantLease();
        if (Context is not null || context.TenantFilterTenantId != tenantId || context.IsTenantFilterBypassed)
            throw new InvalidOperationException("Hold expiry requires one original selected Runtime lease.");
        Context = context;
    }
}
