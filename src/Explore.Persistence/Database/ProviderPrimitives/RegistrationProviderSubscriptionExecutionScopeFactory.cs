using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Services.Registration;
using Explore.Domain;
using Explore.Persistence.Database;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Persistence.Database.ProviderPrimitives;

public sealed class RegistrationProviderSubscriptionExecutionScopeFactory(
    IServiceScopeFactory scopes,
    ITenantExploreDbContextFactory tenantFactory) : IRegistrationProviderSubscriptionExecutionScopeFactory
{
    public async ValueTask<IRegistrationProviderSubscriptionExecutionScope> CreateAsync(
        Guid tenantId, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("Subscription execution requires an exact tenant.", nameof(tenantId));
        var scope = scopes.CreateAsyncScope();
        ExploreDbContext? context = null;
        bool captured = false;
        try
        {
            scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().BindExecutionTenant(tenantId);
            context = await tenantFactory.CreateDbContextAsync(tenantId, cancellationToken);
            if (context.IsTenantFilterBypassed || context.BindTenantLease() != tenantId)
                throw new InvalidOperationException("subscription_runtime_tenant_not_selected");
            scope.ServiceProvider.GetRequiredService<ExploreDbContextExecutionLease>().Bind(context, tenantId);
            if (!ReferenceEquals(scope.ServiceProvider.GetRequiredService<ExploreDbContext>(), context))
                throw new InvalidOperationException("subscription_runtime_context_not_selected");
            captured = true;
            // Resolving the lifecycle now binds every repository, UoW, receipt and adapter
            // dependency to the selected Runtime lease; nothing is retargeted in place.
            var lifecycle = scope.ServiceProvider.GetRequiredService<RegistrationProviderSubscriptionLifecycleService>();
            return new ExecutionScope(scope, lifecycle);
        }
        catch
        {
            if (!captured && context is not null)
                await context.DisposeAsync();
            await scope.DisposeAsync();
            throw;
        }
    }

    private sealed class ExecutionScope(AsyncServiceScope scope, RegistrationProviderSubscriptionLifecycleService lifecycle)
        : IRegistrationProviderSubscriptionExecutionScope
    {
        public Task<bool> ProcessAsync(RegistrationProviderSubscriptionClaim claim,
            RegistrationProviderSubscriptionOperation operation, CancellationToken cancellationToken) =>
            lifecycle.ProcessAsync(claim, operation, cancellationToken);

        public ValueTask DisposeAsync() => scope.DisposeAsync();
    }
}
