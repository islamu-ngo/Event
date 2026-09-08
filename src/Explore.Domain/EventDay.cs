using System;
using System.ComponentModel.DataAnnotations.Schema;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class EventDay : ITenantEntity, IAuditableEntity, ISoftDeletable, IConcurrencyAware
{
    public Guid Id { get; set; }

    [ForeignKey("Event")]
    public Guid EventId { get; set; }
    public required Event Event { get; set; }

    public DateOnly LocalDate { get; set; }

    public string? Label { get; set; }
    public string? Description { get; set; }
    public string? BannerText { get; set; }

    [ForeignKey("BannerImage")]
    public Guid? BannerImageId { get; set; }
    public StorageObject? BannerImage { get; set; }

    public bool IsPublished { get; set; }
    public int SortOrder { get; set; }

    public bool AllowsDayScopeRegistration { get; set; }

    [ForeignKey("Tenant")]
    public Guid TenantId { get; set; }
    public required Tenant Tenant { get; set; }

    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    public Guid ConcurrencyStamp { get; set; }
}
