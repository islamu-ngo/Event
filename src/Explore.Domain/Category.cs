using System;
using System.ComponentModel.DataAnnotations.Schema;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class Category : ITenantEntity, IConcurrencyAware
{
    public Guid Id { get; set; }
    public Guid ConcurrencyStamp { get; set; }
    public string MasterCode { get; set; }
    public string FullName { get; set; }

    [ForeignKey("Parent")]
    public Guid? ParentId { get; set; }
    public Category? Parent { get; set; }

    [ForeignKey("Tenant")]
    public Guid TenantId { get; set; }
    public required Tenant Tenant { get; set; }
}
