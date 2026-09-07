using Explore.Domain;

namespace Explore.Application.Notifications;

public sealed record NotificationFanoutOccurrenceCandidate(
    Guid OccurrenceId,
    Guid PointerOutboxMessageId,
    Guid TenantId,
    Guid EventId,
    Guid? SessionId,
    DateTime OccurredAt,
    DateTime AudienceCutoffAt,
    Guid AggregateVersion,
    string ChangeSetJson,
    string SafeBeforeSnapshotJson,
    string SafeAfterSnapshotJson,
    string TemplateKey,
    int TemplateVersion,
    int DeliveryPolicyId,
    int PolicyVersion,
    DateTime RequestedNotBefore,
    string SourceType,
    Guid SourceId);

public enum NotificationFanoutOccurrenceCoordinationOutcome
{
    NewlyActive = 1,
    Superseded = 2,
    SourceReplay = 3
}

public sealed record NotificationFanoutOccurrenceCoordinationResult(
    NotificationFanoutOccurrenceCoordinationOutcome Outcome,
    NotificationFanoutOccurrence Occurrence,
    Guid ActiveOccurrenceId,
    bool PointerCreated);

public enum NotificationFanoutOccurrenceKind
{
    Reminder = 1,
    ImportantUpdate = 2,
    SessionCancellation = 3,
    EventCancellation = 4,
    HeavyModerationUnavailable = 5
}

public static class NotificationFanoutOccurrenceCoordinationPolicy
{
    public static readonly TimeSpan ImportantUpdateWindow = TimeSpan.FromMinutes(5);

    public const string HeavyModerationUnavailableTemplateKey = "event.moderation.unavailable";
    public const string EventReminderTemplateKey = "event.reminder";
    public const string SessionReminderTemplateKey = "event.session.reminder";

    public const int ReminderPriority = 10;
    public const int ImportantUpdatePriority = 30;
    public const int SessionCancellationPriority = 50;
    public const int EventCancellationPriority = 70;
    public const int HeavyModerationUnavailablePriority = 100;
}
