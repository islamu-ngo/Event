using ISLAMU.Moderation.Domain;

namespace Event.Domain.UnitTests.Entities;

public sealed class ModerationEventTargetAdoptionTests
{
    [Test]
    public async Task IndependentEventAdoption_RetainsOriginalObservationThroughTerminalRevocation()
    {
        var (selection, generation) = Inputs();
        var adoption = ModerationEventTargetAdoption.Adopt(Guid.CreateVersion7(), selection, selection.Bindings[0],
            generation, Guid.CreateVersion7(), 4, selection.RecordedAt, selection.RecordedAt.AddMinutes(5));
        var revoker = Guid.CreateVersion7();
        adoption.Revoke(1, revoker, selection.RecordedAt.AddMinutes(1));
        adoption.ValidatePersistedState();
        await Assert.That(adoption.Revision).IsEqualTo(2L);
        await Assert.That(adoption.GenerationId).IsEqualTo(generation.Id);
        await Assert.That(adoption.GenerationWriterGeneration).IsEqualTo(generation.WriterGeneration);
        await Assert.That(adoption.RevokedByHumanId).IsEqualTo(revoker);
        await Assert.That(() => adoption.Revoke(2, revoker, selection.RecordedAt.AddMinutes(2))).Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments("stale-revision")]
    [Arguments("empty-human")]
    [Arguments("regressing-time")]
    [Arguments("unbounded-time")]
    public async Task RejectedRevocation_PreservesActiveAdoption(string defect)
    {
        var (selection, generation) = Inputs();
        var adoption = ModerationEventTargetAdoption.Adopt(Guid.CreateVersion7(), selection, selection.Bindings[0],
            generation, Guid.CreateVersion7(), 4, selection.RecordedAt, selection.RecordedAt.AddMinutes(5));
        var humanId = defect == "empty-human" ? Guid.Empty : Guid.CreateVersion7();
        var now = defect switch
        {
            "regressing-time" => adoption.AdoptedAt.AddTicks(-1),
            "unbounded-time" => DateTimeOffset.MaxValue,
            _ => adoption.AdoptedAt
        };
        if (defect == "stale-revision")
            await Assert.That(() => adoption.Revoke(2, humanId, now)).Throws<InvalidOperationException>();
        else
            await Assert.That(() => adoption.Revoke(1, humanId, now)).Throws<ArgumentException>();

        adoption.ValidatePersistedState();
        await Assert.That(adoption.Revision).IsEqualTo(1L);
        await Assert.That(adoption.RevokedAt).IsNull();
        await Assert.That(adoption.RevokedByHumanId).IsNull();
        adoption.Revoke(1, Guid.CreateVersion7(), adoption.AdoptedAt);
        adoption.ValidatePersistedState();
        await Assert.That(adoption.Revision).IsEqualTo(2L);
    }

