using Explore.Application.Contracts.Infrastructure;
using Explore.Persistence.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Explore.Persistence;

public partial class ExploreDbContext : DbContext
{
    private ITenantContext? _tenantContext;
    private string? _tenantFilterBypassReason;

    /// <summary>
    /// Tenant context for multi-tenant data isolation.
    /// Set via property injection after DbContext is retrieved from pool.
    /// When null, tenant filters fail closed unless an explicit bypass reason is enabled.
    /// </summary>
    public ITenantContext? TenantContext
    {
        get => _tenantContext;
        set
        {
            _tenantContext = value;
            if (value is not null)
            {
                ClearTenantFilterBypass();
            }
        }
    }

    /// <summary>
    /// Tenant id consumed by global query filters. Null means no ambient tenant is bound.
    /// </summary>
    public Guid? TenantFilterTenantId => TenantContext?.TenantId;

    /// <summary>
    /// True only for explicit system/admin flows that intentionally query across tenants.
    /// </summary>
    public bool IsTenantFilterBypassed => _tenantFilterBypassReason is not null;

    /// <summary>
    /// Human-readable reason for the current explicit tenant-filter bypass.
    /// </summary>
    public string? TenantFilterBypassReason => _tenantFilterBypassReason;

    /// <summary>
    /// Current user service for audit field population.
    /// Set via property injection after DbContext is retrieved from pool.
    /// When null (e.g., during migrations), audit fields use null values.
    /// </summary>
    public ICurrentUserService? CurrentUserService { get; set; }

    public ExploreDbContext(DbContextOptions<ExploreDbContext> options) : base(options)
    {
    }

    public void EnableTenantFilterBypass(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A tenant filter bypass requires a non-empty reason.", nameof(reason));
        }

        _tenantFilterBypassReason = reason;
    }

    public void ClearTenantFilterBypass()
    {
        _tenantFilterBypassReason = null;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(ExploreDbContext).Assembly,
            configurationType => configurationType.Namespace
                is not "Explore.Persistence.Privacy.ErasureAuthority.Configurations");
        PortableRelationalModelPolicy.Apply(modelBuilder, Database.ProviderName);
        var schema = this.GetService<IDbContextOptions>()
            .FindExtension<Database.RelationalNamespaceOptionsExtension>()?.ModelSchema
            ?? RelationalModelNamespace.DefaultSchema;
        RelationalModelNamespace.Apply(modelBuilder, Database.ProviderName, schema);
        if (Database.ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL")
        {
            modelBuilder.HasPostgresExtension("btree_gist");
        }
        else
        {
            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                foreach (var annotation in entityType.GetAnnotations()
                             .Where(annotation => PostgresExclusionConstraintExtensions.IsExclusionConstraintAnnotation(annotation.Name))
                             .ToArray())
                {
                    entityType.RemoveAnnotation(annotation.Name);
                }
            }
        }

        MySqlModelIdentifierPolicy.Apply(modelBuilder, Database.ProviderName);
        ApplyGlobalQueryFilters(modelBuilder);
    }
}
