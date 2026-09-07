// ABOUTME: Tenant-scoped operational control row for Basic Dispatch Mode email sending.
// ABOUTME: Stores tenant-isolated operator controls, delivery-policy revisions, and optional suppression history.

using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class EmailDispatchTenantControl : ITenantEntity, IAuditableEntity
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;

    public long DeliveryPolicyRevision { get; set; }
    public long? OptionalSuppressedThroughRevision { get; set; }
    public DateTime? OptionalSuppressedThroughUtc { get; set; }

    public bool IsPaused { get; set; }
    public string? PauseReason { get; set; }
    public DateTime? PausedAt { get; set; }
    public Guid? PausedBy { get; set; }

    public int? SmtpAvailableTokens { get; set; }
    public DateTime? SmtpRefillAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}
