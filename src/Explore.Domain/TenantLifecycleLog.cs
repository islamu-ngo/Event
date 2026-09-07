using System.ComponentModel.DataAnnotations.Schema;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class TenantLifecycleLog : IAuditableEntity
{
    public Guid Id { get; set; }

    /// <summary>
    /// Tenant whose lifecycle changed.
    /// </summary>
    [ForeignKey(nameof(Tenant))]
    public Guid TenantId { get; set; }
    public required Tenant Tenant { get; set; }

    /// <summary>
    /// Previous status before the transition. Null for initial provisioning.
    /// </summary>
    [ForeignKey(nameof(OldStatus))]
    public int? OldStatusId { get; set; }
    public TenantStatus? OldStatus { get; set; }

    /// <summary>
    /// New status after the transition.
    /// </summary>
    [ForeignKey(nameof(NewStatus))]
    public int NewStatusId { get; set; }
    public required TenantStatus NewStatus { get; set; }

    /// <summary>
    /// User who triggered the status transition.
    /// </summary>
    public Guid? TransitionedByUserId { get; set; }

    /// <summary>
    /// Optional reason for the transition (required for Suspend/Archive).
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// When the transition occurred (UTC).
    /// </summary>
    public DateTime TransitionedAt { get; set; }

    // IAuditableEntity
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}
