using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Explore.Persistence.Database;

/// <summary>Owns identity-free leases from the separately configured Global credential pool.</summary>
public sealed class GlobalExploreDbContextFactory : IGlobalExploreDbContextFactory
{
    private readonly PooledDbContextFactory<ExploreDbContext> _pool;

    public GlobalExploreDbContextFactory(PrimaryDatabaseConnectionOptions options, int poolSize = 128)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Role != PrimaryDatabaseRole.Global)
            throw new ArgumentException("Global contexts require explicit Global database options.", nameof(options));

        var builder = new DbContextOptionsBuilder<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(builder, options);
        if (options.Provider == PrimaryDatabaseProvider.PostgreSql)
            builder.AddInterceptors(PostgresTenantSessionInterceptor.Instance);
        _pool = new PooledDbContextFactory<ExploreDbContext>(builder.Options, poolSize);
    }

    public async Task<ExploreDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
    {
        var context = await _pool.CreateDbContextAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.ClearTenantFilterBypass();
            context.TenantContext = null;
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
}
