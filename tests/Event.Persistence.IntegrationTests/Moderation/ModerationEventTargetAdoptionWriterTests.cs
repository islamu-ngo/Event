using System.Collections.Immutable;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Policy;
using Explore.Domain;
using Explore.Domain.Enums;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using SourceDatabase = Event.Persistence.IntegrationTests.Moderation.ModerationQualificationWriterTests.SourceDatabase;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed class ModerationEventTargetAdoptionWriterTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task IndependentEventAdoption_ExactRetryAndRevocationSurviveNativeEventDeletion(bool deletePublisher)
    {
        var (database, source) = await CreateAsync();
        await using var owner = database;
        var accepted = await database.ExecuteAsync(database.UrgencyAssessorId, source);
        await Assert.That(accepted.IsSuccess).IsTrue();
        var replay = await database.ExecuteAsync(database.UrgencyAssessorId, source);
        await Assert.That(replay.Id).IsEqualTo(accepted.Id);
        await Assert.That(replay.QualificationRevision).IsEqualTo(accepted.QualificationRevision);
        await using (var writer = database.Context())
        {
            writer.ModerationTrustDomainId = database.Scope.TrustDomainId;
            var eventId = await writer.ModerationEventGenerations.Select(row => row.EventId).SingleAsync();
            await writer.Events.Where(row => row.Id == eventId)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsDeleted, true));
            if (deletePublisher)
            {
                var publisher = await writer.ModerationEventGenerations.Select(row => row.ActorId).SingleAsync();
                await writer.Actors.Where(row => row.Id == publisher)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsDeleted, true));
            }
        }
        var retainedReplay = await database.ExecuteAsync(database.UrgencyAssessorId, source);
        await Assert.That(retainedReplay.IsSuccess).IsTrue();
        await Assert.That(retainedReplay.Id).IsEqualTo(accepted.Id);
        await Assert.That(retainedReplay.QualificationRevision).IsEqualTo(accepted.QualificationRevision);
        var revoke = new RevokeModerationEventTargetSource
        {
            OperationId = Guid.CreateVersion7(), Target = database.Scope, ExpectedQualificationRevision = 6,
            AdoptionId = source.AdoptionId, ExpectedAdoptionRevision = 1, CaseId = source.CaseId, ExpectedCaseRevision = 1
        };
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, revoke)).IsSuccess).IsTrue();
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, revoke)).IsSuccess).IsTrue();
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        var adoption = await reader.ModerationEventTargetAdoptions.SingleAsync();
        var generation = await reader.ModerationEventGenerations.SingleAsync();
        await Assert.That(adoption.Revision).IsEqualTo(2L);
        await Assert.That(adoption.GenerationId).IsEqualTo(source.GenerationId);
        await Assert.That(adoption.GenerationWriterGeneration).IsEqualTo(generation.WriterGeneration);
        await Assert.That(adoption.AdoptedByHumanId).IsEqualTo(database.UrgencyAssessorId);
        await Assert.That(adoption.RevokedByHumanId).IsEqualTo(database.AdministratorId);
        await Assert.That(await reader.ModerationEffectIntents.CountAsync()).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Adoption_fences_retained_parents_before_tenant_and_cannot_acquire_replacement_actors(bool organizer)
    {
        var (database, source) = await CreateAsync();
        await using var owner = database;
        var replacement = new Actor
        {
            Id = Guid.CreateVersion7(), ActorTypeId = (int)ActorTypeEnum.User, ActorType = null!,
            UserId = database.ReviewerId, Pii = new ActorPii { DisplayName = "Replacement publisher" }
        };
        Guid eventId;
        Guid publisher;
        await using (var seed = database.Context())
        {
            seed.ModerationTrustDomainId = database.Scope.TrustDomainId;
            var generation = await seed.ModerationEventGenerations.SingleAsync(row => row.Id == source.GenerationId);
            eventId = generation.EventId;
            publisher = generation.ActorId;
            seed.Actors.Add(replacement);
            await seed.SaveChangesAsync();
        }
        var change = new ModerationEventGenerationWriterTests.ReplaceOwnerBeforeTenant(eventId, replacement.Id, organizer);
        var result = await database.ExecuteAsync(database.UrgencyAssessorId, source, interceptor: change);
        await Assert.That(change.Observed).IsTrue();
        await Assert.That(change.ActorAnchorsBeforeTenant.Contains(publisher)).IsTrue();
        await Assert.That(change.ActorAnchors.Contains(replacement.Id)).IsFalse();
        await Assert.That(result.IsSuccess).IsFalse();
        await using var current = database.Context();
        current.ModerationTrustDomainId = database.Scope.TrustDomainId;
        var actual = await current.Events.SingleAsync(row => row.Id == eventId);
        await Assert.That(actual.ActorId).IsEqualTo(publisher);
        await Assert.That(actual.OrganizerActorId).IsNull();
        await Assert.That(await current.ModerationEventTargetAdoptions.AnyAsync()).IsFalse();
        await Assert.That(await current.ModerationQualificationMutationReceipts.AnyAsync(row => row.Id == source.OperationId)).IsFalse();
        await Assert.That((await current.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(5L);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    [Arguments(6)]
    public async Task UnqualifiedEventAdoption_DoesNotCommitAcceptedPrefix(int substitution)
    {
        var (database, source) = await CreateAsync();
        await using var owner = database;
        if (substitution is 1 or 2 or 3 or 6)
        {
            await using var writer = database.Context();
            writer.ModerationTrustDomainId = database.Scope.TrustDomainId;
            var generation = await writer.ModerationEventGenerations.SingleAsync();
            if (substitution == 1)
                await writer.Events.Where(row => row.Id == generation.EventId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.ConcurrencyStamp, Guid.NewGuid()));
            else if (substitution == 6)
                await writer.Events.Where(row => row.Id == generation.EventId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.SubmittedByUserId, database.ReviewerId));
            else
            {
                var kind = substitution == 2 ? ModerationQualificationSourceKind.EventGeneration
                    : ModerationQualificationSourceKind.ContributionSelection;
                writer.ModerationQualificationMutationReceipts.Remove(
                    await writer.ModerationQualificationMutationReceipts.SingleAsync(row => row.SourceKind == kind));
                await writer.SaveChangesAsync();
            }
        }
        if (substitution == 4) source = source with { ExpectedGenerationRevision = 2 };
        if (substitution == 5) database.ExpireGovernanceAfterSave = true;
        await Assert.That((await database.ExecuteAsync(
            substitution == 0 ? database.AdministratorId : database.UrgencyAssessorId, source)).IsSuccess).IsFalse();
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That(await reader.ModerationEventTargetAdoptions.CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.ModerationQualificationMutationReceipts.AnyAsync(row => row.Id == source.OperationId)).IsFalse();
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(5L);
    }

    [Test]
    [Arguments(1L, 0, true)]
    [Arguments(2L, 0, true)]
    [Arguments(2L, 1, true)]
    [Arguments(1L, 1, false)]
    [Arguments(2L, 2, false)]
    public async Task RetainedSelection_RequiresExactCurrentOrNextDecisionRevision(
        long selectionDecisionRevision, int decisionTransitions, bool accepted)
    {
        var (database, source) = await CreateAsync(selectionDecisionRevision);
        await using var owner = database;
        await using (var writer = database.Context())
        {
            writer.ModerationTrustDomainId = database.Scope.TrustDomainId;
            var decision = await writer.ModerationDecisions.SingleAsync();
            if (decisionTransitions > 0) decision.RecordResponse(1, Now);
            if (decisionTransitions > 1) decision.Withdraw(2, Now);
            await writer.SaveChangesAsync();
        }
        await Assert.That((await database.ExecuteAsync(database.UrgencyAssessorId, source)).IsSuccess).IsEqualTo(accepted);
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        var retained = await reader.ModerationContributionSelections.SingleAsync();
        var unchangedDecision = await reader.ModerationDecisions.SingleAsync();
        await Assert.That(retained.DecisionRevision).IsEqualTo(selectionDecisionRevision);
        await Assert.That(unchangedDecision.Revision).IsEqualTo(1L + decisionTransitions);
        await Assert.That(unchangedDecision.State).IsEqualTo(decisionTransitions switch
        {
            0 => ModerationDecisionState.Proposed,
            1 => ModerationDecisionState.AwaitingFinding,
            _ => ModerationDecisionState.Withdrawn
        });
        await Assert.That(await reader.ModerationEventTargetAdoptions.CountAsync()).IsEqualTo(accepted ? 1 : 0);
        if (accepted)
        {
            var adoption = await reader.ModerationEventTargetAdoptions.SingleAsync();
            await Assert.That(adoption.SelectionId).IsEqualTo(retained.Id);
            await Assert.That(adoption.SelectionRevision).IsEqualTo(retained.Revision);
        }
        await Assert.That(await reader.ModerationQualificationMutationReceipts.AnyAsync(row => row.Id == source.OperationId))
            .IsEqualTo(accepted);
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(accepted ? 6L : 5L);
        await Assert.That(await reader.ModerationEffectIntents.CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.ModerationSanctions.CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task SkippedDecisionRevision_CannotAuthorSelectionOrCommitEventAdoption()
    {
        var (database, source) = await CreateAsync(selectionDecisionRevision: 3);
        await using var owner = database;
        await Assert.That((await database.ExecuteAsync(database.UrgencyAssessorId, source)).IsSuccess).IsFalse();
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That(await reader.ModerationContributionSelections.CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.ModerationEventTargetAdoptions.CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.ModerationQualificationMutationReceipts.AnyAsync(row => row.Id == source.OperationId)).IsFalse();
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(4L);
        await Assert.That((await reader.ModerationDecisions.SingleAsync()).Revision).IsEqualTo(1L);
        await Assert.That(await reader.ModerationEffectIntents.CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.ModerationSanctions.CountAsync()).IsEqualTo(0);
    }

    internal static async Task<(SourceDatabase Database, AdoptModerationEventTargetSource Source)> CreateAsync(
        long selectionDecisionRevision = 2)
    {
        var database = await SourceDatabase.CreateAsync();
        var resourceId = await ModerationResourceGenerationWriterTests.SeedAsync(database);
        Guid eventId;
        await using (var reader = database.Context())
            eventId = await reader.Set<EventResource>().Where(row => row.Id == resourceId).Select(row => row.EventId).SingleAsync();
        var generationId = Guid.CreateVersion7();
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, new RecordModerationEventGenerationSource
        {
            OperationId = Guid.CreateVersion7(), SourceId = generationId, Target = database.Scope,
            ExpectedQualificationRevision = 1, EventId = eventId, ExpectedEventRevision = 0, ValidUntil = Now.AddMinutes(10)
        })).IsSuccess).IsTrue();
        var opened = database.OpenCase() with { ExpectedQualificationRevision = 2 };
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, opened)).IsSuccess).IsTrue();
        var request = database.Publication(3);
        var publication = await database.PublishAsync(database.AdministratorId, request);
        await Assert.That(publication.Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        var profile = request.Proposed.Profiles.Single();
        var decision = ModerationDecision.Propose(Guid.CreateVersion7(), opened.CaseId,
            SubjectReference.Create(ModerationSubjectKind.Human, database.SubjectId, database.Instance),
            SubjectReference.Create(ModerationSubjectKind.Human, database.AdministratorId, database.Instance),
            database.Scope, database.Scope, profile.Rule, [ModerationCapability.Publish], Now, Now.AddHours(1), null, 1, Now);
        await using (var writer = database.Context())
        {
            writer.ModerationTrustDomainId = database.Scope.TrustDomainId;
            writer.ModerationDecisions.Add(decision);
            await writer.SaveChangesAsync();
        }
        var target = new ModerationPolicyTarget(database.Scope, decision.Subject.Kind, decision.Subject.Id,
            eventId, 1, "event.publish", null);
        var selectionId = Guid.CreateVersion7();
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, new RecordModerationContributionSelectionSource
        {
            OperationId = Guid.CreateVersion7(), Target = database.Scope, ExpectedQualificationRevision = 4,
            SelectionId = selectionId, EffectId = Guid.CreateVersion7(), CaseId = opened.CaseId, ExpectedCaseRevision = 1,
            DecisionId = decision.Id, ExpectedDecisionRevision = selectionDecisionRevision, PolicyId = request.Proposed.PolicyId,
            ExpectedPolicyRevision = 1, PublicationId = publication.Publication!.Id, ProfileId = profile.Id,
            Contribution = new(decision.Id, decision.Rule, decision.Scope, decision.Subject.Kind, decision.Subject.Id,
                decision.StartsAt, null, profile.Effects.Select(effect => new ModerationPolicyEffectTarget(effect, target)).ToImmutableArray(),
                [new(null, target)]), ValidUntil = Now.AddMinutes(10)
        })).IsSuccess).IsEqualTo(selectionDecisionRevision is 1 or 2);
        return (database, new()
        {
            OperationId = Guid.CreateVersion7(), Target = database.Scope,
            ExpectedQualificationRevision = selectionDecisionRevision is 1 or 2 ? 5 : 4,
            AdoptionId = Guid.CreateVersion7(), SelectionId = selectionId, BindingOrdinal = 0, GenerationId = generationId,
            ExpectedGenerationRevision = 1, CaseId = opened.CaseId, ExpectedCaseRevision = 1, ValidUntil = Now.AddMinutes(5)
        });
    }
}
