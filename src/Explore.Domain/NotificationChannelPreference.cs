using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class NotificationChannelPreference : ITenantEntity, IAuditableEntity, ISoftDeletable, IConcurrencyAware
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public required Tenant Tenant { get; set; }

    public int ScopeId { get; set; }
    public required SettingScopeLookup Scope { get; set; }

    public Guid? UserId { get; set; }
    public User? User { get; set; }

    public Guid? OrganizationId { get; set; }
    public Organization? Organization { get; set; }

    public Guid? GroupId { get; set; }
    public Group? Group { get; set; }

    public int CategoryId { get; set; }
    public required NotificationPreferenceCategory Category { get; set; }

    public int ChannelId { get; set; }
    public required NotificationPreferenceChannel Channel { get; set; }

    public bool IsEnabled { get; set; }
    public bool IsLocked { get; set; }

    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    public Guid ConcurrencyStamp { get; set; }
}
