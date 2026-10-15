using ISLAMU.Moderation.Domain;

namespace Event.Domain.UnitTests.Entities;

public sealed class ModerationEventGenerationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task NativeVersion_IsIndependentOfObservationSequenceAndWindow()
    {
        var first = Generation();
        var second = Copy(first, revision: 2, observedAt: Now.AddMinutes(1));
        first.ValidatePersistedState();
        second.ValidatePersistedState();
        await Assert.That(first.HasSameNativeVersion(second)).IsTrue();
        await Assert.That(first.EventStamp.Version).IsEqualTo(4);
        await Assert.That(first.ToString()).IsEqualTo(nameof(ModerationEventGeneration));
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task ChangedNativeAncestry_IsNotTheApprovedVersion(int substitution)
    {
        var first = Generation();
        var second = Copy(first, eventStamp: substitution == 0 ? Guid.NewGuid() : first.EventStamp,
            actorStamp: substitution == 1 ? Guid.NewGuid() : first.ActorStamp,
            submittedBy: substitution == 2 ? Guid.CreateVersion7() : first.SubmittedByHumanId,
            eventId: substitution == 3 ? Guid.CreateVersion7() : first.EventId);
        await Assert.That(first.HasSameNativeVersion(second)).IsFalse();
    }

    [Test]
    public async Task IncompleteOrganizerOrUnboundedWindow_IsNotAnObservation()
    {
        var source = Generation();
        await Assert.That(() => Copy(source, validUntil: Now).ValidatePersistedState()).Throws<ArgumentException>();
        await Assert.That(() => Copy(source, organizerId: Guid.CreateVersion7()).ValidatePersistedState()).Throws<ArgumentException>();
    }

    [Test]
    [Arguments(ModerationSubjectKind.Human, 0, true)]
    [Arguments(ModerationSubjectKind.Human, 1, false)]
    [Arguments(ModerationSubjectKind.Human, 2, false)]
    [Arguments(ModerationSubjectKind.Content, 0, false)]
    [Arguments(ModerationSubjectKind.Content, 1, true)]
    [Arguments(ModerationSubjectKind.Content, 2, false)]
    [Arguments(ModerationSubjectKind.Organization, 1, false)]
    [Arguments(ModerationSubjectKind.Service, 0, false)]
    public async Task ObservedSubject_DistinguishesSubmitterFromEventContent(
        ModerationSubjectKind kind, int identity, bool matches)
    {
        var generation = Generation();
        generation.ValidatePersistedState();
        var subjectId = identity switch
        {
            0 => generation.SubmittedByHumanId!.Value,
            1 => generation.EventId,
            _ => Guid.CreateVersion7()
        };
        await Assert.That(generation.MatchesNativeSubject(kind, subjectId)).IsEqualTo(matches);
    }

    private static ModerationEventGeneration Generation() => new()
    {
        Id = Guid.CreateVersion7(), HostBindingId = Guid.CreateVersion7(), TrustDomainId = Guid.CreateVersion7(),
        TenantId = Guid.CreateVersion7(), EventId = Guid.CreateVersion7(), Revision = 1,
        ObservedByHumanId = Guid.CreateVersion7(), WriterGeneration = 2, ObservedAt = Now, ValidUntil = Now.AddMinutes(10),
        EventStamp = Guid.NewGuid(), ActorId = Guid.CreateVersion7(), ActorStamp = Guid.NewGuid(),
        OrganizerActorId = null, OrganizerActorStamp = null, SubmittedByHumanId = Guid.CreateVersion7()
    };

    private static ModerationEventGeneration Copy(
        ModerationEventGeneration row, long? revision = null, DateTimeOffset? observedAt = null,
        DateTimeOffset? validUntil = null, Guid? eventStamp = null, Guid? actorStamp = null,
        Guid? submittedBy = null, Guid? eventId = null, Guid? organizerId = null) => new()
    {
        Id = Guid.CreateVersion7(), HostBindingId = row.HostBindingId, TrustDomainId = row.TrustDomainId, TenantId = row.TenantId,
        EventId = eventId ?? row.EventId, Revision = revision ?? row.Revision, ObservedByHumanId = row.ObservedByHumanId,
        WriterGeneration = row.WriterGeneration, ObservedAt = observedAt ?? row.ObservedAt, ValidUntil = validUntil ?? row.ValidUntil,
        EventStamp = eventStamp ?? row.EventStamp, ActorId = row.ActorId, ActorStamp = actorStamp ?? row.ActorStamp,
        OrganizerActorId = organizerId ?? row.OrganizerActorId, OrganizerActorStamp = row.OrganizerActorStamp,
        SubmittedByHumanId = submittedBy ?? row.SubmittedByHumanId
    };
}
