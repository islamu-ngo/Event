using System;
using System.ComponentModel.DataAnnotations.Schema;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class User : IAuditableEntity, ISoftDeletable, IConcurrencyAware
{
    public Guid Id { get; set; }

    /// <summary>
    /// 1:1 extension table containing sensitive user identity fields.
    /// </summary>
    public required UserPii Pii { get; set; }

    [NotMapped]
    public string Email
    {
        get => Pii.Email;
        set => Pii.Email = value;
    }

    [NotMapped]
    public string FirstName
    {
        get => Pii.FirstName;
        set => Pii.FirstName = value;
    }

    [NotMapped]
    public string LastName
    {
        get => Pii.LastName;
        set => Pii.LastName = value;
    }

    public Actor? Actor { get; set; }

    public bool? EmailVerified { get; set; }
    public Guid ConcurrencyStamp { get; set; }
    public Guid? LastActiveTenantId { get; set; }

    // Audit fields
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    // Soft delete fields
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

}