    [Test]
    public async Task PersistedRevocation_RejectsUnboundedTerminalTime()
    {
        var (selection, generation) = Inputs();
        var adoption = ModerationEventTargetAdoption.Adopt(Guid.CreateVersion7(), selection, selection.Bindings[0],
            generation, Guid.CreateVersion7(), 4, selection.RecordedAt, selection.RecordedAt.AddMinutes(5));
        adoption.Revoke(1, Guid.CreateVersion7(), adoption.AdoptedAt);
        typeof(ModerationEventTargetAdoption).GetProperty(nameof(adoption.RevokedAt))!
            .SetValue(adoption, DateTimeOffset.MaxValue);

        await Assert.That(adoption.ValidatePersistedState).Throws<ArgumentException>();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task EventAdoption_RejectsSelfApprovalExpiredWindowAndUnretainedBinding(int substitution)
    {
        var (selection, generation) = Inputs();
        var binding = substitution == 3 ? ModerationContributionSelectionTests.Selection().Selection.Bindings[0]
            : selection.Bindings[0];
        await Assert.That(() => ModerationEventTargetAdoption.Adopt(Guid.CreateVersion7(), selection, binding, generation,
            substitution == 0 ? selection.AuthoredByHumanId : Guid.CreateVersion7(), 4, selection.RecordedAt,
            substitution switch
            {
                1 => selection.RecordedAt,
                2 => generation.ValidUntil.AddMinutes(1),
                _ => selection.RecordedAt.AddMinutes(5)
            })).Throws<ArgumentException>();
    }

    [Test]
    public async Task EventAdoption_RejectsAnotherHumansNativeSubmission()
    {
        var (selection, generation) = Inputs(substitutedSubmitter: true);
        await Assert.That(() => ModerationEventTargetAdoption.Adopt(Guid.CreateVersion7(),
            selection, selection.Bindings[0], generation, Guid.CreateVersion7(), 4,
            selection.RecordedAt, selection.RecordedAt.AddMinutes(5))).Throws<ArgumentException>();
    }

    [Test]
    [Arguments(ModerationScopeKind.Event, ModerationSubjectKind.Content)]
    [Arguments(ModerationScopeKind.Event, ModerationSubjectKind.Human)]
    [Arguments(ModerationScopeKind.Tenant, ModerationSubjectKind.Human)]
    public async Task EventAdoptionRetainsExactTypedContentOrActualHumanPartition(
        ModerationScopeKind scopeKind, ModerationSubjectKind subjectKind)
    {
        var (selection, generation) = Inputs(scopeKind: scopeKind, subjectKind: subjectKind);
        var binding = selection.Bindings[0];
        var adoption = ModerationEventTargetAdoption.Adopt(Guid.CreateVersion7(), selection, binding,
            generation, Guid.CreateVersion7(), 4, selection.RecordedAt, selection.RecordedAt.AddMinutes(5));
        adoption.ValidatePersistedState();
        await Assert.That(adoption.BindingOrdinal).IsEqualTo(binding.Ordinal);
        await Assert.That(binding.ScopeKind).IsEqualTo(scopeKind);
        await Assert.That(binding.ScopeEventId).IsEqualTo(scopeKind == ModerationScopeKind.Event ? generation.EventId : (Guid?)null);
        await Assert.That(binding.ScopeResourceId).IsNull();
        await Assert.That(binding.Scope.Kind).IsEqualTo(scopeKind);
    }

    [Test]
    [Arguments(ModerationScopeKind.Resource, ModerationSubjectKind.Content, false)]
    [Arguments(ModerationScopeKind.Resource, ModerationSubjectKind.Human, false)]
    [Arguments(ModerationScopeKind.Tenant, ModerationSubjectKind.Content, false)]
    [Arguments(ModerationScopeKind.Event, ModerationSubjectKind.Content, true)]
    [Arguments(ModerationScopeKind.Event, ModerationSubjectKind.Human, true)]
    public async Task EventAdoptionRejectsResourceSubstitutionAndUnqualifiedContentPartition(
        ModerationScopeKind scopeKind, ModerationSubjectKind subjectKind, bool substituteEvent)
    {
        var (selection, generation) = Inputs(scopeKind: scopeKind, subjectKind: subjectKind,
            substituteEvent: substituteEvent);
        await Assert.That(() => ModerationEventTargetAdoption.Adopt(Guid.CreateVersion7(),
            selection, selection.Bindings[0], generation, Guid.CreateVersion7(), 4,
            selection.RecordedAt, selection.RecordedAt.AddMinutes(5))).Throws<ArgumentException>();
    }

    private static (ModerationContributionSelection Selection, ModerationEventGeneration Generation) Inputs(
        bool substitutedSubmitter = false, ModerationScopeKind scopeKind = ModerationScopeKind.Tenant,
        ModerationSubjectKind subjectKind = ModerationSubjectKind.Human, bool substituteEvent = false)
    {
        var selection = ModerationContributionSelectionTests.Selection(scopeKind, subjectKind, substituteEvent).Selection;
        return (selection, new()
        {
            Id = Guid.CreateVersion7(),
            HostBindingId = selection.HostBindingId,
            TrustDomainId = selection.TrustDomainId,
            TenantId = selection.TenantId,
            EventId = selection.Bindings[0].TargetId,
            Revision = 1,
            ObservedByHumanId = Guid.CreateVersion7(),
            WriterGeneration = 3,
            ObservedAt = selection.RecordedAt,
            ValidUntil = selection.ValidUntil,
            EventStamp = Guid.NewGuid(),
            ActorId = Guid.CreateVersion7(),
            ActorStamp = Guid.NewGuid(),
            OrganizerActorId = null,
            OrganizerActorStamp = null,
            SubmittedByHumanId = substitutedSubmitter ? Guid.CreateVersion7() : selection.Bindings[0].SubjectId
        });
    }
}
