using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Persistence.Database.ProviderPrimitives;

public sealed class GenericOutboxExecutionScopeFactory(
    IServiceScopeFactory scopes,
    ITenantExploreDbContextFactory tenantFactory,
    IDbContextFactory<ExploreDbContext> runtimeFactory) : IGenericOutboxExecutionScopeFactory
{
    /// <summary>Hosting compositions must reuse the selected Runtime lease rather than resolve ambient tenancy.</summary>
    public static ExploreDbContext? GetSelectedRuntimeContext(IServiceProvider services) =>
        services.GetRequiredService<ExploreDbContextExecutionLease>().Context;

    public async ValueTask<IGenericOutboxExecutionScope> CreateAsync(
        GenericOutboxClaim claim, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(claim);
        var scope = scopes.CreateAsyncScope();
        ExploreDbContext? context = null;
        bool captured = false;
        try
        {
            if (claim.TenantId is { } tenant)
            {
                scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().BindExecutionTenant(tenant);
                context = await tenantFactory.CreateDbContextAsync(tenant, cancellationToken);
            }
            else
            {
                // Ordinary Runtime pool, explicitly sealed without a tenant. Never use Global for effects.
                context = await runtimeFactory.CreateDbContextAsync(cancellationToken);
                context.ClearTenantFilterBypass();
                context.TenantContext = null;
                context.CurrentUserService = null;
                context.BindTenantLease();
            }
            scope.ServiceProvider.GetRequiredService<ExploreDbContextExecutionLease>().Bind(context, claim.TenantId);
            if (!ReferenceEquals(scope.ServiceProvider.GetRequiredService<ExploreDbContext>(), context))
                throw new InvalidOperationException("generic_outbox_runtime_context_not_selected");
            captured = true;
            await scope.ServiceProvider.GetRequiredService<IGenericOutboxExecutionAuthority>()
                .BindAsync(claim, cancellationToken);
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

    private sealed class ExecutionScope(AsyncServiceScope scope) : IGenericOutboxExecutionScope
    {
        public IServiceProvider Services => scope.ServiceProvider;
        public ValueTask DisposeAsync() => scope.DisposeAsync();
    }
}
