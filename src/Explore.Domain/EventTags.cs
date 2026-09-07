using System;
using System.ComponentModel.DataAnnotations.Schema;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class EventTags : ITenantEntity, IAuditableEntity, IConcurrencyAware
{
    public Guid Id { get; set; }
    public Guid ConcurrencyStamp { get; set; }

    [ForeignKey("Event")]
    public Guid EventId { get; set; }
    public required Event Event { get; set; }

    [ForeignKey("Tag")]
    public Guid TagId { get; set; }
    public required Tag Tag { get; set; }

    [ForeignKey("Tenant")]
    public Guid TenantId { get; set; }
    public required Tenant Tenant { get; set; }

    // Audit fields
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}
