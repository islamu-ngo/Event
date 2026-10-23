using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Explore.Persistence.Database;

/// <summary>Separate instance and exact-tenant email mutation pools, never Runtime or Global fallbacks.</summary>
public sealed class EmailPolicyMutationDbContextFactories
{
    private readonly PooledDbContextFactory<ExploreDbContext> _instance;
    private readonly PooledDbContextFactory<ExploreDbContext> _tenant;

    public EmailPolicyMutationDbContextFactories(
        PrimaryDatabaseConnectionOptions runtime,
        PrimaryDatabaseConnectionOptions global,
        PrimaryDatabaseConnectionOptions instanceMutation,
        PrimaryDatabaseConnectionOptions tenantMutation)
    {
        if (runtime.Role != PrimaryDatabaseRole.Runtime || global.Role != PrimaryDatabaseRole.Global
            || instanceMutation.Role != PrimaryDatabaseRole.EmailPolicyInstanceMutation
            || tenantMutation.Role != PrimaryDatabaseRole.EmailPolicyTenantMutation)
            throw new InvalidOperationException("email_policy_mutation_configuration_incomplete");

        PrimaryDatabaseConnectionOptions[] authorities = [runtime, global, instanceMutation, tenantMutation];
        foreach (var options in new[] { instanceMutation, tenantMutation })
        {
            if (options.Provider != runtime.Provider || options.Host != runtime.Host || options.Port != runtime.Port
                || options.Database != runtime.Database || options.Schema != runtime.Schema
                || options.TlsMode != runtime.TlsMode || options.TrustServerCertificate != runtime.TrustServerCertificate
                || options.ServerFlavor != runtime.ServerFlavor || options.ServerVersion != runtime.ServerVersion)
                throw new InvalidOperationException("email_policy_mutation_database_mismatch");
        }
        foreach (var options in authorities)
            PrimaryDatabaseConfiguration.Validate(options);
        if (runtime.Provider != PrimaryDatabaseProvider.Sqlite
            && (authorities.Select(options => options.Username).Distinct(StringComparer.Ordinal).Count() != authorities.Length
                || authorities.Select(options => options.Password).Distinct(StringComparer.Ordinal).Count() != authorities.Length))
            throw new InvalidOperationException("email_policy_mutation_credentials_must_be_distinct");

        _instance = CreatePool(instanceMutation);
        _tenant = CreatePool(tenantMutation);
    }

    internal async Task<ExploreDbContext> CreateAsync(PolicyMutationScope selection, CancellationToken cancellationToken)
    {
        if (selection.Purpose != PolicyMutationPurpose.EmailDelivery || selection.TenantId == Guid.Empty)
            throw new ArgumentException("Email mutation requires an instance or exact tenant purpose.", nameof(selection));
        var context = await (selection.TenantId.HasValue ? _tenant : _instance).CreateDbContextAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.ClearTenantFilterBypass();
            context.TenantContext = selection.TenantId is { } tenantId ? new TenantIdentity(tenantId) : null;
            context.CurrentUserService = null;
            context.BindTenantLease();
            // Opening uses the ordinary immutable-lease admission interceptor, not SET ROLE.
            await context.Database.OpenConnectionAsync(cancellationToken);
            if (selection.TenantId is { } target
                && !await context.Tenants.AsNoTracking().AnyAsync(tenant => tenant.Id == target, cancellationToken))
                throw new InvalidOperationException("email_policy_mutation_tenant_unavailable");
            return context;
        }
        catch
        {
            await context.DisposeAsync();
            throw;
        }
    }

    private static PooledDbContextFactory<ExploreDbContext> CreatePool(PrimaryDatabaseConnectionOptions options)
    {
        var builder = new DbContextOptionsBuilder<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(builder, options);
        if (options.Provider == PrimaryDatabaseProvider.PostgreSql)
            builder.AddInterceptors(PostgresTenantSessionInterceptor.RuntimeInstance);
        return new PooledDbContextFactory<ExploreDbContext>(builder.Options, 32);
    }

    private sealed record TenantIdentity(Guid TenantId) : ITenantContext;
}
