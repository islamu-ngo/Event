using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Persistence.Database;

internal sealed class ConfigurationManifestTenantReadScopeFactory(
    IServiceProvider provider, IServiceScopeFactory scopes, ITenantExploreDbContextFactory tenantFactory)
    : IConfigurationManifestTenantReadScopeFactory
{
    public async ValueTask<IConfigurationManifestTenantReadScope> CreateAsync(
        Guid tenantId, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(tenantId, Guid.Empty);
        var scope = scopes.CreateAsyncScope();
        ExploreDbContext? context = null;
        bool captured = false;
        try
        {
            context = await tenantFactory.CreateDbContextAsync(tenantId, cancellationToken);
            context.CurrentUserService = provider.GetService<ICurrentUserService>();
            scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().BindExecutionTenant(tenantId);
            scope.ServiceProvider.GetRequiredService<ExploreDbContextExecutionLease>().Bind(context, tenantId);
            _ = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            captured = true;
            return new ReadScope(scope);
        }
        catch
        {
            if (!captured && context is not null)
                await context.DisposeAsync();
            await scope.DisposeAsync();
            throw;
        }
    }

    private sealed class ReadScope(AsyncServiceScope scope) : IConfigurationManifestTenantReadScope
    {
        public IServiceProvider Services => scope.ServiceProvider;
        public ValueTask DisposeAsync() => scope.DisposeAsync();
    }
}
