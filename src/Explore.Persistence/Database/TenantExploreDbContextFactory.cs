using Explore.Application.Contracts.Infrastructure;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Explore.Persistence.Database;

/// <summary>Owns an independent ordinary pool without resolving request or Global contexts.</summary>
public sealed class TenantExploreDbContextFactory : ITenantExploreDbContextFactory
{
    private readonly PooledDbContextFactory<ExploreDbContext> _pool;

    public TenantExploreDbContextFactory(PrimaryDatabaseConnectionOptions options, int poolSize = 128)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Role != PrimaryDatabaseRole.Runtime)
            throw new ArgumentException("Tenant contexts require ordinary Runtime database options.", nameof(options));

        var builder = new DbContextOptionsBuilder<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(builder, options);
        if (options.Provider == PrimaryDatabaseProvider.PostgreSql)
            builder.AddInterceptors(PostgresTenantSessionInterceptor.RuntimeInstance);
        _pool = new PooledDbContextFactory<ExploreDbContext>(builder.Options, poolSize);
    }

    public async Task<ExploreDbContext> CreateDbContextAsync(
        Guid tenantId, CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("Tenant contexts require an exact tenant.", nameof(tenantId));

        var context = await _pool.CreateDbContextAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.ClearTenantFilterBypass();
            context.TenantContext = new TenantIdentity(tenantId);
            context.CurrentUserService = null;
            context.BindTenantLease();
            return context;
        }
        catch
        {
            await context.DisposeAsync();
            throw;
        }
    }

    private sealed record TenantIdentity(Guid TenantId) : ITenantContext;
}
