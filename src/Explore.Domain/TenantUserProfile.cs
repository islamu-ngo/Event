using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class TenantUserProfile : ITenantEntity, IAuditableEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public required Tenant Tenant { get; set; }
    public Guid TenantUserId { get; set; }
    public required TenantUser TenantUser { get; set; }
    public string? DisplayNameOverride { get; set; }
    public string? ContactEmailOverride { get; set; }
    public string? Locale { get; set; }
    public string? TimeZone { get; set; }
    public string? PreferencesJson { get; set; }
    public string? ConsentJson { get; set; }
    public string? AdminNote { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}
