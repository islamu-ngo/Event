using System.ComponentModel.DataAnnotations.Schema;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class AuditLog : ITenantEntity
{
    public Guid Id { get; set; }

    /// <summary>
    /// The CLR type name of the entity that was changed (e.g., "Event", "Organization").
    /// </summary>
    public required string EntityType { get; set; }

    /// <summary>
    /// The primary key of the entity that was changed.
    /// </summary>
    public required string EntityId { get; set; }

    /// <summary>
    /// The type of change: Created, Updated, Deleted, SoftDeleted.
    /// </summary>
    public required string Action { get; set; }

    /// <summary>
    /// JSON snapshot of the old values (null for Created).
    /// </summary>
    public string? OldValues { get; set; }

    /// <summary>
    /// JSON snapshot of the new values (null for Deleted).
    /// </summary>
    public string? NewValues { get; set; }

    /// <summary>
    /// JSON array of property names that changed (for Updates).
    /// </summary>
    public string? AffectedColumns { get; set; }

    /// <summary>
    /// The user who performed the action.
    /// </summary>
    public Guid? ActorId { get; set; }

    /// <summary>
    /// When the action occurred (UTC).
    /// </summary>
    public DateTime Timestamp { get; set; }

    [ForeignKey("Tenant")]
    public Guid TenantId { get; set; }
    public required Tenant Tenant { get; set; }
}
