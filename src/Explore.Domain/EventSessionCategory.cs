using System;
using System.ComponentModel.DataAnnotations.Schema;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class EventSessionCategory : ITenantEntity, IAuditableEntity
{
    public Guid Id { get; set; }

    [ForeignKey("EventSession")]
    public Guid EventSessionId { get; set; }
    public required EventSession EventSession { get; set; }

    [ForeignKey("Category")]
    public Guid CategoryId { get; set; }
    public required Category Category { get; set; }

    [ForeignKey("Tenant")]
    public Guid TenantId { get; set; }
    public required Tenant Tenant { get; set; }

    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}
