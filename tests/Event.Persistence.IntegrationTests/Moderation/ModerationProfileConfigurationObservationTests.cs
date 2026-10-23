using Explore.Application.Contracts.Persistence;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Persistence.Repositories;
using ISLAMU.Moderation.Application;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed class ModerationProfileConfigurationObservationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    [Test]
    [Arguments(1, false, false)]
    [Arguments(2, false, true)]
    [Arguments(2, true, false)]
    public async Task DecisionChangingAcquisition_RequiresNextSelectionAndTheUnchangedAcquiredDecision(
        long selectionDecisionRevision, bool mutateAcquiredDecision, bool accepted)
    {
        var (database, targetSource) = await ModerationResourceTargetAdoptionWriterTests.CreateAsync(
            selectionDecisionRevision: selectionDecisionRevision);
        await using var owner = database;
        await Assert.That((await database.ExecuteAsync(database.UrgencyAssessorId, new AdoptModerationProfileSource
        {
            OperationId = Guid.CreateVersion7(),
            Target = database.Scope,
            ExpectedQualificationRevision = 5,
            AdoptionId = Guid.CreateVersion7(),
            SelectionId = targetSource.SelectionId,
            Mode = ModerationNativeMode.Embedded,
            CaseId = targetSource.CaseId,
            ExpectedCaseRevision = 1,
            ValidUntil = Now.AddMinutes(5)
        })).IsSuccess).IsTrue();
        ModerationDecision original;
        ModerationContributionSelection selection;
        ModerationPolicyPublication publication;
        await using (var reader = database.Context())
        {
            reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
            original = await reader.ModerationDecisions.SingleAsync();
            selection = await reader.ModerationContributionSelections.SingleAsync();
            publication = await reader.ModerationPolicyPublications.SingleAsync();
        }
        var clock = new ObservationClock();
        await using var context = database.Context();
        var privacy = database.Privacy.Authority(context, clock);
        var participant = database.Participant(context, database.SubjectId, clock);
        var operation = ModerationOperation.Transition(ModerationOperationKind.RecordResponse, original.Subject,
            database.Scope, Guid.CreateVersion7(), selection.EffectId, original.Id, 1, 1, Guid.Empty);
        var store = new ModerationStore(context, participant, clock, privacy,
            ModerationLedgerPersistenceTests.Capture(context, privacy, participant, clock));
        ModerationActivationProfileObservation? observed = null;
        var entered = false;
        await store.ExecuteAsync(operation, async (session, token) =>
        {
            entered = true;
            var resolved = await participant.ResolveAsync(session, operation, token);
            await Assert.That(resolved.Outcome).IsEqualTo(ModerationOperationOutcome.Known);
            var decision = (await session.FindDecisionAsync(original.Id, original.Subject, database.Scope, token))!;
            if (mutateAcquiredDecision) decision.RecordResponse(1, Now);
            observed = await participant.AcquireProfileConfigurationAsync(
                context.Database.CurrentTransaction!.TransactionId, publication, decision, resolved.Grant!, Now, token);
            return ModerationOperationResult.Unavailable();
        }, CancellationToken.None);
        await Assert.That(entered).IsTrue();
        await Assert.That(observed is not null).IsEqualTo(accepted);
        if (observed is not null)
            await Assert.That(observed.DecisionRevision).IsEqualTo(1L);
        await using var unchanged = database.Context();
        unchanged.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That((await unchanged.ModerationDecisions.SingleAsync()).Revision).IsEqualTo(1L);
        await Assert.That(await unchanged.ModerationSanctions.CountAsync()).IsEqualTo(0);
        await Assert.That(await unchanged.ModerationEffectIntents.CountAsync()).IsEqualTo(0);
        await Assert.That(await unchanged.ModerationOperationReceipts.CountAsync()).IsEqualTo(0);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    [Arguments(6)]
    [Arguments(7)]
    [Arguments(8)]
    [Arguments(9)]
    public async Task CurrentProfileConfiguration_RequiresOriginalAcceptanceAndTheActualLifecycleAcquisition(int change)
    {
        var (database, targetSource) = await ModerationResourceTargetAdoptionWriterTests.CreateAsync();
        await using var owner = database;
        var adoptionId = Guid.CreateVersion7();
        await Assert.That((await database.ExecuteAsync(database.UrgencyAssessorId, new AdoptModerationProfileSource
        {
            OperationId = Guid.CreateVersion7(),
            Target = database.Scope,
            ExpectedQualificationRevision = 5,
            AdoptionId = adoptionId,
            SelectionId = targetSource.SelectionId,
            Mode = ModerationNativeMode.Embedded,
            CaseId = targetSource.CaseId,
            ExpectedCaseRevision = 1,
            ValidUntil = Now.AddMinutes(5)
        })).IsSuccess).IsTrue();
        ModerationContributionSelection selection;
        ModerationDecision original;
        ModerationPolicyPublication publication;
        await using (var reader = database.Context())
        {
            reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
            selection = await reader.ModerationContributionSelections.SingleAsync();
            original = await reader.ModerationDecisions.SingleAsync();
            publication = await reader.ModerationPolicyPublications.SingleAsync();
        }
        if (change == 1)
            await Assert.That((await database.ExecuteAsync(database.AdministratorId, new RevokeModerationProfileSource
            {
                OperationId = Guid.CreateVersion7(),
                Target = database.Scope,
                ExpectedQualificationRevision = 6,
                AdoptionId = adoptionId,
                ExpectedAdoptionRevision = 1,
                CaseId = original.CaseId,
                ExpectedCaseRevision = 1
            })).IsSuccess).IsTrue();
        if (change is 2 or 3 or 4 or 9)
        {
            await using var writer = database.Context();
            writer.ModerationTrustDomainId = database.Scope.TrustDomainId;
            if (change is 2 or 9)
                writer.PlatformUserRoles.Remove(await writer.PlatformUserRoles.SingleAsync(row =>
                    row.UserId == (change == 2 ? database.UrgencyAssessorId : database.AdministratorId)));
            else
                writer.ModerationQualificationMutationReceipts.Remove(await writer.ModerationQualificationMutationReceipts
                    .SingleAsync(row => row.SourceKind == (change == 3
                        ? ModerationQualificationSourceKind.ProfileAdoption : ModerationQualificationSourceKind.ContributionSelection)));
            await writer.SaveChangesAsync();
        }
        var clock = new ObservationClock();
        await using var context = database.Context();
        var privacyAuthority = database.Privacy.Authority(context, clock);
        var participant = database.Participant(context, database.SubjectId, clock);
        var operation = ModerationOperation.Transition(ModerationOperationKind.RecordResponse, original.Subject,
            database.Scope, Guid.CreateVersion7(), selection.EffectId, original.Id, 1, 1, Guid.Empty);
        var store = new ModerationStore(context, participant, clock, privacyAuthority, ModerationLedgerPersistenceTests.Capture(context, privacyAuthority, participant, clock));
        ModerationActivationProfileObservation? observed = null;
        await store.ExecuteAsync(operation, async (session, token) =>
        {
            var resolved = await participant.ResolveAsync(session, operation, token);
            await Assert.That(resolved.Outcome).IsEqualTo(ModerationOperationOutcome.Known);
            var decision = (await session.FindDecisionAsync(original.Id, original.Subject, database.Scope, token))!;
            if (change == 5) clock.UtcNow = Now.AddMinutes(5);
            observed = await participant.AcquireProfileConfigurationAsync(
                change == 6 ? Guid.CreateVersion7() : context.Database.CurrentTransaction!.TransactionId,
                publication, change == 8 ? original : decision,
                change == 7 ? resolved.Grant! with { } : resolved.Grant!, clock.UtcNow, token);
            return ModerationOperationResult.Unavailable();
        }, CancellationToken.None);
        if (change == 0)
        {
            await Assert.That(observed).IsNotNull();
            await Assert.That(observed!.SourceId).IsEqualTo(adoptionId);
            await Assert.That(observed.SourceRevision).IsEqualTo(1L);
            await Assert.That(observed.PublicationId).IsEqualTo(publication.Id);
            await Assert.That(observed.ProfileId).IsEqualTo(selection.ProfileId);
            await Assert.That(observed.DecisionRevision).IsEqualTo(original.Revision);
            await Assert.That(observed.Mode).IsEqualTo(ModerationNativeMode.Embedded);
            await Assert.That(observed.ValidUntil).IsEqualTo(Now.AddMinutes(5));
            await Assert.That(observed.RequiredPrerequisites.Contains(ModerationActivationPrerequisite.ConstitutionAndReviewers)).IsTrue();
            await Assert.That(observed.RequiredPrerequisites.Contains(ModerationActivationPrerequisite.MeasuredOperations)).IsTrue();
            await Assert.That(observed.Contribution.Id).IsEqualTo(original.Id);
        }
        else await Assert.That(observed).IsNull();
        await using var unchanged = database.Context();
        unchanged.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That((await unchanged.ModerationDecisions.SingleAsync()).Revision).IsEqualTo(1L);
        await Assert.That(await unchanged.ModerationEffectIntents.CountAsync()).IsEqualTo(0);
    }

    private sealed class ObservationClock : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = Now;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
