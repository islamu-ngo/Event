using Explore.Domain;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationNoticeReadTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Adjudicated_grant_settlement_advances_once_per_mutation_and_never_for_exact_replay(bool colocatedPrivacy)
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        var original = await SeedCreatingNoticeLedgerAsync(fixture);
        var request = AdjudicatedGrantRequest(fixture, original);
        await using var primary = fixture.Database.Context();
        await using var colocated = colocatedPrivacy ? ModerationNoticePrivacyFixture.ReuseColocated(primary) : null;
        await using var writer = fixture.Database.Context();
        var repository = AdjudicatedGrants(writer, fixture, fixture.Database.AdministratorId, colocated);
        var before = await RecoveryAsync();
        await Assert.That(await repository.GrantAsync(request)).IsNotNull();
        var issued = await RecoveryAsync();
        await Assert.That(issued).IsEqualTo((before.Id, before.Revision + 1, before.Generation, before.Position + 1));
        await Assert.That(await repository.GrantAsync(request)).IsNotNull();
        await Assert.That(await RecoveryAsync()).IsEqualTo(issued);
        var revocation = Guid.CreateVersion7();
        await Assert.That(await repository.RevokeAsync(request.OperationId, revocation, 1)).IsNotNull();
        var revoked = await RecoveryAsync();
        await Assert.That(revoked).IsEqualTo((issued.Id, issued.Revision + 1, issued.Generation, issued.Position + 1));
        await Assert.That(await repository.RevokeAsync(request.OperationId, revocation, 1)).IsNotNull();
        await Assert.That(await RecoveryAsync()).IsEqualTo(revoked);
        await AssertOriginalAdjudicatedLedgerUnchangedAsync(fixture, original);

        async Task<(Guid Id, long Revision, long Generation, long Position)> RecoveryAsync()
        {
            await using var inspect = fixture.Database.Context();
            var state = await inspect.Set<ModerationRecoveryStoreState>().AsNoTracking().SingleAsync();
            return (state.Id, state.Revision, state.Generation, state.Position);
        }
    }

    [Test]
    [Arguments(false, "recipient")]
    [Arguments(false, "expiry")]
    [Arguments(true, "recipient")]
    [Arguments(true, "expiry")]
    [Arguments(false, "extra-first-save")]
    [Arguments(true, "extra-first-save")]
    [Arguments(false, "extra-second-save")]
    [Arguments(true, "extra-second-save")]
    [Arguments(false, "qualification-revision")]
    [Arguments(true, "qualification-revision")]
    [Arguments(false, "qualification-replacement")]
    [Arguments(true, "qualification-replacement")]
    public async Task Adjudicated_grant_settlement_rejects_substitution_and_unowned_grants(
        bool colocatedPrivacy, string defect)
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        var original = await SeedCreatingNoticeLedgerAsync(fixture);
        var request = AdjudicatedGrantRequest(fixture, original);
        await using var primary = fixture.Database.Context();
        var qualificationBefore = await primary.ModerationQualificationEpochs.AsNoTracking().SingleAsync();
        await using var colocated = colocatedPrivacy ? ModerationNoticePrivacyFixture.ReuseColocated(primary) : null;
        var extraId = Guid.CreateVersion7();
        var probe = new SubstituteAdjudicatedGrantAtSave(request.OperationId, extraId, fixture.Database.SubjectId, defect);
        await using (var writer = fixture.Database.Context(probe))
        {
            await Assert.That(await AdjudicatedGrants(writer, fixture, fixture.Database.AdministratorId, colocated)
                .GrantAsync(request)).IsNull();
            await Assert.That(probe.Observed).IsTrue();
            await Assert.That(writer.Database.CurrentTransaction).IsNull();
            await Assert.That(writer.ChangeTracker.HasChanges()).IsFalse();
        }
        await using var inspect = fixture.Database.Context();
        inspect.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        await Assert.That(await inspect.Set<ModerationAdjudicatedCaseReadGrant>()
            .AnyAsync(row => row.Id == request.OperationId || row.Id == extraId)).IsFalse();
        var qualificationAfter = await inspect.ModerationQualificationEpochs.AsNoTracking().SingleAsync();
        await Assert.That((qualificationAfter.Id, qualificationAfter.HostBindingId, qualificationAfter.TenantId, qualificationAfter.Revision))
            .IsEqualTo((qualificationBefore.Id, qualificationBefore.HostBindingId, qualificationBefore.TenantId, qualificationBefore.Revision));
        await AssertOriginalAdjudicatedLedgerUnchangedAsync(fixture, original);
    }

    private sealed class SubstituteAdjudicatedGrantAtSave(
        Guid grantId, Guid extraId, Guid replacementRecipient, string defect) : SaveChangesInterceptor
    {
        private int _saves;
        public bool Observed { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context is not ExploreDbContext context) return ValueTask.FromResult(result);
            var entry = context.ChangeTracker.Entries<ModerationAdjudicatedCaseReadGrant>()
                .SingleOrDefault(row => row.Entity.Id == grantId);
            if (entry is null) return ValueTask.FromResult(result);
            _saves++;
            if (!Observed && (defect is "recipient" or "expiry") && entry.State == EntityState.Added)
            {
                Observed = true;
                if (defect == "recipient") entry.Property(row => row.RecipientUserId).CurrentValue = replacementRecipient;
                else entry.Property(row => row.ExpiresAt).CurrentValue = entry.Entity.ExpiresAt.AddMicroseconds(1);
            }
            else if (!Observed && defect == "extra-second-save" && _saves == 2)
                AddExtra(context, entry.Entity);
            else if (!Observed && (defect is "qualification-revision" or "qualification-replacement")
                && entry.State == EntityState.Added)
            {
                Observed = true;
                var epoch = context.ChangeTracker.Entries<ModerationQualificationEpoch>().Single();
                if (defect == "qualification-revision") epoch.Entity.Advance(epoch.Entity.Revision);
                else
                {
                    var replacement = (ModerationQualificationEpoch)epoch.CurrentValues.ToObject();
                    epoch.State = EntityState.Detached;
                    context.Attach(replacement);
                }
            }
            return ValueTask.FromResult(result);
        }

        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            if (!Observed && defect == "extra-first-save" && eventData.Context is ExploreDbContext context
                && context.ChangeTracker.Entries<ModerationAdjudicatedCaseReadGrant>()
                    .SingleOrDefault(row => row.Entity.Id == grantId) is { } entry)
                AddExtra(context, entry.Entity);
            return ValueTask.FromResult(result);
        }

        private void AddExtra(ExploreDbContext context, ModerationAdjudicatedCaseReadGrant original)
        {
            Observed = true;
            context.Add(ModerationAdjudicatedCaseReadGrant.Grant(extraId, original.HostBindingId,
                original.TrustDomainId, original.TenantId, original.CaseId, original.CaseRevision,
                replacementRecipient, original.IssuerUserId, original.IssuerRoleGrantId, original.IssuerPlatformGrant,
                original.OriginalEffectId, original.OriginalSnapshotHash, original.OriginalEffectBindingHash,
                original.OriginalRetainUntil, original.IssuedAt, original.ExpiresAt));
        }
    }
}
