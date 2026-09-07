using System.ComponentModel.DataAnnotations.Schema;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class Notification : ITenantEntity, IAuditableEntity, ISoftDeletable
{
    public Guid Id { get; set; }

    public Guid? NotificationIntentId { get; set; }
    public NotificationIntent? NotificationIntent { get; set; }

    [ForeignKey("User")]
    public Guid UserId { get; set; }
    public required User User { get; set; }

    [ForeignKey("NotificationType")]
    public int NotificationTypeId { get; set; }
    public required NotificationType NotificationType { get; set; }

    public required string Title { get; set; }
    public string? Body { get; set; }
    public required string DeduplicationKey { get; set; }

    public bool IsRead { get; set; }
    public DateTime? ReadAt { get; set; }

    /// <summary>
    /// Optional: the type of entity this notification relates to (e.g., Event, Organization).
    /// </summary>
    [ForeignKey("NotificationEntityType")]
    public int? NotificationEntityTypeId { get; set; }
    public NotificationEntityType? NotificationEntityType { get; set; }

    /// <summary>
    /// Optional: the ID of the related entity for deep linking.
    /// </summary>
    public string? EntityId { get; set; }

    /// <summary>
    /// The scope of this notification: Personal/User, Organization, Group, or System.
    /// </summary>
    [ForeignKey("NotificationScope")]
    public int NotificationScopeId { get; set; }
    public required NotificationScopeType NotificationScope { get; set; }

    /// <summary>
    /// Optional: the actor (user, org, bot, system) that caused this notification.
    /// </summary>
    [ForeignKey("SourceActor")]
    public Guid? SourceActorId { get; set; }
    public Actor? SourceActor { get; set; }

    /// <summary>
    /// Optional: the actor context in which the recipient sees this notification.
    /// Null or user's own actor = personal. Org actor = org scope. Group actor = group scope.
    /// </summary>
    [ForeignKey("RecipientContextActor")]
    public Guid? RecipientContextActorId { get; set; }
    public Actor? RecipientContextActor { get; set; }

    [ForeignKey("NotificationReason")]
    public int? NotificationReasonId { get; set; }
    public NotificationReason? NotificationReason { get; set; }

    public bool IsArchived { get; set; }
    public DateTime? ArchivedAt { get; set; }

    public DateTime? SnoozedUntil { get; set; }

    [ForeignKey("Tenant")]
    public Guid TenantId { get; set; }
    public required Tenant Tenant { get; set; }

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
