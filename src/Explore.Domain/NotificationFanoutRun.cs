using System.ComponentModel.DataAnnotations.Schema;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class NotificationFanoutRun : ITenantEntity, IAuditableEntity, IConcurrencyAware
{
    public const int MaxLeaseOwnerLength = 200;

    public Guid Id { get; set; }

    [ForeignKey("Tenant")]
    public Guid TenantId { get; set; }
    public required Tenant Tenant { get; set; }

    public required string FanoutKind { get; set; }

    [ForeignKey("NotificationEntityType")]
    public int NotificationEntityTypeId { get; set; }
    public required NotificationEntityType NotificationEntityType { get; set; }

    public Guid EntityId { get; set; }

    [ForeignKey("SourceActor")]
    public Guid SourceActorId { get; set; }
    public required Actor SourceActor { get; set; }

    public required string Status { get; set; }
    public Guid? CursorSubscriberTenantUserId { get; set; }
    public DateTime? CursorFirstEligibleRegistrationCreatedAt { get; set; }
    public Guid? CursorUserId { get; set; }
    public Guid? FanoutOccurrenceId { get; set; }
    public NotificationFanoutOccurrence? FanoutOccurrence { get; set; }
    public string? ProcessingLeaseOwner { get; set; }
    public Guid? ProcessingLeaseToken { get; set; }
    public DateTime? ProcessingLeaseExpiresAt { get; set; }
    public int ProcessingGeneration { get; set; }
    public long ProcessingFence { get; set; }
    public DateTime? HeartbeatAt { get; set; }
    public int ProcessedCount { get; set; }
    public int CreatedNotificationCount { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? FailedAt { get; set; }
    public string? LastError { get; set; }

    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public Guid ConcurrencyStamp { get; set; }
}
