using System.Collections.Immutable;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Repositories;
using ISLAMU.Moderation.Application;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed class ModerationEventTargetObservationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

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
    [Arguments(10)]
    [Arguments(11)]
    public async Task FinalEventObservation_RequiresAcceptedIndependentAdoptionAndCurrentNativeLineage(int substitution)
    {
        var (database, source) = await ModerationEventTargetAdoptionWriterTests.CreateAsync();
        await using var owner = database;
        await Assert.That((await database.ExecuteAsync(database.UrgencyAssessorId, source)).IsSuccess).IsTrue();
        ModerationContributionSelection selection;
        ModerationDecision original;
        ModerationPolicyPublication publication;
        ModerationEventGeneration generation;
        await using (var reader = database.Context())
        {
            reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
            selection = await reader.ModerationContributionSelections.SingleAsync();
            original = await reader.ModerationDecisions.SingleAsync();
            publication = await reader.ModerationPolicyPublications.SingleAsync();
            generation = await reader.ModerationEventGenerations.SingleAsync();
        }
        await Assert.That((await database.ExecuteAsync(database.ReviewerId, new ApproveModerationNoticeDisclosureSource
        {
            OperationId = Guid.CreateVersion7(), Target = database.Scope, ExpectedQualificationRevision = 6,
            Intent = new(Guid.CreateVersion7(), selection.EffectId, original.Id, 2, original.CaseId, 1,
                selection.PolicyId, 1, publication.Id, selection.ProfileId, original.Id, selection.SelectionDigest,
                new("Native Event source classification."), new("Current source authority and protected response."),
                ImmutableArray.Create(new ModerationNoticeSafeText("Explicitly approved native Event explanation.")),
                new("Protected response remains available."), new("Request independent review."),
                new("No automated finding."), new("Purpose-bound retention."), Now.AddMinutes(10), Now.AddYears(1))
        })).IsSuccess).IsTrue();
        if (substitution is 1 or 2 or 3 or 4 or 5 or 10 or 11)
        {
            await using var writer = database.Context();
            writer.ModerationTrustDomainId = database.Scope.TrustDomainId;
            if (substitution == 1)
                (await writer.ModerationEventTargetAdoptions.SingleAsync()).Revoke(1, database.AdministratorId, Now);
            else if (substitution == 2)
                writer.PlatformUserRoles.Remove(await writer.PlatformUserRoles.SingleAsync(
                    row => row.UserId == database.UrgencyAssessorId));
            else if (substitution == 3)
                await writer.Events.Where(row => row.Id == generation.EventId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.ConcurrencyStamp, Guid.NewGuid()));
            else if (substitution == 10)
                await writer.Events.Where(row => row.Id == generation.EventId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.SubmittedByUserId, database.ReviewerId));
            else if (substitution == 11)
                await writer.Events.Where(row => row.Id == generation.EventId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.OrganizerActorId, generation.ActorId));
            else
            {
                var kind = substitution == 4 ? ModerationQualificationSourceKind.EventTargetAdoption
                    : ModerationQualificationSourceKind.EventGeneration;
                writer.ModerationQualificationMutationReceipts.Remove(
                    await writer.ModerationQualificationMutationReceipts.SingleAsync(row => row.SourceKind == kind));
            }
            await writer.SaveChangesAsync();
        }
        if (substitution == 6)
            await Assert.That((await database.ExecuteAsync(database.AdministratorId, new RecordModerationEventGenerationSource
            {
                OperationId = Guid.CreateVersion7(), SourceId = Guid.CreateVersion7(), Target = database.Scope,
                ExpectedQualificationRevision = 7, EventId = generation.EventId, ExpectedEventRevision = 1,
                ValidUntil = Now.AddMinutes(10)
            })).IsSuccess).IsTrue();
        var clock = new ObservationClock();
        await using var context = database.Context();
        var privacyAuthority = database.Privacy.Authority(context, clock);
        var participant = database.Participant(context, database.SubjectId, clock);
        var operation = ModerationOperation.Transition(ModerationOperationKind.RecordResponse,
            original.Subject, database.Scope, Guid.CreateVersion7(), selection.EffectId, original.Id, 1, 1, Guid.Empty);
        var store = new ModerationStore(context, participant, clock, privacyAuthority, ModerationLedgerPersistenceTests.Capture(context, privacyAuthority, participant, clock));
        ModerationNoticeTargetObservation? observed = null;
        await store.ExecuteAsync(operation, async (session, token) =>
        {
            var resolved = await participant.ResolveAsync(session, operation, token);
            await Assert.That(resolved.Outcome).IsEqualTo(ModerationOperationOutcome.Known);
            var decision = (await session.FindDecisionAsync(original.Id, original.Subject, database.Scope, token))!;
            decision.RecordResponse(1, Now);
            var effect = ModerationEffectIntent.Create(operation.EffectId, operation.OperationId, decision.Id, decision.Revision,
                null, null, database.Scope.TrustDomainId, (int)database.Scope.Kind, database.Scope.Id,
                (int)decision.Subject.Kind, decision.Subject.Id, (int)operation.Kind, 1,
                resolved.Grant!.QualificationRevision, Now, null, null);
            context.ModerationEffectIntents.Add(effect);
            if (substitution == 7) clock.UtcNow = Now.AddMinutes(5);
            observed = await participant.AcquireEventTargetObservationAsync(
                substitution == 8 ? Guid.CreateVersion7() : context.Database.CurrentTransaction!.TransactionId,
                publication, effect, decision, substitution == 9 ? resolved.Grant with { } : resolved.Grant, 0,
                clock.UtcNow, token);
            return ModerationOperationResult.Unavailable();
        }, CancellationToken.None);
        if (substitution == 0)
        {
            await Assert.That(observed).IsNotNull();
            await Assert.That(observed!.ApprovedSource).IsEqualTo(observed.CurrentSource);
            await Assert.That(observed.Target.TargetId).IsEqualTo(generation.EventId);
            await Assert.That(observed.Target.TargetRevision).IsEqualTo(generation.Revision);
            await Assert.That(observed.ValidUntil).IsEqualTo(Now.AddMinutes(5));
        }
        else await Assert.That(observed).IsNull();
        await using var unchanged = database.Context();
        unchanged.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That((await unchanged.ModerationDecisions.SingleAsync()).Revision).IsEqualTo(1L);
        await Assert.That(await unchanged.ModerationEffectIntents.CountAsync()).IsEqualTo(0);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task CompleteEventApproval_RequiresAllOrdinalsAndUsesTheEarliestPartitionDeadline(
        bool adoptEveryOrdinal, bool revokeConsequence)
    {
        var (database, source) = await ModerationEventTargetAdoptionWriterTests.CreateAsync();
        await using var owner = database;
        await Assert.That((await database.ExecuteAsync(database.UrgencyAssessorId, source)).IsSuccess).IsTrue();
        ModerationContributionSelection selection;
        ModerationDecision original;
        ModerationPolicyPublication publication;
        await using (var reader = database.Context())
        {
            reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
            selection = await reader.ModerationContributionSelections.Include(row => row.Bindings).SingleAsync();
            original = await reader.ModerationDecisions.SingleAsync();
            publication = await reader.ModerationPolicyPublications.SingleAsync();
        }
        await Assert.That(selection.Bindings.Count).IsGreaterThan(1);
        var epoch = 6L;
        var otherAdoptions = new List<Guid>();
        if (adoptEveryOrdinal)
        {
            foreach (var binding in selection.Bindings.Where(row => row.Ordinal != 0).OrderBy(row => row.Ordinal))
            {
                var id = Guid.CreateVersion7();
                await Assert.That((await database.ExecuteAsync(database.UrgencyAssessorId, source with
                {
                    OperationId = Guid.CreateVersion7(), AdoptionId = id, BindingOrdinal = binding.Ordinal,
                    ExpectedQualificationRevision = epoch, ValidUntil = Now.AddMinutes(4)
                })).IsSuccess).IsTrue();
                otherAdoptions.Add(id);
                epoch++;
            }
        }
        await Assert.That((await database.ExecuteAsync(database.ReviewerId, new ApproveModerationNoticeDisclosureSource
        {
            OperationId = Guid.CreateVersion7(), Target = database.Scope, ExpectedQualificationRevision = epoch,
            Intent = new(Guid.CreateVersion7(), selection.EffectId, original.Id, 2, original.CaseId, 1,
                selection.PolicyId, 1, publication.Id, selection.ProfileId, original.Id, selection.SelectionDigest,
                new("Native Event source classification."), new("Current source authority and protected response."),
                ImmutableArray.Create(new ModerationNoticeSafeText("Approved complete native Event explanation.")),
                new("Protected response remains available."), new("Request independent review."),
                new("No automated finding."), new("Purpose-bound retention."), Now.AddMinutes(10), Now.AddYears(1))
        })).IsSuccess).IsTrue();
        if (revokeConsequence)
        {
            await using var writer = database.Context();
            writer.ModerationTrustDomainId = database.Scope.TrustDomainId;
            var last = otherAdoptions[^1];
            (await writer.ModerationEventTargetAdoptions.SingleAsync(row => row.Id == last))
                .Revoke(1, database.AdministratorId, Now);
            await writer.SaveChangesAsync();
        }
        var clock = new ObservationClock();
        await using var context = database.Context();
        var privacyAuthority = database.Privacy.Authority(context, clock);
        var participant = database.Participant(context, database.SubjectId, clock);
        var operation = ModerationOperation.Transition(ModerationOperationKind.RecordResponse,
            original.Subject, database.Scope, Guid.CreateVersion7(), selection.EffectId, original.Id, 1, 1, Guid.Empty);
        var store = new ModerationStore(context, participant, clock, privacyAuthority, ModerationLedgerPersistenceTests.Capture(context, privacyAuthority, participant, clock));
        ModerationPolicyPublicationNoticeApproval? approval = null;
        await store.ExecuteAsync(operation, async (session, token) =>
        {
            var resolved = await participant.ResolveAsync(session, operation, token);
            await Assert.That(resolved.Outcome).IsEqualTo(ModerationOperationOutcome.Known);
            var decision = (await session.FindDecisionAsync(original.Id, original.Subject, database.Scope, token))!;
            decision.RecordResponse(1, Now);
            var effect = ModerationEffectIntent.Create(operation.EffectId, operation.OperationId, decision.Id, decision.Revision,
                null, null, database.Scope.TrustDomainId, (int)database.Scope.Kind, database.Scope.Id,
                (int)decision.Subject.Kind, decision.Subject.Id, (int)operation.Kind, 1,
                resolved.Grant!.QualificationRevision, Now, null, null);
            context.ModerationEffectIntents.Add(effect);
            approval = await participant.AcquireEventNoticeApprovalAsync(
                context.Database.CurrentTransaction!.TransactionId, publication, effect, decision, resolved.Grant, Now, token);
            return ModerationOperationResult.Unavailable();
        }, CancellationToken.None);
        if (adoptEveryOrdinal && !revokeConsequence)
        {
            await Assert.That(approval).IsNotNull();
            await Assert.That(approval!.Targets.Length).IsEqualTo(1);
            await Assert.That(approval.Targets[0].ApprovedSource).IsEqualTo(approval.Targets[0].CurrentSource);
            await Assert.That(approval.ValidUntil).IsEqualTo(Now.AddMinutes(4));
            await Assert.That(approval.Targets[0].ValidUntil).IsEqualTo(approval.ValidUntil);
            await Assert.That(approval.Contribution.Id).IsEqualTo(original.Id);
        }
        else await Assert.That(approval).IsNull();
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
