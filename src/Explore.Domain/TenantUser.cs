using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class TenantUser : ITenantEntity, IAuditableEntity, ISoftDeletable
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public required Tenant Tenant { get; set; }
    public Guid UserId { get; set; }
    public required User User { get; set; }
    public Guid? ActorId { get; set; }
    public Actor? Actor { get; set; }
    public int StatusId { get; set; }
    public DateTime? JoinedAt { get; set; }
    public DateTime? SuspendedAt { get; set; }
    public Guid? SuspendedBy { get; set; }
    public DateTime? BanExpiresAt { get; set; }
    public DateTime? RemovedAt { get; set; }
    public Guid? RemovedBy { get; set; }
    public string? ModerationNote { get; set; }
    public TenantUserProfile? Profile { get; set; }
    public ICollection<TenantUserRoleGrant> RoleGrants { get; set; } = new List<TenantUserRoleGrant>();
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
}
