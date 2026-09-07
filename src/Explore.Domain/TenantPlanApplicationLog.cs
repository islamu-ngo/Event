using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class TenantPlanApplicationLog : IAuditableEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;
    public Guid TenantPlanId { get; set; }
    public TenantPlan TenantPlan { get; set; } = null!;
    public Guid TenantPlanVersionId { get; set; }
    public TenantPlanVersion TenantPlanVersion { get; set; } = null!;
    public Guid? TenantPlanAssignmentId { get; set; }
    public TenantPlanAssignment? TenantPlanAssignment { get; set; }
    public int TenantPlanApplicationStatusId { get; set; }
    public TenantPlanApplicationStatus TenantPlanApplicationStatus { get; set; } = null!;
    public Guid? AppliedByUserId { get; set; }
    public DateTime AppliedAt { get; set; }
    public Guid? PreviousTenantPlanVersionId { get; set; }
    public TenantPlanVersion? PreviousTenantPlanVersion { get; set; }
    public required string ChangedSettingKeysJson { get; set; }
    public required string ChangedQuotaKeysJson { get; set; }
    public string? FailureReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}
