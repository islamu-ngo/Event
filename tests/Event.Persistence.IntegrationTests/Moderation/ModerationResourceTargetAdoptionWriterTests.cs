using System.Collections.Immutable;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Policy;
using Explore.Domain.Enums;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;
using Microsoft.EntityFrameworkCore;
using SourceDatabase = Event.Persistence.IntegrationTests.Moderation.ModerationQualificationWriterTests.SourceDatabase;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed class ModerationResourceTargetAdoptionWriterTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task IndependentNativeAdoption_ExactRetryAndTerminalRevocationPreserveOriginalGeneration(bool deleteNativeResource)
    {
        var (database, source) = await CreateAsync();
        await using var owner = database;
        var accepted = await database.ExecuteAsync(database.UrgencyAssessorId, source);
        await Assert.That(accepted.IsSuccess).IsTrue();
        var replay = await database.ExecuteAsync(database.UrgencyAssessorId, source);
        await Assert.That(replay.Id).IsEqualTo(accepted.Id);
        await Assert.That(replay.QualificationRevision).IsEqualTo(accepted.QualificationRevision);
        if (deleteNativeResource)
        {
            await using var deleted = database.Context();
            deleted.ModerationTrustDomainId = database.Scope.TrustDomainId;
            var generation = await deleted.ModerationResourceGenerations.SingleAsync();
            await deleted.EventResources.Where(row => row.Id == generation.ResourceId)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsDeleted, true));
            await deleted.Events.Where(row => row.Id == generation.EventId)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsDeleted, true));
            await deleted.Actors.Where(row => row.Id == generation.ActorId)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsDeleted, true));
            var retainedReplay = await database.ExecuteAsync(database.UrgencyAssessorId, source);
            await Assert.That(retainedReplay.IsSuccess).IsTrue();
            await Assert.That(retainedReplay.Id).IsEqualTo(accepted.Id);
            await Assert.That(retainedReplay.QualificationRevision).IsEqualTo(accepted.QualificationRevision);
        }
        var revoke = new RevokeModerationResourceTargetSource
        {
            OperationId = Guid.CreateVersion7(), Target = database.Scope, ExpectedQualificationRevision = 6,
            AdoptionId = source.AdoptionId, ExpectedAdoptionRevision = 1, CaseId = source.CaseId, ExpectedCaseRevision = 1
        };
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, revoke)).IsSuccess).IsTrue();
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, revoke)).IsSuccess).IsTrue();
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        var adoption = await reader.ModerationResourceTargetAdoptions.SingleAsync();
        await Assert.That(adoption.Revision).IsEqualTo(2L);
        await Assert.That(adoption.GenerationId).IsEqualTo(source.GenerationId);
        await Assert.That(adoption.AdoptedByHumanId).IsEqualTo(database.UrgencyAssessorId);
        await Assert.That(adoption.RevokedByHumanId).IsEqualTo(database.AdministratorId);
        await Assert.That(await reader.ModerationEffectIntents.CountAsync()).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Resource_adoption_cannot_acquire_a_replacement_parent_after_its_tenant_anchor(bool replaceOrganizer)
    {
        var (database, source) = await CreateAsync();
        await using var owner = database;
        Guid eventId;
        Guid publisherId;
        var replacement = Guid.CreateVersion7();
        await using (var setup = database.Context())
        {
            setup.ModerationTrustDomainId = database.Scope.TrustDomainId;
            var generation = await setup.ModerationResourceGenerations.SingleAsync();
            eventId = generation.EventId;
            publisherId = generation.ActorId;
            setup.Actors.Add(new Explore.Domain.Actor
            {
                Id = replacement, ActorTypeId = (int)ActorTypeEnum.User, ActorType = null!,
                UserId = database.ReviewerId,
                Pii = new Explore.Domain.ActorPii { ActorId = replacement, DisplayName = "Replacement resource parent" }
            });
            await setup.SaveChangesAsync();
        }
        var checkpoint = new ModerationEventGenerationWriterTests.ReplaceOwnerBeforeTenant(eventId, replacement, replaceOrganizer);
        var result = await database.ExecuteAsync(database.UrgencyAssessorId, source, interceptor: checkpoint);
        await Assert.That(checkpoint.Observed).IsTrue();
        await Assert.That(checkpoint.ActorAnchorsBeforeTenant.Contains(publisherId)).IsTrue();
        await Assert.That(checkpoint.ActorAnchors.Contains(replacement)).IsFalse();
        await Assert.That(result.IsSuccess).IsFalse();
        await using var current = database.Context();
        current.ModerationTrustDomainId = database.Scope.TrustDomainId;
        var owningEvent = await current.Events.SingleAsync(row => row.Id == eventId);
        await Assert.That(owningEvent.ActorId).IsEqualTo(publisherId);
        await Assert.That(owningEvent.OrganizerActorId).IsNull();
        await Assert.That(await current.ModerationResourceTargetAdoptions.CountAsync()).IsEqualTo(0);
        await Assert.That(await current.ModerationQualificationMutationReceipts.AnyAsync(row => row.Id == source.OperationId)).IsFalse();
        await Assert.That((await current.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(5L);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Stored_resource_adoption_plans_its_storage_actor_before_tenant_and_rejects_late_rebinding(bool replaceStorageActor)
    {
        var (database, source) = await CreateAsync(storedFile: true);
        await using var owner = database;
        Guid storageId;
        Guid storageActor;
        var replacement = Guid.CreateVersion7();
        await using (var setup = database.Context())
        {
            setup.ModerationTrustDomainId = database.Scope.TrustDomainId;
            var generation = await setup.ModerationResourceGenerations.SingleAsync();
            storageId = generation.StorageObjectId!.Value;
            storageActor = (await setup.StorageObjects.SingleAsync(row => row.Id == storageId)).ActorId!.Value;
            setup.Actors.Add(new Explore.Domain.Actor
            {
                Id = replacement, ActorTypeId = (int)ActorTypeEnum.User, ActorType = null!,
                UserId = database.UrgencyAssessorId,
                Pii = new Explore.Domain.ActorPii { ActorId = replacement, DisplayName = "Replacement storage owner" }
            });
            await setup.SaveChangesAsync();
        }
        var checkpoint = new ModerationResourceGenerationWriterTests.ReplaceStorageActorBeforeTenant(
            storageId, replaceStorageActor ? replacement : null);
        var result = await database.ExecuteAsync(database.UrgencyAssessorId, source, interceptor: checkpoint);
        await Assert.That(checkpoint.Observed).IsTrue();
        await Assert.That(checkpoint.ActorAnchorsBeforeTenant.Contains(storageActor)).IsTrue();
        await Assert.That(checkpoint.ActorAnchors.Contains(replacement)).IsFalse();
        await Assert.That(result.IsSuccess).IsEqualTo(!replaceStorageActor);
        await using var current = database.Context();
        current.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That((await current.StorageObjects.SingleAsync(row => row.Id == storageId)).ActorId).IsEqualTo(storageActor);
        await Assert.That(await current.ModerationResourceTargetAdoptions.CountAsync()).IsEqualTo(replaceStorageActor ? 0 : 1);
        await Assert.That(await current.ModerationQualificationMutationReceipts.AnyAsync(row => row.Id == source.OperationId))
            .IsEqualTo(!replaceStorageActor);
        await Assert.That((await current.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(replaceStorageActor ? 5L : 6L);
    }

    [Test]
    [Arguments(0, false)]
    [Arguments(1, false)]
    [Arguments(2, false)]
    [Arguments(3, false)]
    [Arguments(4, false)]
    [Arguments(5, false)]
    [Arguments(0, true)]
    [Arguments(1, true)]
    [Arguments(2, true)]
    [Arguments(3, true)]
    [Arguments(4, true)]
    [Arguments(5, true)]
    [Arguments(6, true)]
    [Arguments(7, true)]
    public async Task UnqualifiedChangedOrSelfApprovedTarget_DoesNotCommitAdoption(int substitution, bool content)
    {
        var (database, source) = await CreateAsync(content);
        await using var owner = database;
        var caller = substitution == 0 ? database.AdministratorId : database.UrgencyAssessorId;
        if (substitution is 1 or 2 or 3)
        {
            await using var writer = database.Context();
            writer.ModerationTrustDomainId = database.Scope.TrustDomainId;
            var generation = await writer.ModerationResourceGenerations.SingleAsync();
            if (substitution == 1)
                await writer.Set<Explore.Domain.EventResource>().Where(row => row.Id == generation.ResourceId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.ConcurrencyStamp, Guid.NewGuid()));
            else
            {
                var kind = substitution == 2 ? Explore.Domain.ModerationQualificationSourceKind.ResourceGeneration
                    : Explore.Domain.ModerationQualificationSourceKind.ContributionSelection;
                writer.ModerationQualificationMutationReceipts.Remove(
                    await writer.ModerationQualificationMutationReceipts.SingleAsync(row => row.SourceKind == kind));
                await writer.SaveChangesAsync();
            }
        }
        if (substitution == 4) source = source with { ExpectedGenerationRevision = 2 };
        if (substitution == 5) database.ExpireGovernanceAfterSave = true;
        if (substitution == 6)
        {
            var replacementId = await ModerationResourceGenerationWriterTests.SeedAsync(database);
            await using var writer = database.Context();
            var replacementEvent = await writer.EventResources.Where(row => row.Id == replacementId)
                .Select(row => row.EventId).SingleAsync();
            await writer.EventResources.Where(row => row.Id == source.Target.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.EventId, replacementEvent));
        }
        if (substitution == 7)
            source = source with
            {
                Target = AuthorityScope.Resource(
                    AuthorityScope.Tenant(database.Instance, Guid.CreateVersion7()), source.Target.Id)
            };
        await Assert.That((await database.ExecuteAsync(caller, source)).IsSuccess).IsFalse();
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That(await reader.ModerationResourceTargetAdoptions.CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.ModerationQualificationMutationReceipts.AnyAsync(row => row.Id == source.OperationId)).IsFalse();
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(5L);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Owning_Event_identity_cannot_substitute_for_the_qualified_EventResource_target(bool content)
    {
        var (database, source) = await CreateAsync(content);
        await using var owner = database;
        Guid eventId;
        await using (var reader = database.Context())
        {
            eventId = await reader.EventResources.Where(row => row.Id == source.Target.Id)
                .Select(row => row.EventId).SingleAsync();
            await Assert.That(eventId).IsNotEqualTo(source.Target.Id);
        }
        var substitution = source with { Target = AuthorityScope.Resource(database.Scope, eventId) };
        await Assert.That((await database.ExecuteAsync(database.UrgencyAssessorId, substitution)).IsSuccess).IsFalse();
        await using var observer = database.Context();
        observer.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That(await observer.ModerationResourceTargetAdoptions.CountAsync()).IsEqualTo(0);
        await Assert.That(await observer.ModerationQualificationMutationReceipts
            .AnyAsync(row => row.Id == source.OperationId)).IsFalse();
        await Assert.That((await observer.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(5L);
        await Assert.That(await observer.ModerationEffectIntents.CountAsync()).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Content_resource_adoption_binds_the_owning_Event_not_its_human_submitter(bool wrongEvent)
    {
        var (database, source) = await CreateAsync(content: true, wrongEvent: wrongEvent);
        await using var owner = database;
        var accepted = await database.ExecuteAsync(database.UrgencyAssessorId, source);
        await Assert.That(accepted.IsSuccess).IsEqualTo(!wrongEvent);
        if (!wrongEvent)
        {
            var replay = await database.ExecuteAsync(database.UrgencyAssessorId, source);
            await Assert.That(replay.Id).IsEqualTo(accepted.Id);
            await Assert.That(replay.QualificationRevision).IsEqualTo(accepted.QualificationRevision);
        }
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        var decision = await reader.ModerationDecisions.SingleAsync();
        await Assert.That(decision.Subject.Kind).IsEqualTo(ModerationSubjectKind.Content);
        await Assert.That(decision.Subject.Id).IsNotEqualTo(database.SubjectId);
        await Assert.That(decision.Subject.Origin).IsEqualTo(database.Scope);
        await Assert.That(await reader.ModerationResourceTargetAdoptions.CountAsync()).IsEqualTo(wrongEvent ? 0 : 1);
        await Assert.That(await reader.ModerationQualificationMutationReceipts.AnyAsync(row => row.Id == source.OperationId))
            .IsEqualTo(!wrongEvent);
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(wrongEvent ? 5L : 6L);
        await Assert.That(await reader.ModerationEffectIntents.CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.EventResources.AllAsync(
            row => row.PublicationStateId == (int)EventResourcePublicationStateEnum.Draft)).IsTrue();
    }

    [Test]
    [Arguments(false, 1L, 0, true)]
    [Arguments(false, 2L, 0, true)]
    [Arguments(false, 2L, 1, true)]
    [Arguments(false, 1L, 1, false)]
    [Arguments(false, 2L, 2, false)]
    [Arguments(true, 1L, 0, true)]
    [Arguments(true, 2L, 0, true)]
    [Arguments(true, 2L, 1, true)]
    [Arguments(true, 1L, 1, false)]
    [Arguments(true, 2L, 2, false)]
    public async Task RetainedSelection_RequiresExactCurrentOrNextDecisionRevision(
        bool content, long selectionDecisionRevision, int decisionTransitions, bool accepted)
    {
        var (database, source) = await CreateAsync(content, selectionDecisionRevision: selectionDecisionRevision);
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
        await Assert.That(await reader.ModerationResourceTargetAdoptions.CountAsync()).IsEqualTo(accepted ? 1 : 0);
        if (accepted)
        {
            var adoption = await reader.ModerationResourceTargetAdoptions.SingleAsync();
            await Assert.That(adoption.SelectionId).IsEqualTo(retained.Id);
            await Assert.That(adoption.SelectionRevision).IsEqualTo(retained.Revision);
        }
        await Assert.That(await reader.ModerationQualificationMutationReceipts.AnyAsync(row => row.Id == source.OperationId))
            .IsEqualTo(accepted);
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(accepted ? 6L : 5L);
        await Assert.That(await reader.ModerationEffectIntents.CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.ModerationSanctions.CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.EventResources.AllAsync(
            row => row.PublicationStateId == (int)EventResourcePublicationStateEnum.Draft)).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SkippedDecisionRevision_CannotAuthorSelectionOrCommitResourceAdoption(bool content)
    {
        var (database, source) = await CreateAsync(content, selectionDecisionRevision: 3);
        await using var owner = database;
        await Assert.That((await database.ExecuteAsync(database.UrgencyAssessorId, source)).IsSuccess).IsFalse();
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That(await reader.ModerationContributionSelections.CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.ModerationResourceTargetAdoptions.CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.ModerationQualificationMutationReceipts.AnyAsync(row => row.Id == source.OperationId)).IsFalse();
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(4L);
        await Assert.That((await reader.ModerationDecisions.SingleAsync()).Revision).IsEqualTo(1L);
        await Assert.That(await reader.ModerationEffectIntents.CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.ModerationSanctions.CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.EventResources.AllAsync(
            row => row.PublicationStateId == (int)EventResourcePublicationStateEnum.Draft)).IsTrue();
    }

    internal static async Task<(SourceDatabase Database, AdoptModerationResourceTargetSource Source)> CreateAsync(
        bool content = false, bool wrongEvent = false, long selectionDecisionRevision = 2, bool storedFile = false)
    {
        var database = await SourceDatabase.CreateAsync();
        var resourceId = await ModerationResourceGenerationWriterTests.SeedAsync(database, storedFile: storedFile);
        if (storedFile)
        {
            await using var setup = database.Context();
            var actorId = Guid.CreateVersion7();
            setup.Actors.Add(new Explore.Domain.Actor
            {
                Id = actorId, ActorTypeId = (int)ActorTypeEnum.User, ActorType = null!, UserId = database.ReviewerId,
                Pii = new Explore.Domain.ActorPii { ActorId = actorId, DisplayName = "Original resource storage owner" }
            });
            var storageId = await setup.EventResources.Where(row => row.Id == resourceId).Select(row => row.StorageObjectId).SingleAsync();
            (await setup.StorageObjects.SingleAsync(row => row.Id == storageId)).ActorId = actorId;
            await setup.SaveChangesAsync();
        }
        var subjectResourceId = wrongEvent ? await ModerationResourceGenerationWriterTests.SeedAsync(database) : resourceId;
        Guid eventId;
        await using (var reader = database.Context())
            eventId = await reader.EventResources.Where(row => row.Id == subjectResourceId).Select(row => row.EventId).SingleAsync();
        var scope = content ? AuthorityScope.Resource(database.Scope, resourceId) : database.Scope;
        var generationId = Guid.CreateVersion7();
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, new RecordModerationResourceGenerationSource
        {
            OperationId = Guid.CreateVersion7(), SourceId = generationId, Target = database.Scope,
            ExpectedQualificationRevision = 1, ResourceId = resourceId, ExpectedResourceRevision = 0,
            ValidUntil = Now.AddMinutes(10)
        })).IsSuccess).IsTrue();
        var opened = database.OpenCase(scope) with
        {
            ExpectedQualificationRevision = 2,
            SubjectKind = content ? ModerationSubjectKind.Content : ModerationSubjectKind.Human,
            SubjectId = content ? eventId : database.SubjectId
        };
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, opened)).IsSuccess).IsTrue();
        var request = database.Publication(3,
            content ? ModerationReferenceProfileId.CFG09 : ModerationReferenceProfileId.CFG06);
        if (content)
            request = request with
            {
                Proposed = request.Proposed with
                {
                    Profiles = [request.Proposed.Profiles.Single() with
                    {
                        ScopeKinds = [ModerationScopeKind.Resource], SubjectKinds = [ModerationSubjectKind.Content]
                    }]
                }
            };
        var publication = await database.PublishAsync(database.AdministratorId, request);
        await Assert.That(publication.Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        var profile = request.Proposed.Profiles.Single();
        var decision = ModerationDecision.Propose(Guid.CreateVersion7(), opened.CaseId,
            SubjectReference.Create(opened.SubjectKind, opened.SubjectId, content ? database.Scope : database.Instance),
            SubjectReference.Create(ModerationSubjectKind.Human, database.AdministratorId, database.Instance),
            database.Scope, scope, profile.Rule, [content ? ModerationCapability.ReadResource : ModerationCapability.Publish],
            Now, content ? null : Now.AddHours(1), content ? Now.AddHours(1) : null, profile.RequiredApprovals, Now);
        await using (var writer = database.Context())
        {
            writer.ModerationTrustDomainId = database.Scope.TrustDomainId;
            writer.ModerationDecisions.Add(decision);
            if (!content)
                writer.ModerationQualificationEvidence.Add(Explore.Domain.ModerationQualificationEvidence.RecordResponse(
                    Guid.CreateVersion7(), opened.CaseId, database.Scope.TrustDomainId, database.TenantId,
                    database.SubjectId, database.SubjectId, "Native resource response witness.", Now));
            await writer.SaveChangesAsync();
        }
        var target = new ModerationPolicyTarget(scope, decision.Subject.Kind, decision.Subject.Id,
            resourceId, 1, "resource.read", null);
        var selectionId = Guid.CreateVersion7();
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, new RecordModerationContributionSelectionSource
        {
            OperationId = Guid.CreateVersion7(), Target = scope, ExpectedQualificationRevision = 4,
            SelectionId = selectionId, EffectId = Guid.CreateVersion7(), CaseId = opened.CaseId, ExpectedCaseRevision = 1,
            DecisionId = decision.Id, ExpectedDecisionRevision = selectionDecisionRevision, PolicyId = request.Proposed.PolicyId,
            ExpectedPolicyRevision = 1, PublicationId = publication.Publication!.Id, ProfileId = profile.Id,
            Contribution = new(decision.Id, decision.Rule, decision.Scope, decision.Subject.Kind, decision.Subject.Id,
                decision.StartsAt, null,
                profile.Effects.Select(effect => new ModerationPolicyEffectTarget(effect, target)).ToImmutableArray(),
                [new(null, target)]), ValidUntil = Now.AddMinutes(10)
        })).IsSuccess).IsEqualTo(selectionDecisionRevision is 1 or 2);
        return (database, new()
        {
            OperationId = Guid.CreateVersion7(), Target = scope,
            ExpectedQualificationRevision = selectionDecisionRevision is 1 or 2 ? 5 : 4,
            AdoptionId = Guid.CreateVersion7(), SelectionId = selectionId, BindingOrdinal = 0, GenerationId = generationId,
            ExpectedGenerationRevision = 1, CaseId = opened.CaseId, ExpectedCaseRevision = 1, ValidUntil = Now.AddMinutes(5)
        });
    }
}
