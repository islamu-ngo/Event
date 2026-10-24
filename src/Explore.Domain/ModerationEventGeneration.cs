using ISLAMU.Moderation.Domain;

namespace Explore.Domain;

/// <summary>
/// Immutable payload-free observation of native Event and publisher/organizer lineage.
/// The observation sequence never replaces the original GUID concurrency stamps.
/// </summary>
public sealed class ModerationEventGeneration
{
    public required Guid Id { get; init; }
    public required Guid HostBindingId { get; init; }
    public required Guid TrustDomainId { get; init; }
    public required Guid TenantId { get; init; }
    public required Guid EventId { get; init; }
    public required long Revision { get; init; }
    public required Guid ObservedByHumanId { get; init; }
    public required long WriterGeneration { get; init; }
    public required DateTimeOffset ObservedAt { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }
    public required Guid EventStamp { get; init; }
    public required Guid ActorId { get; init; }
    public required Guid ActorStamp { get; init; }
    public required Guid? OrganizerActorId { get; init; }
    public required Guid? OrganizerActorStamp { get; init; }
    public required Guid? SubmittedByHumanId { get; init; }

    public void ValidatePersistedState()
    {
        foreach (var id in new[] { Id, HostBindingId, TrustDomainId, TenantId })
            ModerationHostBinding.RequireUuidV7(id, nameof(Id));
        if (EventId == Guid.Empty || EventStamp == Guid.Empty || ActorId == Guid.Empty || ActorStamp == Guid.Empty
            || ObservedByHumanId == Guid.Empty || SubmittedByHumanId == Guid.Empty
            || OrganizerActorId == Guid.Empty || OrganizerActorStamp == Guid.Empty
            || OrganizerActorId.HasValue != OrganizerActorStamp.HasValue
            || Revision is < 1 or long.MaxValue || WriterGeneration is < 1 or long.MaxValue
            || ObservedAt.Offset != TimeSpan.Zero || ObservedAt == DateTimeOffset.MinValue
            || ValidUntil.Offset != TimeSpan.Zero || ValidUntil == DateTimeOffset.MaxValue || ValidUntil <= ObservedAt)
            throw new ArgumentException("An event generation requires exact native ancestry and a finite observation window.");
    }

    public bool HasSameNativeVersion(ModerationEventGeneration current) =>
        HostBindingId == current.HostBindingId && TrustDomainId == current.TrustDomainId && TenantId == current.TenantId
        && EventId == current.EventId && EventStamp == current.EventStamp && ActorId == current.ActorId
        && ActorStamp == current.ActorStamp && OrganizerActorId == current.OrganizerActorId
        && OrganizerActorStamp == current.OrganizerActorStamp && SubmittedByHumanId == current.SubmittedByHumanId;

    /// <summary>
    /// Binds this Event observation to either its actual human submitter or the Event's
    /// content subject. This identity check does not qualify material, policy or disclosure.
    /// </summary>
    public bool MatchesNativeSubject(ModerationSubjectKind kind, Guid subjectId) =>
        subjectId != Guid.Empty && kind switch
        {
            ModerationSubjectKind.Human => SubmittedByHumanId == subjectId,
            ModerationSubjectKind.Content => EventId == subjectId,
            _ => false
        };

    public override string ToString() => nameof(ModerationEventGeneration);
}
