using System;
using System.ComponentModel.DataAnnotations.Schema;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class EventSessionLanguage : ITenantEntity, IConcurrencyAware
{
    public int Id { get; set; }

    public Guid ConcurrencyStamp { get; set; }

    [ForeignKey("EventSession")]
    public Guid EventSessionId { get; set; }
    public required EventSession EventSession { get; set; }

    [ForeignKey("Language")]
    public int LanguageId { get; set; }
    public required Language Language { get; set; }

    [ForeignKey("Tenant")]
    public Guid TenantId { get; set; }
    public required Tenant Tenant { get; set; }
}
