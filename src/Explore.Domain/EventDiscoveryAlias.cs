using Explore.Domain.Interfaces;

namespace Explore.Domain;

/// <summary>A direct, reviewed discovery relationship, independent of attendee obligations.</summary>
public sealed class EventDiscoveryAlias : ITenantEntity, IAuditableEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid MemberIdentityId { get; set; }
    public EventDiscoveryIdentity Member { get; set; } = null!;
    public Guid PrimaryIdentityId { get; set; }
    public EventDiscoveryIdentity Primary { get; set; } = null!;
    public long RelationshipRevision { get; set; }
    public Guid ReviewerId { get; set; }
    public string ReasonCode { get; set; } = string.Empty;
    public DateTime ReviewedAtUtc { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}
