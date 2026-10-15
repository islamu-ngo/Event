using Explore.Domain.Enums;
using ISLAMU.Moderation.Domain;

namespace Event.Domain.UnitTests.Entities;

public sealed class ModerationResourceTargetAdoptionTests
{
    [Test]
    [Arguments(ModerationScopeKind.Tenant)]
    [Arguments(ModerationScopeKind.Resource)]
    public async Task ExactNativePartition_CanBeIndependentlyAdopted(ModerationScopeKind scopeKind)
    {
        var (selection, generation) = Inputs(scopeKind);
        var adoption = ModerationResourceTargetAdoption.Adopt(Guid.CreateVersion7(), selection,
            selection.Bindings[0], generation, Guid.CreateVersion7(), 4,
            selection.RecordedAt, selection.RecordedAt.AddMinutes(5));

        adoption.ValidatePersistedState();
        await Assert.That(adoption.GenerationId).IsEqualTo(generation.Id);
        await Assert.That(adoption.GenerationRevision).IsEqualTo(generation.Revision);
        await Assert.That(adoption.GenerationWriterGeneration).IsEqualTo(generation.WriterGeneration);
        await Assert.That(adoption.BindingOrdinal).IsEqualTo(selection.Bindings[0].Ordinal);
    }

    [Test]
    [Arguments(ModerationScopeKind.Event)]
    [Arguments(ModerationScopeKind.Resource)]
    public async Task RetainedPartition_WithDifferentNativeScopeCannotBeAdopted(ModerationScopeKind scopeKind)
    {
        var (selection, generation) = Inputs(scopeKind, substituteScope: true);

        await Assert.That(() => ModerationResourceTargetAdoption.Adopt(Guid.CreateVersion7(), selection,
            selection.Bindings[0], generation, Guid.CreateVersion7(), 4,
            selection.RecordedAt, selection.RecordedAt.AddMinutes(5))).Throws<ArgumentException>();
    }

    [Test]
    public async Task IndependentAdoption_RevocationIsTerminalAndRetainsAcceptedTarget()
    {
        var (selection, generation) = Inputs();
        var adoption = ModerationResourceTargetAdoption.Adopt(Guid.CreateVersion7(), selection,
            selection.Bindings[0], generation, Guid.CreateVersion7(), 4,
            selection.RecordedAt, selection.RecordedAt.AddMinutes(5));
        var originalGeneration = adoption.GenerationId;
        var revoker = Guid.CreateVersion7();
        adoption.Revoke(1, revoker, selection.RecordedAt.AddMinutes(1));
        adoption.ValidatePersistedState();
        await Assert.That(adoption.Revision).IsEqualTo(2L);
        await Assert.That(adoption.GenerationId).IsEqualTo(originalGeneration);
        await Assert.That(adoption.RevokedByHumanId).IsEqualTo(revoker);
        await Assert.That(() => adoption.Revoke(2, revoker, selection.RecordedAt.AddMinutes(2))).Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task InvalidAdoption_RejectsSelfApprovalExpiryAndUnretainedPartition(int substitution)
    {
        var (selection, generation) = Inputs();
        var binding = substitution == 3 ? ModerationContributionSelectionTests.Selection().Selection.Bindings[0] : selection.Bindings[0];
        await Assert.That(() => ModerationResourceTargetAdoption.Adopt(Guid.CreateVersion7(), selection, binding,
            generation, substitution == 0 ? selection.AuthoredByHumanId : Guid.CreateVersion7(), 4,
            selection.RecordedAt, substitution switch
            {
                1 => selection.RecordedAt,
                2 => selection.ValidUntil.AddMinutes(1),
                _ => selection.RecordedAt.AddMinutes(5)
            })).Throws<ArgumentException>();
    }

    [Test]
    public async Task FailedWithdrawal_PreservesAdoptionUntilExactRevisionIsWithdrawn()
    {
        var (selection, generation) = Inputs();
        var adoption = ModerationResourceTargetAdoption.Adopt(Guid.CreateVersion7(), selection,
            selection.Bindings[0], generation, Guid.CreateVersion7(), 4,
            selection.RecordedAt, selection.RecordedAt.AddMinutes(5));
        var revoker = Guid.CreateVersion7();

        await Assert.That(() => adoption.Revoke(2, revoker, selection.RecordedAt)).Throws<InvalidOperationException>();
        await Assert.That(() => adoption.Revoke(1, Guid.Empty, selection.RecordedAt)).Throws<ArgumentException>();
        await Assert.That(() => adoption.Revoke(1, revoker, selection.RecordedAt.AddTicks(-1))).Throws<ArgumentException>();
        await Assert.That(() => adoption.Revoke(1, revoker, DateTimeOffset.MaxValue)).Throws<ArgumentException>();
        await Assert.That(adoption.Revision).IsEqualTo(1L);
        await Assert.That(adoption.RevokedAt).IsNull();
        await Assert.That(adoption.RevokedByHumanId).IsNull();

        adoption.Revoke(1, revoker, selection.RecordedAt);
        adoption.ValidatePersistedState();
        await Assert.That(adoption.Revision).IsEqualTo(2L);
        await Assert.That(adoption.GenerationId).IsEqualTo(generation.Id);
    }

    [Test]
    public async Task PersistedRevocation_RejectsUnboundedTerminalTime()
    {
        var (selection, generation) = Inputs();
        var adoption = ModerationResourceTargetAdoption.Adopt(Guid.CreateVersion7(), selection,
            selection.Bindings[0], generation, Guid.CreateVersion7(), 4,
            selection.RecordedAt, selection.RecordedAt.AddMinutes(5));
        adoption.Revoke(1, Guid.CreateVersion7(), adoption.AdoptedAt);
        typeof(ModerationResourceTargetAdoption).GetProperty(nameof(adoption.RevokedAt))!
            .SetValue(adoption, DateTimeOffset.MaxValue);

        await Assert.That(adoption.ValidatePersistedState).Throws<ArgumentException>();
    }

    private static (ModerationContributionSelection Selection, ModerationResourceGeneration Generation) Inputs(
        ModerationScopeKind scopeKind = ModerationScopeKind.Tenant, bool substituteScope = false)
    {
        var selection = ModerationContributionSelectionTests.Selection(scopeKind, substituteEvent: substituteScope).Selection;
        return (selection, new()
        {
            Id = Guid.CreateVersion7(), HostBindingId = selection.HostBindingId, TrustDomainId = selection.TrustDomainId,
            TenantId = selection.TenantId, ResourceId = selection.Bindings[0].TargetId, Revision = 1,
            ObservedByHumanId = Guid.CreateVersion7(), WriterGeneration = 3, ObservedAt = selection.RecordedAt,
            ValidUntil = selection.ValidUntil, ResourceStamp = Guid.NewGuid(), EventId = Guid.CreateVersion7(), EventStamp = Guid.NewGuid(),
            MaterialVersionId = Guid.CreateVersion7(),
            ActorId = Guid.CreateVersion7(), ActorStamp = Guid.NewGuid(), OrganizerActorId = null, OrganizerActorStamp = null,
            DeliveryType = EventResourceDeliveryTypeEnum.ExternalLink, StorageObjectId = null, StorageObjectStamp = null,
            StorageProviderBindingId = null, StorageProvider = null
        });
    }
}
