namespace Explore.Domain.Policies;

public sealed class TenantPolicySet
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public EventPolicy Events { get; set; } = new();
    public OrganizationPolicy Organizations { get; set; } = new();
    public BrandingPolicy Branding { get; set; } = new();
    public RenderPolicy RenderPolicy { get; set; } = new();

    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public uint RowVersion { get; set; }
}
