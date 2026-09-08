using Explore.Domain.Enums;

namespace Explore.Infrastructure;

/// <summary>
/// Configuration settings for deployment mode.
/// Bind from appsettings.json section "Deployment".
/// </summary>
public class DeploymentSettings
{
    /// <summary>
    /// Configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "Deployment";

    /// <summary>
    /// Deployment mode: SingleTenant or MultiTenant.
    /// Default: SingleTenant.
    /// </summary>
    public DeploymentMode Mode { get; set; } = DeploymentMode.SingleTenant;

    /// <summary>
    /// Default tenant ID used in SingleTenant mode.
    /// All entities will use this tenant ID.
    /// </summary>
    public Guid DefaultTenantId { get; set; } = Guid.Empty;

    /// <summary>
    /// Whether to hide platform-admin endpoints in SingleTenant mode.
    /// When true, tenant management and system settings endpoints return 404.
    /// Default: true.
    /// </summary>
    public bool HidePlatformAdminInSingleTenant { get; set; } = true;

    /// <summary>
    /// Default tenant subdomain used for URL generation in SingleTenant mode.
    /// </summary>
    public string? DefaultTenantSubdomain { get; set; }

    /// <summary>
    /// Whether the deployment is in single-tenant mode.
    /// </summary>
    public bool IsSingleTenant => Mode == DeploymentMode.SingleTenant;

    /// <summary>
    /// Whether the deployment is in multi-tenant mode.
    /// </summary>
    public bool IsMultiTenant => Mode == DeploymentMode.MultiTenant;
}
