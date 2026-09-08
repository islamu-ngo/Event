using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class GroupTenant : ITenantEntity, IAuditableEntity, ISoftDeletable, IConcurrencyAware
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public required Tenant Tenant { get; set; }
    public Guid GroupId { get; set; }
    public required Group Group { get; set; }
    public int ApprovalStatusId { get; set; }
    public required ApprovalStatus ApprovalStatus { get; set; }
    public bool IsVisible { get; set; }
    public bool IsOrganizerEligible { get; set; }
    public bool IsSuspended { get; set; }
    public DateTime? SuspendedAt { get; set; }
    public Guid? SuspendedBy { get; set; }
    public string? ModerationNote { get; set; }
    public string? DisplayNameOverride { get; set; }
    public string? DescriptionOverride { get; set; }
    public Guid? ProfilePictureId { get; set; }
    public StorageObject? ProfilePicture { get; set; }
    public Guid? BannerPictureId { get; set; }
    public StorageObject? BannerPicture { get; set; }
    public Guid? BackgroundImageId { get; set; }
    public StorageObject? BackgroundImage { get; set; }
    public string? BackgroundColor { get; set; }
    public string? BackgroundEffect { get; set; }
    public string? BannerColor { get; set; }
    public Guid? ParentOrganizationTenantId { get; set; }
    public OrganizationTenant? ParentOrganizationTenant { get; set; }
    public Guid? ParentGroupTenantId { get; set; }
    public GroupTenant? ParentGroupTenant { get; set; }
    public ICollection<GroupTenant> ChildGroups { get; set; } = [];
    public ICollection<GroupMember> Members { get; set; } = [];
    public ICollection<GroupSetting> Settings { get; set; } = [];
    public DateTime? ApprovedAt { get; set; }
    public Guid? ApprovedBy { get; set; }
    public string? ApprovalNotes { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
    public Guid ConcurrencyStamp { get; set; }
}
