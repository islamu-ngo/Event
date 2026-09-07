using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class TenantPlanVersion : IAuditableEntity
{
    public Guid Id { get; set; }
    public Guid TenantPlanId { get; set; }
    public TenantPlan TenantPlan { get; set; } = null!;
    public int VersionNumber { get; set; }
    public int TenantPlanStatusId { get; set; }
    public TenantPlanStatus TenantPlanStatus { get; set; } = null!;
    public decimal PriceAmount { get; set; }
    public required string CurrencyCode { get; set; }
    public required string BillingPeriod { get; set; }
    public bool IsActiveForProvisioning { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public ICollection<TenantPlanVersionSetting> Settings { get; set; } = [];
    public ICollection<TenantPlanVersionQuota> Quotas { get; set; } = [];
    public ICollection<TenantPlanAssignment> Assignments { get; set; } = [];
}
