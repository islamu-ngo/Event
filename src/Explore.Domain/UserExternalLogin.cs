using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class UserExternalLogin : IAuditableEntity
{
    public Guid Id { get; set; }

    [ForeignKey("User")]
    public Guid UserId { get; set; }
    public required User User { get; set; }

    [ForeignKey(nameof(AuthenticationProvider))]
    public int AuthenticationProviderId { get; set; }
    public required AuthenticationProvider AuthenticationProvider { get; set; }

    public required string ProviderKey { get; set; }
    public string? ProviderDisplayName { get; set; }

    // Audit fields
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}
