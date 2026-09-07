namespace Explore.Application.DTOs.Notification;

public sealed record NotificationDto
{
    public Guid Id { get; init; }
    public Guid UserId { get; init; }
    public int NotificationTypeId { get; init; }
    public string? NotificationTypeName { get; init; }
    public required string Title { get; init; }
    public string? Body { get; init; }
    public bool IsRead { get; init; }
    public DateTime? ReadAt { get; init; }
    public int? NotificationEntityTypeId { get; init; }
    public string? NotificationEntityTypeName { get; init; }
    public string? EntityId { get; init; }
    public int NotificationScopeId { get; init; }
    public string? NotificationScopeName { get; init; }
    public Guid? SourceActorId { get; init; }
    public string? SourceActorName { get; init; }
    public Guid? RecipientContextActorId { get; init; }
    public string? RecipientContextActorName { get; init; }
    public int? NotificationReasonId { get; init; }
    public string? NotificationReasonName { get; init; }
    public bool IsArchived { get; init; }
    public DateTime? ArchivedAt { get; init; }
    public DateTime? SnoozedUntil { get; init; }
    public DateTime CreatedAt { get; init; }
    public Guid TenantId { get; init; }
}
