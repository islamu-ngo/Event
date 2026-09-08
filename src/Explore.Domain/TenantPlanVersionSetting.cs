using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class TenantPlanVersionSetting : IAuditableEntity
{
    public Guid Id { get; set; }
    public Guid TenantPlanVersionId { get; set; }
    public TenantPlanVersion TenantPlanVersion { get; set; } = null!;
    public required string SettingKey { get; set; }
    public required string JsonValue { get; set; }
    public bool IsLocked { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}
