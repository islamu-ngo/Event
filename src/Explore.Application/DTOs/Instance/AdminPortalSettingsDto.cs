namespace Explore.Application.DTOs.Instance;

public sealed record AdminPortalSettingsDto
{
    public bool Enabled { get; set; } = true;
    public string PublicUrl { get; set; } = string.Empty;
    public bool AllowTenantAdminAccess { get; set; }
}
