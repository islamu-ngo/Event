using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class TenantPlanVersionQuota : IAuditableEntity
{
    public Guid Id { get; set; }
    public Guid TenantPlanVersionId { get; set; }
    public TenantPlanVersion TenantPlanVersion { get; set; } = null!;
    public required string QuotaKey { get; set; }
    public long Limit { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}
