namespace Explore.Domain;

using Explore.Domain.Interfaces;

public class UserNotificationPreference : ITenantEntity, IAuditableEntity
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }
    public required Tenant Tenant { get; set; }

    public Guid UserId { get; set; }

    public required string Category { get; set; }

    public bool IsEnabled { get; set; }

    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}
