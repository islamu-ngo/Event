namespace Explore.Application.DTOs.Instance;

public sealed record DomainSettingsDto
{
    public string InstanceBaseDomain { get; set; } = string.Empty;
    public string AdminHost { get; set; } = string.Empty;
    public bool AllowTenantCustomDomains { get; set; } = true;
    public bool LockTenantSubdomain { get; set; }
    public bool LockTenantCustomDomain { get; set; }
}
