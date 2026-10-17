using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Explore.Persistence.Database;

/// <summary>Four independent credential pools; none wraps or mutates a request/Global lease.</summary>
public sealed class InstanceApiKeyDbContextFactories
{
    private readonly IReadOnlyDictionary<PrimaryDatabaseRole, PooledDbContextFactory<ExploreDbContext>> _pools;
    internal PlatformApiKeySourceEnrollment? SourceEnrollment { get; }

    public InstanceApiKeyDbContextFactories(
        PrimaryDatabaseConnectionOptions runtime,
        PrimaryDatabaseConnectionOptions global,
        IReadOnlyList<PrimaryDatabaseConnectionOptions> capabilities,
        PlatformApiKeySourceEnrollment? sourceEnrollment = null)
    {
        SourceEnrollment = sourceEnrollment;
        if (runtime.Provider == PrimaryDatabaseProvider.PostgreSql)
        {
            (sourceEnrollment ?? throw new InvalidOperationException("platform_key_source_enrollment_required")).Validate();
            if (capabilities.Single(options => options.Role == PrimaryDatabaseRole.InstanceKeyRead).Username != sourceEnrollment.ReadLogin
                || capabilities.Single(options => options.Role == PrimaryDatabaseRole.InstanceKeyWrite).Username != sourceEnrollment.WriteLogin
                || capabilities.Single(options => options.Role == PrimaryDatabaseRole.PlatformKeyReport).Username != sourceEnrollment.ReportLogin)
                throw new InvalidOperationException("platform_key_source_pool_enrollment_mismatch");
        }
        PrimaryDatabaseRole[] required =
        [
            PrimaryDatabaseRole.InstanceKeyRead, PrimaryDatabaseRole.InstanceKeyWrite,
            PrimaryDatabaseRole.InstanceKeyIssuance, PrimaryDatabaseRole.PlatformKeyReport
        ];
        if (capabilities.Count != required.Length
            || !required.All(role => capabilities.Count(options => options.Role == role) == 1))
            throw new InvalidOperationException("instance_api_key_capability_configuration_incomplete");
        var authorities = capabilities.Prepend(global).Prepend(runtime).ToArray();
        foreach (var options in capabilities)
        {
            PrimaryDatabaseConfiguration.Validate(options);
            if (options.Provider != runtime.Provider || options.Host != runtime.Host || options.Port != runtime.Port
                || options.Database != runtime.Database || options.Schema != runtime.Schema
                || options.TlsMode != runtime.TlsMode || options.TrustServerCertificate != runtime.TrustServerCertificate)
                throw new InvalidOperationException("instance_api_key_capability_database_mismatch");
        }
        if (runtime.Provider != PrimaryDatabaseProvider.Sqlite
            && (authorities.Select(options => options.Username).Distinct(StringComparer.Ordinal).Count() != authorities.Length
                || authorities.Select(options => options.Password).Distinct(StringComparer.Ordinal).Count() != authorities.Length))
            throw new InvalidOperationException("instance_api_key_capability_credentials_must_be_distinct");
        _pools = capabilities.ToDictionary(options => options.Role, options =>
        {
            var builder = new DbContextOptionsBuilder<ExploreDbContext>();
            PrimaryDatabaseProviderComposition.ConfigureApplication(builder, options);
            if (options.Provider == PrimaryDatabaseProvider.PostgreSql)
                builder.AddInterceptors(PostgresTenantSessionInterceptor.RuntimeInstance);
            return new PooledDbContextFactory<ExploreDbContext>(builder.Options, 32);
        });
    }

    internal async Task<ExploreDbContext> CreateAsync(PrimaryDatabaseRole role, CancellationToken cancellationToken)
    {
        if (!_pools.TryGetValue(role, out var pool))
            throw new ArgumentOutOfRangeException(nameof(role));
        var context = await pool.CreateDbContextAsync(cancellationToken);
        try
        {
            context.ClearTenantFilterBypass();
            context.TenantContext = null;
            context.CurrentUserService = null;
            context.BindTenantLease();
            await context.Database.OpenConnectionAsync(cancellationToken);
            if (context.Database.IsNpgsql())
            {
                var sql = new InstanceApiKeyCapabilitySql(context);
                switch (role)
                {
                    case PrimaryDatabaseRole.InstanceKeyRead:
                        await sql.RequireReadyAsync(PostgresInstanceApiKeyManagementContract.ReadExecutor,
                            PostgresInstanceApiKeyManagementContract.Functions(context)
                                .Where(function => function.Executor == PostgresInstanceApiKeyManagementContract.ReadExecutor).ToArray(),
                            cancellationToken, SourceEnrollment);
                        break;
                    case PrimaryDatabaseRole.InstanceKeyWrite:
                        await sql.RequireReadyAsync(PostgresInstanceApiKeyManagementContract.WriteExecutor,
                            PostgresInstanceApiKeyManagementContract.Functions(context)
                                .Where(function => function.Executor == PostgresInstanceApiKeyManagementContract.WriteExecutor).ToArray(),
                            cancellationToken, SourceEnrollment);
                        break;
                    case PrimaryDatabaseRole.InstanceKeyIssuance:
                        await sql.RequireReadyAsync(PostgresInstanceApiKeyIssuanceContract.ExecutorRole,
                            PostgresInstanceApiKeyIssuanceContract.Functions(context), cancellationToken);
                        break;
                    case PrimaryDatabaseRole.PlatformKeyReport:
                        await sql.RequireReadyAsync(PostgresPlatformApiKeyUsageReportContract.ExecutorRole,
                            [PostgresPlatformApiKeyUsageReportContract.Function(context)], cancellationToken, SourceEnrollment);
                        break;
                }
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

    public async Task RequireReadyAsync(CancellationToken cancellationToken)
    {
        foreach (var role in _pools.Keys.Order())
            await using (await CreateAsync(role, cancellationToken)) { }
    }
}
