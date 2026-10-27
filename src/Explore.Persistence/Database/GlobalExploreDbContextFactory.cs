using Explore.Application.Contracts.Infrastructure;
using Explore.Persistence.Security;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Explore.Persistence.Database;

/// <summary>
/// Owns an independent pool; it never resolves request contexts or their scoped credentials.
/// PostgreSQL permissions, not a mutable context mode, bound the Global lookup capability.
/// </summary>
public sealed class GlobalExploreDbContextFactory :
    IGlobalExploreDbContextFactory, IPdsSettlementDbContextFactory, IPdsSyncQueueAdmissionDbContextFactory,
    IEmailDispatchAdmissionDbContextFactory, IAtprotoMaterializationDbContextFactory,
    INotificationFanoutAdmissionDbContextFactory, INotificationOutboxAdmissionDbContextFactory, IGenericOutboxAdmissionDbContextFactory,
    IWebPushDispatchAdmissionDbContextFactory,
    IWebhookLocalDeliveryAdmissionDbContextFactory, IPaymentReconciliationAdmissionDbContextFactory,
    IMixedSettingsDbContextFactory
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
        {
            string globalLogin = options.Username
                ?? throw new ArgumentException("PostgreSQL Global contexts require an explicit login.", nameof(options));
            builder.AddInterceptors(PostgresTenantSessionInterceptor.ForGlobalLogin(globalLogin));
        }
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

    async Task<ExploreDbContext> IMixedSettingsDbContextFactory.CreateMixedSettingsAsync(
        Guid? tenantId, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("Mixed settings require an exact tenant or instance target.", nameof(tenantId));
        var context = await _pool.CreateDbContextAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!context.Database.IsNpgsql() && !context.Database.IsSqlite())
                throw new NotSupportedException("mixed_settings_native_provider_not_supported");
            context.ClearTenantFilterBypass();
            context.TenantContext = tenantId is { } target ? new SettlementTenant(target) : null;
            context.CurrentUserService = null;
            context.BindTenantLease();
            // Retain the same physical connector from original observation through
            // settlement; the mixed source must not close and re-rent between them.
            await context.Database.OpenConnectionAsync(cancellationToken);
            return context;
        }
        catch
        {
            await context.DisposeAsync();
            throw;
        }
    }

    async Task<ExploreDbContext> IPdsSettlementDbContextFactory.CreateDbContextAsync(
        Guid tenantId, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("Settlement requires an exact persisted tenant.", nameof(tenantId));

        var context = await _pool.CreateDbContextAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.ClearTenantFilterBypass();
            context.TenantContext = new SettlementTenant(tenantId);
            context.CurrentUserService = null;
            context.BindTenantLease();
            if (context.Database.IsNpgsql())
            {
                await context.Database.OpenConnectionAsync(cancellationToken);
                await PostgresPdsSettlementContract.RequireReadyAsync(context, cancellationToken);
            }
            if (!await context.Tenants.AsNoTracking().AnyAsync(row => row.Id == tenantId, cancellationToken))
                throw new InvalidOperationException("pds_settlement_tenant_unavailable");
            cancellationToken.ThrowIfCancellationRequested();
            return context;
        }
        catch
        {
            await context.DisposeAsync();
            throw;
        }
    }

    async Task<ExploreDbContext> IAtprotoMaterializationDbContextFactory.CreateDbContextAsync(
        CancellationToken cancellationToken)
    {
        var context = await CreateDbContextAsync(cancellationToken);
        try
        {
            if (context.Database.IsNpgsql())
            {
                await context.Database.OpenConnectionAsync(cancellationToken);
                await PostgresAtprotoMaterializationContract.RequireReadyAsync(context, cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return context;
        }
        catch
        {
            await context.DisposeAsync();
            throw;
        }
    }

    private sealed record SettlementTenant(Guid TenantId) : ITenantContext;
}
