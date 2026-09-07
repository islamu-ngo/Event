using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class Group : IAuditableEntity, ISoftDeletable, IConcurrencyAware
{
    public Guid Id { get; set; }
    public required string FullName { get; set; }
    public string? Description { get; set; }

    public Actor? Actor { get; set; }

    public ICollection<GroupTenant> TenantParticipations { get; set; } = [];

    // Audit fields
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    // Soft delete fields
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    // Concurrency control
    public Guid ConcurrencyStamp { get; set; }

}
