using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class TenantPlanAssignment : IAuditableEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;
    public Guid TenantPlanId { get; set; }
    public TenantPlan TenantPlan { get; set; } = null!;
    public Guid TenantPlanVersionId { get; set; }
    public TenantPlanVersion TenantPlanVersion { get; set; } = null!;
    public int TenantPlanAssignmentStatusId { get; set; }
    public TenantPlanAssignmentStatus TenantPlanAssignmentStatus { get; set; } = null!;
    public Guid? AssignedByUserId { get; set; }
    public DateTime AssignedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public ICollection<TenantPlanApplicationLog> ApplicationLogs { get; set; } = [];
}
