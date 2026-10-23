using System.Collections.Immutable;
using System.Text.Json;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Policy;
using Explore.Application.Notifications;
using Explore.Application.Services;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using ISLAMU.Moderation.Application;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationCaseNativeProofTests
{
    [Test]
    public Task NativeScheduledResourceExpiryCommitsRetainedCaptureWithoutPolicyActivation() =>
        ScheduledResourceTerminalAsync(null);

    [Test]
    [Arguments("qualification-revision")]
    [Arguments("capture-deadline")]
    public Task NativeScheduledResourceExpiryRollsBackCompletePrimaryFlushLoss(string loss) =>
        ScheduledResourceTerminalAsync(loss);

    private static async Task ScheduledResourceTerminalAsync(string? loss)
    {
        // Starting finding/decision/sanction aggregates and credentials are historical setup.
        // The native inactive finite policy is not active-policy or conformance proof.
        await using var fixture = await ScheduledProvenanceFixture.CreateAsync(endedInterval: true);
        var database = fixture.Database;
        var operation = fixture.Reconcile();
        await Assert.That(database.Clock.Now).IsEqualTo(fixture.Decision.EndsAt!.Value);
        await Assert.That(fixture.Sanction.State).IsEqualTo(ModerationSanctionState.Scheduled);
        await Assert.That(operation.Kind).IsEqualTo(ModerationOperationKind.Reconcile);

        var generationId = Guid.CreateVersion7();
        var selectionId = Guid.CreateVersion7();
        var profileAdoptionId = Guid.CreateVersion7();
        ModerationPolicyPublication publication;
        ModerationPolicyProfileDefinition profile;
        Guid resourceStamp;
        await using (var reader = database.Context())
        {
            publication = await reader.ModerationPolicyPublications.AsNoTracking()
                .SingleAsync(row => row.JurisdictionTenantId == fixture.Case.Scope.Id);
            profile = ModerationPolicyPublicationProjection.RestoreHistory(publication)!
                .Proposed.Profiles.Single();
            var resource = await reader.Set<EventResource>().AsNoTracking()
                .SingleAsync(row => row.Id == database.ResourceId);
            resourceStamp = resource.ConcurrencyStamp;
            await Assert.That(resource.EventId).IsEqualTo(database.EventId);
            await Assert.That((await reader.Events.AsNoTracking().SingleAsync(row => row.Id == resource.EventId))
                .SubmittedByUserId).IsEqualTo(fixture.Decision.Subject.Id);
        }
        await WriteScheduledTerminalSourceAsync(fixture, fixture.Original.Assessor,
            new RecordModerationResourceGenerationSource
            {
                OperationId = Guid.CreateVersion7(), Target = fixture.Case.Scope,
                ExpectedQualificationRevision = 1, SourceId = generationId,
                ResourceId = database.ResourceId, ExpectedResourceRevision = 0, ValidUntil = fixture.Until
            });
        var target = new ModerationPolicyTarget(fixture.Case.Scope, fixture.Decision.Subject.Kind,
            fixture.Decision.Subject.Id, database.ResourceId, 1, "resource.read", null);
        var contribution = new ModerationPolicyContribution(fixture.Decision.Id, fixture.Decision.Rule,
            fixture.Case.Scope, fixture.Decision.Subject.Kind, fixture.Decision.Subject.Id,
            fixture.Decision.StartsAt, null,
            profile.Effects.Select(effect => new ModerationPolicyEffectTarget(effect, target)).ToImmutableArray(),
            [new(null, target)]);
        await WriteScheduledTerminalSourceAsync(fixture, fixture.Original.Assessor,
            new RecordModerationContributionSelectionSource
            {
                OperationId = Guid.CreateVersion7(), Target = fixture.Case.Scope,
                ExpectedQualificationRevision = 1, SelectionId = selectionId, EffectId = operation.EffectId,
                CaseId = fixture.Case.Id, ExpectedCaseRevision = fixture.Case.Revision,
                DecisionId = fixture.Decision.Id, ExpectedDecisionRevision = fixture.Decision.Revision,
                PolicyId = publication.PolicyId, ExpectedPolicyRevision = publication.Revision,
                PublicationId = publication.Id, ProfileId = profile.Id,
                Contribution = contribution, ValidUntil = fixture.Until
            });
        var bindings = ModerationContributionSelectionProjection.Bindings(selectionId, contribution);
        // Adopt every retained ordinal, even when they share the same native Resource.
        foreach (var binding in bindings)
            await WriteScheduledTerminalSourceAsync(fixture, fixture.PrivacyOwner,
                new AdoptModerationResourceTargetSource
                {
                    OperationId = Guid.CreateVersion7(), Target = fixture.Case.Scope,
                    ExpectedQualificationRevision = 1, AdoptionId = Guid.CreateVersion7(),
                    SelectionId = selectionId, BindingOrdinal = binding.Ordinal, GenerationId = generationId,
                    ExpectedGenerationRevision = 1, CaseId = fixture.Case.Id,
                    ExpectedCaseRevision = fixture.Case.Revision, ValidUntil = fixture.Until
                });
        await WriteScheduledTerminalSourceAsync(fixture, fixture.PrivacyOwner, new AdoptModerationProfileSource
        {
            OperationId = Guid.CreateVersion7(), Target = fixture.Case.Scope,
            ExpectedQualificationRevision = 1, AdoptionId = profileAdoptionId,
            SelectionId = selectionId, Mode = ModerationNativeMode.Embedded,
            CaseId = fixture.Case.Id, ExpectedCaseRevision = fixture.Case.Revision, ValidUntil = fixture.Until
        });
        var draft = ModerationPolicyPublicationProjection.RestoreHistory(publication)!;
        var reason = draft.Proposed.Reasons.Single(row => row.Reference == profile.Reason);
        var classification = draft.Proposed.Classes.Single(row => row.Reference == reason.Class);
        var disclosure = new ModerationNoticeDisclosureIntent(Guid.CreateVersion7(), operation.EffectId,
            fixture.Decision.Id, fixture.Decision.Revision, fixture.Case.Id, fixture.Case.Revision,
            publication.PolicyId, publication.Revision, publication.Id, profile.Id, fixture.Decision.Id,
            ModerationNoticeSnapshotFactory.SelectionDigest(contribution),
            new(classification.Meaning), new(reason.Meaning),
            [new("The scheduled Resource contribution has reached its original end.")],
            new("Protected response remains available."), new("Request independent review."),
            new("Maintenance expires a contribution; it makes no new finding."),
            new("The original interval is unchanged."), fixture.Until, database.Clock.Now.AddYears(1));
        await WriteScheduledTerminalSourceAsync(fixture, fixture.Original.Review.Caller.UserId!.Value,
            new ApproveModerationNoticeDisclosureSource
            {
                OperationId = Guid.CreateVersion7(), Target = fixture.Case.Scope,
                ExpectedQualificationRevision = 1, Intent = disclosure
            });

        long qualificationRevision;
        int notificationCount;
        int outboxCount;
        await using (var reader = database.Context())
        {
            qualificationRevision = (await reader.ModerationQualificationEpochs.AsNoTracking().SingleAsync()).Revision;
            notificationCount = await reader.NotificationIntents.CountAsync();
            outboxCount = await reader.OutboxMessages.CountAsync();
        }
        var flush = new ScheduledResourceTerminalFlush(
            fixture, operation, qualificationRevision, notificationCount, outboxCount, loss);
        var commit = new ScheduledTerminalCommit();
        await using var context = database.Context(flush, commit);
        var (admin, machine) = ScheduledTerminalIdentity(context, fixture, null);
        var inventory = ModerationCapabilityRegistry.Parse(ModerationCapabilityRegistry.ReadInventory());
        var publications = new ModerationPolicyPublicationRepository(context, admin, machine, inventory, null, database.Clock);
        var activation = new ModerationNativeActivationQualification(context, inventory, null,
            new ModerationQualificationPolicySourceOwner(context, database.Clock), database.Clock);
        var keys = new ExternalApiKeyAuthenticationRepository(new SecurityTestSupport.AdministrativeAuthorityTestFactory(context));
        var machineBindings = new ModerationMachineSubjectBindingRepository(context, admin, machine, keys,
            new ModerationNativeAuthorityParticipantTests.UnconfiguredKeys(), database.Clock);
        var responses = new RecordingScheduledResponseOwner(fixture.Responses(context));
        var participant = new ModerationAuthorityParticipant(context, admin, machine, machineBindings, keys,
            publications, inventory, activation, database.Clock,
            caseAttribution: fixture.Attribution(context), preFindingResponses: responses);
        var selections = new ModerationPolicySelectionSource(context, publications, inventory);
        var observed = new ScheduledTerminalNoticeOwner(
            new ModerationPolicySelectionNoticeSourceOwner(context, selections, participant, database.Clock),
            participant, publication, profileAdoptionId);
        var unitOfWork = new EfCoreUnitOfWork(context);
        var mutationLock = new RelationalSettingMutationLock(context, unitOfWork);
        var materializer = new RecipientNotificationMaterializer(
            new NotificationIntentRepository(context, mutationLock, database.Clock), unitOfWork, mutationLock,
            new PrivacyErasureStateRepository(context));
        var privacy = fixture.Original.Privacy.Authority(context, database.Clock);
        var capture = new ModerationNoticeCaptureRepository(context, privacy, observed, selections,
            new ModerationNoticeRecipientRepository(context),
            new ModerationNoticeNativeMaterializer(
                new DefaultNotificationOwnershipResolver(Options.Create(new NotificationRoutingOptions())),
                new NotificationPreferenceResolver(context), materializer), database.Clock, participant);
        context.ModerationTrustDomainId = null;
        var result = await new ModerationOperationService(
            new ModerationStore(context, participant, database.Clock, privacy, capture),
            participant, new ModerationEffectPlanner(), database.Clock).ExecuteAsync(operation);

        await Assert.That(flush.CompleteFlushCount).IsEqualTo(1);
        await Assert.That(context.CurrentUserService!.UserId).IsNull();
        await Assert.That(responses.Session).IsNull();
        await Assert.That(observed.Configuration).IsNotNull();
        await Assert.That(observed.Observation).IsNotNull();
        var observedTarget = observed.Observation!.Targets.Single();
        await Assert.That(observedTarget.Target).IsEqualTo(target);
        await Assert.That(observedTarget.Owner).IsEqualTo("event.resource-generation");
        await Assert.That(observedTarget.ApprovedSource.Id).IsEqualTo(generationId);
        await Assert.That(observedTarget.ApprovedSource).IsEqualTo(observedTarget.CurrentSource);
        await using var inspect = database.Context();
        var generation = await inspect.ModerationResourceGenerations.AsNoTracking().SingleAsync(row => row.Id == generationId);
        await Assert.That(generation.ResourceId).IsEqualTo(database.ResourceId);
        await Assert.That(generation.EventId).IsEqualTo(database.EventId);
        await Assert.That(generation.ResourceStamp).IsEqualTo(resourceStamp);
        await Assert.That(generation.Revision).IsEqualTo(1L);
        await Assert.That(generation.ObservedByHumanId).IsEqualTo(fixture.Original.Assessor);
        var adoptions = await inspect.ModerationResourceTargetAdoptions.AsNoTracking()
            .Where(row => row.SelectionId == selectionId).ToListAsync();
        await Assert.That(adoptions.Count).IsEqualTo(bindings.Length);
        foreach (var binding in bindings)
        {
            var adoption = adoptions.Single(row => row.BindingOrdinal == binding.Ordinal);
            await Assert.That(adoption.GenerationId).IsEqualTo(generation.Id);
            await Assert.That(adoption.GenerationRevision).IsEqualTo(1L);
            await Assert.That(adoption.AdoptedByHumanId).IsEqualTo(fixture.PrivacyOwner);
            await Assert.That(adoption.RevokedAt).IsNull();
        }
        await Assert.That(await inspect.ModerationEventGenerations.AnyAsync()).IsFalse();
        await Assert.That(await inspect.ModerationEventTargetAdoptions.AnyAsync()).IsFalse();
        var decision = await inspect.ModerationDecisions.AsNoTracking().SingleAsync(row => row.Id == fixture.Decision.Id);
        var sanction = await inspect.ModerationSanctions.AsNoTracking().SingleAsync(row => row.Id == fixture.Sanction.Id);
        await Assert.That(decision.State).IsEqualTo(ModerationDecisionState.Scheduled);
        await Assert.That(decision.Revision).IsEqualTo(fixture.Decision.Revision);
        await Assert.That(decision.StartsAt).IsEqualTo(fixture.Decision.StartsAt);
        await Assert.That(decision.EndsAt).IsEqualTo(fixture.Decision.EndsAt);
        await Assert.That(decision.ReviewAt).IsEqualTo(fixture.Decision.ReviewAt);
        await Assert.That(sanction.StartsAt).IsEqualTo(fixture.Sanction.StartsAt);
        await Assert.That(sanction.EndsAt).IsEqualTo(fixture.Sanction.EndsAt);
        await Assert.That((await inspect.ModerationQualificationEpochs.AsNoTracking().SingleAsync()).Revision)
            .IsEqualTo(qualificationRevision);
        if (loss is not null)
        {
            await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Unavailable);
            await Assert.That(commit.CommittedCount).IsEqualTo(0);
            await Assert.That(sanction.State).IsEqualTo(ModerationSanctionState.Scheduled);
            await Assert.That(sanction.Revision).IsEqualTo(fixture.Sanction.Revision);
            await Assert.That(await inspect.ModerationOperationReceipts.AnyAsync(row => row.Id == operation.OperationId)).IsFalse();
            await Assert.That(await inspect.ModerationEffectIntents.AnyAsync(row => row.Id == operation.EffectId)).IsFalse();
            await Assert.That(await inspect.ModerationNoticeSources.AnyAsync(row => row.EffectId == operation.EffectId)).IsFalse();
            await Assert.That(await inspect.ModerationNotices.AnyAsync(row => row.EffectId == operation.EffectId)).IsFalse();
            await Assert.That(await inspect.NotificationIntents.CountAsync()).IsEqualTo(notificationCount);
            await Assert.That(await inspect.OutboxMessages.CountAsync()).IsEqualTo(outboxCount);
            return;
        }
        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Known);
        await Assert.That(commit.CommittedCount).IsEqualTo(1);
        await Assert.That(sanction.State).IsEqualTo(ModerationSanctionState.Expired);
        await Assert.That(sanction.Revision).IsEqualTo(fixture.Sanction.Revision + 1);
        await Assert.That(result.Revision).IsEqualTo(sanction.Revision);
        var receipt = await inspect.ModerationOperationReceipts.AsNoTracking().SingleAsync(row => row.Id == operation.OperationId);
        await Assert.That(receipt.PrincipalId).IsEqualTo(fixture.MaintenanceId);
        await Assert.That(receipt.DecisionId).IsEqualTo(decision.Id);
        await Assert.That(receipt.SanctionId).IsEqualTo(sanction.Id);
        await Assert.That(receipt.ResultRevision).IsEqualTo(sanction.Revision);
        var effect = await inspect.ModerationEffectIntents.AsNoTracking().SingleAsync(row => row.Id == operation.EffectId);
        await Assert.That(effect.OperationId).IsEqualTo(operation.OperationId);
        await Assert.That(effect.OperationKind).IsEqualTo((int)ModerationOperationKind.Reconcile);
        await Assert.That(effect.CompletedAt).IsNull();
        var source = await inspect.ModerationNoticeSources.AsNoTracking().SingleAsync(row => row.EffectId == effect.Id);
        var captured = JsonSerializer.Deserialize<ModerationNoticeCaptureSnapshot>(source.SnapshotJson)!;
        await Assert.That(captured.Matches(effect)).IsTrue();
        await Assert.That(captured.HasNoSubstantiveWork).IsFalse();
        await Assert.That(captured.Decision.Revision).IsEqualTo(fixture.Decision.Revision);
        await Assert.That(captured.Decision.StartsAt).IsEqualTo(fixture.Decision.StartsAt);
        await Assert.That(captured.Decision.EndsAt).IsEqualTo(fixture.Decision.EndsAt);
        await Assert.That(captured.Notice!.SanctionState).IsEqualTo(ModerationSanctionState.Expired);
        await Assert.That(captured.Notice.ApprovalId).IsEqualTo(disclosure.ApprovalId);
        await Assert.That(captured.ValidUntil).IsEqualTo(fixture.Until);
        await Assert.That(captured.Reviewers.Length).IsEqualTo(2);
        await Assert.That(await inspect.ModerationNotices.CountAsync(row => row.EffectId == effect.Id)).IsEqualTo(3);
        await Assert.That(await inspect.NotificationIntents.CountAsync()).IsEqualTo(notificationCount + 3);
        await Assert.That(await inspect.OutboxMessages.CountAsync()).IsEqualTo(outboxCount + 1);
    }

    /// <summary>Witnesses the persisted primary graph before injecting exact terminal qualification loss.</summary>
    private sealed class ScheduledResourceTerminalFlush(
        ScheduledProvenanceFixture fixture, ModerationOperation operation, long qualificationRevision,
        int notificationCount, int outboxCount, string? loss) : SaveChangesInterceptor
    {
        public int CompleteFlushCount { get; private set; }

        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken token = default)
        {
            var context = (ExploreDbContext)eventData.Context!;
            if (CompleteFlushCount != 0
                || context.ModerationNotices.Local.Count(row => row.EffectId == operation.EffectId) != 3
                || context.ChangeTracker.HasChanges())
                return result;
            await Assert.That(context.Database.CurrentTransaction).IsNotNull();
            var receipt = await context.ModerationOperationReceipts.AsNoTracking()
                .SingleAsync(row => row.Id == operation.OperationId, token);
            var sanction = await context.ModerationSanctions.AsNoTracking()
                .SingleAsync(row => row.Id == fixture.Sanction.Id, token);
            var effect = await context.ModerationEffectIntents.AsNoTracking()
                .SingleAsync(row => row.Id == operation.EffectId, token);
            var source = await context.ModerationNoticeSources.AsNoTracking()
                .SingleAsync(row => row.EffectId == operation.EffectId, token);
            var captured = JsonSerializer.Deserialize<ModerationNoticeCaptureSnapshot>(source.SnapshotJson)!;
            await Assert.That(sanction.State).IsEqualTo(ModerationSanctionState.Expired);
            await Assert.That(sanction.Revision).IsEqualTo(fixture.Sanction.Revision + 1);
            await Assert.That(receipt.ResultRevision).IsEqualTo(sanction.Revision);
            await Assert.That(receipt.PrincipalId).IsEqualTo(fixture.MaintenanceId);
            await Assert.That(effect.OperationId).IsEqualTo(receipt.Id);
            await Assert.That(effect.SanctionRevision).IsEqualTo(sanction.Revision);
            await Assert.That(captured.Matches(effect)).IsTrue();
            await Assert.That(captured.HasNoSubstantiveWork).IsFalse();
            await Assert.That(await context.ModerationNotices.CountAsync(row => row.EffectId == effect.Id, token)).IsEqualTo(3);
            await Assert.That(await context.NotificationIntents.CountAsync(token)).IsEqualTo(notificationCount + 3);
            await Assert.That(await context.OutboxMessages.CountAsync(token)).IsEqualTo(outboxCount + 1);
            CompleteFlushCount++;
            if (loss == "qualification-revision")
                // Adversarial loss in the real writer transaction, not qualification setup.
                await Assert.That(await context.ModerationQualificationEpochs
                    .Where(row => row.Revision == qualificationRevision)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Revision, row => row.Revision + 1), token))
                    .IsEqualTo(1);
            else if (loss == "capture-deadline")
                fixture.Database.Clock.Now = captured.ValidUntil!.Value;
            return result;
        }
    }
}
