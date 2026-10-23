using Explore.Application.Contracts.Infrastructure;
using Explore.Persistence.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Explore.Persistence;

public partial class ExploreDbContext : DbContext
{
    private ITenantContext? _tenantContext;
    private string? _tenantFilterBypassReason;
    private bool _tenantLeaseBound;
    private Guid? _tenantLeaseId;
    private ITenantContext? _tenantLeaseIdentity;
    internal bool IsPostgresRuntimeReady { get; set; }
    internal bool IsConfigurationInstanceImportOwner { get; private set; }

    internal void BindConfigurationInstanceImportOwner()
    {
        if (BindTenantLease() is not null || IsConfigurationInstanceImportOwner)
            throw new InvalidOperationException("An instance import requires a fresh sealed global owner.");
        IsConfigurationInstanceImportOwner = true;
    }

    /// <summary>
    /// Tenant context for multi-tenant data isolation.
    /// Set via property injection after DbContext is retrieved from pool.
    /// When null, tenant filters fail closed unless an explicit bypass reason is enabled.
    /// </summary>
    public ITenantContext? TenantContext
    {
        get
        {
            ValidateTenantLease();
            return _tenantLeaseBound ? _tenantLeaseIdentity : _tenantContext;
        }
        set
        {
            ValidateTenantLease();
            if (_tenantLeaseBound && !ReferenceEquals(_tenantContext, value))
                throw new InvalidOperationException("tenant_lease_identity_changed");
            _tenantContext = value;
            ModerationTrustDomainId = null;
            if (value is not null)
            {
                ClearTenantFilterBypass();
            }
        }
    }

    /// <summary>
    /// Tenant id consumed by global query filters. Null means no ambient tenant is bound.
    /// </summary>
    public Guid? TenantFilterTenantId
    {
        get
        {
            ValidateTenantLease();
            return _tenantLeaseBound ? _tenantLeaseId : _tenantContext?.TenantId;
        }
    }

    internal Guid? BindTenantLease()
    {
        ValidateTenantLease();
        if (!_tenantLeaseBound)
        {
            _tenantLeaseId = NormalizeTenantId(_tenantContext?.TenantId);
            _tenantLeaseIdentity = _tenantLeaseId is { } id ? new TenantLeaseIdentity(id) : null;
            _tenantLeaseBound = true;
        }
        return _tenantLeaseId;
    }

    internal void ValidateTenantLease()
    {
        if (_tenantLeaseBound && NormalizeTenantId(_tenantContext?.TenantId) != _tenantLeaseId)
            throw new InvalidOperationException("tenant_lease_identity_changed");
    }

    /// <summary>Requires an existing immutable owner; read admission must never mint a missing lease.</summary>
    internal void RequireBoundTenantLease()
    {
        ValidateTenantLease();
        if (!_tenantLeaseBound)
            throw new InvalidOperationException("tenant_lease_not_bound");
    }

    public override void Dispose()
    {
        ClearTenantLease();
        base.Dispose();
        GC.SuppressFinalize(this);
    }

    public override ValueTask DisposeAsync()
    {
        ClearTenantLease();
        GC.SuppressFinalize(this);
        return base.DisposeAsync();
    }

    private void ClearTenantLease()
    {
        _nativePaymentReconciliationOwner = null;
        _nativeHumanTenantOwner = null;
        ResetHumanCommitAdmissions();
        ResetManagedWorkerCreationContextLease();
        _outboxExecutionClaim = null;
        ResetTrustedSetupSettingsAdmissions();
        ResetNativeWebhookClaims();
        IsPostgresRuntimeReady = false;
        IsConfigurationInstanceImportOwner = false;
        _tenantLeaseBound = false;
        _tenantLeaseId = null;
        _tenantLeaseIdentity = null;
        _tenantContext = null;
        _tenantFilterBypassReason = null;
        HumanCommitContextLease = null;
        HumanCommitAuditSubject = null;
        CurrentUserService = null;
    }

    private static Guid? NormalizeTenantId(Guid? tenantId) =>
        tenantId == Guid.Empty ? null : tenantId;

    private sealed record TenantLeaseIdentity(Guid TenantId) : ITenantContext;

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

    public ExploreDbContext(DbContextOptions<ExploreDbContext> options) : base(WithDisclosureInterceptor(options))
    {
    }

    private static DbContextOptions<ExploreDbContext> WithDisclosureInterceptor(DbContextOptions<ExploreDbContext> options)
    {
        var interceptor = Explore.Persistence.Database.EventDiscoveryDisclosureTransactionInterceptor.Instance;
        return options.FindExtension<CoreOptionsExtension>()?.Interceptors?.Contains(interceptor) == true
            ? options
            : new DbContextOptionsBuilder<ExploreDbContext>(options).AddInterceptors(interceptor).Options;
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
        ApplyModerationQueryFilters(modelBuilder);
        ApplyModerationPublicationQueryFilters(modelBuilder);
        ApplyModerationNoticeQueryFilters(modelBuilder);
        ApplyModerationCaseSourceQueryFilters(modelBuilder);
        ApplyPreFindingQueryFilters(modelBuilder);
        ApplyModerationRecognitionQueryFilters(modelBuilder);
    }
}
