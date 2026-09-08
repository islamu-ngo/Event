using System.ComponentModel.DataAnnotations.Schema;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class TenantInvitation : ITenantEntity, IAuditableEntity
{
    public Guid Id { get; set; }

    [ForeignKey(nameof(Tenant))]
    public Guid TenantId { get; set; }
    public required Tenant Tenant { get; set; }

    /// <summary>
    /// Email address of the invited user.
    /// </summary>
    public required string Email { get; set; }

    /// <summary>
    /// Role to assign upon acceptance. Follows role ceiling enforcement in handler.
    /// </summary>
    [ForeignKey(nameof(Role))]
    public int RoleId { get; set; }
    public required Role Role { get; set; }

    /// <summary>
    /// Unique, one-time invitation token for secure acceptance.
    /// </summary>
    public required string Token { get; set; }

    /// <summary>
    /// When the invitation expires (UTC).
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// Whether the invitation has been accepted.
    /// </summary>
    public bool IsAccepted { get; set; }

    /// <summary>
    /// When the invitation was accepted (UTC). Null if not yet accepted.
    /// </summary>
    public DateTime? AcceptedAt { get; set; }

    /// <summary>
    /// User who accepted the invitation. Null if not yet accepted.
    /// </summary>
    public Guid? AcceptedByUserId { get; set; }

    /// <summary>
    /// User who created the invitation.
    /// </summary>
    public Guid? InvitedByUserId { get; set; }

    /// <summary>
    /// Optional email domain constraint. When set, only users with a matching
    /// email domain may accept this invitation (e.g., "example.com").
    /// </summary>
    public string? AllowedDomain { get; set; }

    // IAuditableEntity
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}
