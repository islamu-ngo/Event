using System.Collections.Immutable;
using System.Data.Common;
using System.Security.Claims;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Policy;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Identity;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using ISLAMU.Moderation.Application;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SourceDatabase = Event.Persistence.IntegrationTests.Moderation.ModerationQualificationWriterTests.SourceDatabase;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationContentNoticeCommitTests
{
    [Test]
    [Arguments(false, 0)]
    [Arguments(true, 0)]
    [Arguments(false, 1)]
    [Arguments(true, 1)]
    [Arguments(false, 2)]
    [Arguments(true, 2)]
    [Arguments(false, 3)]
    [Arguments(true, 3)]
    [Arguments(true, 4)]
    [Arguments(true, 5)]
    public async Task Urgent_Content_acceptance_keeps_original_sources_through_actual_commit(bool colocated, int loss)
    {
        await using var database = await SourceDatabase.CreateAsync();
        var prepared = await PrepareSubstantiveContentAsync(database);
        var checkpoint = new ContentCaptureCheckpoint(database, prepared, loss);
        using var baseline = database.Context();
        var options = (DbContextOptions<ExploreDbContext>)baseline.GetService<IDbContextOptions>();
        await using var context = new ExploreDbContext(new DbContextOptionsBuilder<ExploreDbContext>(options)
            .AddInterceptors(new ObserveContentCapture(checkpoint), new ContentCaptureFlush(checkpoint),
                new ContentCaptureCommit(checkpoint)).Options)
        { TenantContext = database };
        await using var shared = colocated
            ? await ModerationNoticePrivacyFixture.CreateColocatedAsync(context, new ContentCaptureCommit(checkpoint)) : null;
        var clock = new CommitClock(database);
        var privacy = (shared ?? database.Privacy).Authority(context, clock);
        var (admin, machine) = ContentIdentity(context, database.ReviewerId);
        var publications = new ModerationPolicyPublicationRepository(context, admin, machine, database.Inventory, null, clock);
        var activation = new ModerationNativeActivationQualification(context, database.Inventory, null,
            new ModerationQualificationPolicySourceOwner(context, clock), clock);
        var keys = new ExternalApiKeyAuthenticationRepository(new SecurityTestSupport.AdministrativeAuthorityTestFactory(context));
        var bindings = new ModerationMachineSubjectBindingRepository(context, admin, machine, keys,
            new ModerationNativeAuthorityParticipantTests.UnconfiguredKeys(), clock);
        var participant = new ModerationAuthorityParticipant(context, admin, machine, bindings, keys,
            publications, database.Inventory, activation, clock);
        var selections = new ModerationPolicySelectionSource(context, publications, database.Inventory);
        var sourceOwner = new ModerationPolicySelectionNoticeSourceOwner(context, selections, participant, clock);
        var unit = new EfCoreUnitOfWork(context);
        var mutationLock = new RelationalSettingMutationLock(context, unit);
        var materializer = new RecipientNotificationMaterializer(
            new NotificationIntentRepository(context, mutationLock, clock), unit, mutationLock,
            new PrivacyErasureStateRepository(context));
        var capture = new ModerationNoticeCaptureRepository(context, privacy, sourceOwner, selections,
            new ModerationNoticeRecipientRepository(context), new ModerationNoticeNativeMaterializer(
                new DefaultNotificationOwnershipResolver(Options.Create(new NotificationRoutingOptions())),
                new NotificationPreferenceResolver(context), materializer), clock, participant);
        var service = new ModerationOperationService(new ModerationStore(context, participant, clock, privacy, capture),
            participant, new ModerationEffectPlanner(), clock);

        var result = await service.ExecuteAsync(prepared.Operation);

        await Assert.That(checkpoint.CompletePrimaryFlushes).IsEqualTo(1);
        await Assert.That(checkpoint.PrivacyOwned).IsEqualTo(colocated);
        await Assert.That(checkpoint.PrimaryCommits + checkpoint.PrivacyCommits).IsEqualTo(loss == 0 ? 1 : 0);
        if (loss >= 4) await Assert.That(checkpoint.DisclosureFlushes).IsEqualTo(1);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await Assert.That(context.IdentityFenceOwnsTransaction).IsFalse();
        await using var inspect = Read(database);
        var decision = await inspect.ModerationDecisions.SingleAsync(row => row.Id == prepared.Operation.DecisionId);
        if (loss != 0)
        {
            await Assert.That(result.Outcome == ModerationOperationOutcome.Known).IsFalse();
            await Assert.That(decision.Revision).IsEqualTo(1L);
            await Assert.That(decision.State).IsEqualTo(ModerationDecisionState.Proposed);
            await Assert.That(await inspect.ModerationOperationReceipts.AnyAsync(row => row.Id == prepared.Operation.OperationId)).IsFalse();
            await Assert.That(await inspect.ModerationEffectIntents.AnyAsync(row => row.Id == prepared.Operation.EffectId)).IsFalse();
            await Assert.That(await inspect.ModerationNoticeSources.AnyAsync(row => row.EffectId == prepared.Operation.EffectId)).IsFalse();
            await Assert.That(await inspect.ModerationNotices.AnyAsync(row => row.EffectId == prepared.Operation.EffectId)).IsFalse();
            await Assert.That(await inspect.NotificationIntents.CountAsync()).IsEqualTo(0);
            await Assert.That(await inspect.OutboxMessages.CountAsync()).IsEqualTo(0);
            await Assert.That((await inspect.EventRoleAssignments.SingleAsync(row => row.Id == prepared.Representative.AssignmentId)).RoleId)
                .IsEqualTo((int)RoleEnum.EventOwner);
            return;
        }
        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Known);
        await Assert.That(decision.Revision).IsEqualTo(2L);
        await Assert.That(decision.State).IsEqualTo(ModerationDecisionState.AwaitingApproval);
        var source = await inspect.ModerationNoticeSources.SingleAsync(row => row.EffectId == prepared.Operation.EffectId);
        var snapshot = System.Text.Json.JsonSerializer.Deserialize<ModerationNoticeCaptureSnapshot>(source.SnapshotJson)!;
        await Assert.That(snapshot.HasNoSubstantiveWork).IsFalse();
        await Assert.That(snapshot.ContentRepresentatives.Single()).IsEqualTo(prepared.Representative);
        await Assert.That(source.RetainUntil).IsEqualTo(Now.AddYears(1));
        await Assert.That(snapshot.ValidUntil <= prepared.RepresentativeUntil).IsTrue();
        await Assert.That(await inspect.ModerationNotices.AnyAsync(row => row.EffectId == prepared.Operation.EffectId
            && row.Audience == ModerationNoticeAudience.ContentRepresentative
            && row.RecipientSubjectId == prepared.Representative.HumanId)).IsTrue();
        await Assert.That(await inspect.ModerationNotices.AnyAsync(row => row.EffectId == prepared.Operation.EffectId
            && row.Audience == ModerationNoticeAudience.Subject && row.RecipientSubjectId == prepared.Operation.Subject.Id)).IsTrue();
        await Assert.That(await inspect.ModerationNotices.AnyAsync(row => row.EffectId == prepared.Operation.EffectId
            && row.Audience == ModerationNoticeAudience.Reviewer && row.RecipientSubjectId == database.ReviewerId)).IsTrue();
        await Assert.That(await inspect.ModerationSanctions.CountAsync()).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Content_urgency_cannot_replace_the_cases_original_Resource_scope(bool eventScope)
    {
        await using var database = await SourceDatabase.CreateAsync();
        var resource = await ModerationResourceGenerationWriterTests.SeedAsync(database);
        Guid eventId;
        await using (var read = database.Context())
            eventId = await read.EventResources.Where(row => row.Id == resource).Select(row => row.EventId).SingleAsync();
        var scope = AuthorityScope.Resource(database.Scope, resource);
        var opened = database.OpenCase(scope) with { SubjectKind = ModerationSubjectKind.Content, SubjectId = eventId };
        await WriteContentSourceAsync(database, database.AdministratorId, opened);
        var wrongScope = eventScope ? AuthorityScope.Event(database.Scope, eventId)
            : AuthorityScope.Resource(database.Scope, Guid.CreateVersion7());
        var request = new QualifyModerationCaseUrgency
        {
            OperationId = Guid.CreateVersion7(),
            Target = wrongScope,
            ExpectedQualificationRevision = 2,
            EvidenceId = Guid.CreateVersion7(),
            CaseId = opened.CaseId,
            ExpectedCaseRevision = 1,
            Justification = "A different target cannot borrow this Content case.",
            ValidUntil = Now.AddMinutes(2)
        };
        var outcome = await ExecuteContentSourceAsync(database, database.UrgencyAssessorId, request);
        await Assert.That(outcome.IsSuccess).IsFalse();
        await using var inspect = Read(database);
        await Assert.That(await inspect.ModerationQualificationEvidence.CountAsync()).IsEqualTo(0);
        await Assert.That(await inspect.ModerationQualificationMutationReceipts.AnyAsync(row => row.Id == request.OperationId)).IsFalse();
        await Assert.That((await inspect.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(2L);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Resource_Content_prerequisites_cannot_acquire_replacement_Actors_after_Tenant(bool organizer)
    {
        await using var database = await SourceDatabase.CreateAsync();
        var resource = await ModerationResourceGenerationWriterTests.SeedAsync(database);
        Guid eventId;
        Guid publisher;
        var replacement = Guid.CreateVersion7();
        await using (var seed = database.Context())
        {
            var ownerId = await seed.EventResources.Where(row => row.Id == resource).Select(row => row.EventId).SingleAsync();
            var owner = await seed.Events.SingleAsync(row => row.Id == ownerId);
            eventId = owner.Id;
            publisher = owner.ActorId;
            seed.Actors.Add(new Actor
            {
                Id = replacement,
                ActorTypeId = (int)ActorTypeEnum.User,
                ActorType = null!,
                UserId = database.ReviewerId,
                Pii = new ActorPii { ActorId = replacement, DisplayName = "Replacement prerequisite parent" }
            });
            await seed.SaveChangesAsync();
        }
        var scope = AuthorityScope.Resource(database.Scope, resource);
        var opened = database.OpenCase(scope) with { SubjectKind = ModerationSubjectKind.Content, SubjectId = eventId };
        await WriteContentSourceAsync(database, database.AdministratorId, opened);
        var change = new ModerationEventGenerationWriterTests.ReplaceOwnerBeforeTenant(eventId, replacement, organizer);
        await using var context = database.Context(change);
        var (admin, machine) = ContentIdentity(context, database.UrgencyAssessorId);
        await database.Privacy.Authority(context, new CommitClock(database)).ExecuteSerializedAsync(async token =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, token);
            var acquired = await new ModerationQualificationAuthority(context, admin, machine).AcquireAsync(new QualifyModerationCaseUrgency
            {
                OperationId = Guid.CreateVersion7(),
                Target = scope,
                ExpectedQualificationRevision = 2,
                EvidenceId = Guid.CreateVersion7(),
                CaseId = opened.CaseId,
                ExpectedCaseRevision = 1,
                Justification = "A late native parent cannot replace the originally fenced Content ancestry.",
                ValidUntil = Now.AddMinutes(2)
            }, token);
            await Assert.That(acquired).IsNull();
            return true;
        }, default);
        await Assert.That(change.Observed).IsTrue();
        await Assert.That(change.ActorAnchorsBeforeTenant.Contains(publisher)).IsTrue();
        await Assert.That(change.ActorAnchors.Contains(replacement)).IsFalse();
        await using var inspect = Read(database);
        var current = await inspect.Events.SingleAsync(row => row.Id == eventId);
        await Assert.That(current.ActorId).IsEqualTo(publisher);
        await Assert.That(current.OrganizerActorId).IsNull();
        await Assert.That(await inspect.ModerationQualificationEvidence.AnyAsync()).IsFalse();
        await Assert.That((await inspect.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(2L);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Resource_Content_terminal_acquisition_borrows_the_original_parent_plan_even_when_missing(bool missingPlan)
    {
        await using var database = await SourceDatabase.CreateAsync();
        var prepared = await PrepareSubstantiveContentAsync(database);
        ApproveModerationNoticeDisclosureSource request;
        await using (var read = Read(database))
            request = new ApproveModerationNoticeDisclosureSource
            {
                OperationId = Guid.CreateVersion7(),
                Target = prepared.Operation.Target,
                ExpectedQualificationRevision = (await read.ModerationQualificationEpochs.SingleAsync()).Revision,
                Intent = (await read.ModerationNoticeDisclosureApprovals.SingleAsync(row => row.EffectId == prepared.Operation.EffectId)).Intent
            };
        var order = new DisclosureSourceOrder(prepared.ReplacementActorId, 0) { Armed = true };
        await using var context = database.Context(order);
        var (admin, machine) = ContentIdentity(context, database.AdministratorId);
        await database.Privacy.Authority(context, new CommitClock(database)).ExecuteSerializedAsync(async token =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, token);
            var owner = new ModerationQualificationAuthority(context, admin, machine);
            var original = await owner.AcquireAsync(request, token);
            await Assert.That(original).IsNotNull();
            await context.Events.Where(row => row.Id == prepared.Operation.Subject.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ActorId, prepared.ReplacementActorId), token);
            var borrowed = missingPlan ? original! with { ContentTargetCase = null } : original!;
            await Assert.That(await owner.AcquireAsync(request, borrowed, token)).IsNull();
            return true;
        }, default);
        await Assert.That(order.ObservedTenant).IsTrue();
        await Assert.That(order.UnplannedSources).IsEqualTo(0);
        await Assert.That(order.ActorsBeforeTenant.Contains(prepared.ReplacementActorId)).IsFalse();
        await using var inspect = Read(database);
        await Assert.That((await inspect.Events.SingleAsync(row => row.Id == prepared.Operation.Subject.Id)).ActorId)
            .IsNotEqualTo(prepared.ReplacementActorId);
        await Assert.That(await inspect.ModerationQualificationMutationReceipts.AnyAsync(row => row.Id == request.OperationId)).IsFalse();
    }

    private static async Task<SubstantiveContent> PrepareSubstantiveContentAsync(SourceDatabase database)
    {
        var resourceId = await ModerationResourceGenerationWriterTests.SeedAsync(database);
        Guid eventId;
        EventRoleAssignment representative;
        var unrelatedEventId = Guid.CreateVersion7();
        var replacementActorId = Guid.CreateVersion7();
        await using (var seed = database.Context())
        {
            eventId = await seed.EventResources.Where(row => row.Id == resourceId).Select(row => row.EventId).SingleAsync();
            representative = EventRoleAssignment.Create(database.TenantId, eventId, database.SubjectId,
                (int)RoleEnum.EventOwner, EventRoleAssignmentStatus.Active, Now.AddHours(-1).UtcDateTime,
                Now.AddMinutes(5).UtcDateTime, database.AdministratorId);
            seed.EventRoleAssignments.Add(representative);
            var original = await seed.Events.SingleAsync(row => row.Id == eventId);
            seed.Events.Add(new Explore.Domain.Event(EventStatusEnum.Draft)
            {
                Id = unrelatedEventId,
                TenantId = database.TenantId,
                Tenant = original.Tenant,
                ActorId = original.ActorId,
                Actor = original.Actor,
                Title = "Independent disclosure dependency",
                SubmittedByUserId = database.SubjectId,
                EventProvenanceTypeId = original.EventProvenanceTypeId,
                VisibilityTypeId = original.VisibilityTypeId,
                VisibilityType = null!,
                EventFormatId = original.EventFormatId,
                EventFormat = null!,
                EventStatus = null!
            });
            seed.Actors.Add(new Actor
            {
                Id = replacementActorId,
                ActorTypeId = (int)ActorTypeEnum.User,
                ActorType = null!,
                UserId = database.UrgencyAssessorId,
                Pii = new ActorPii { ActorId = replacementActorId, DisplayName = "Replacement capture parent" }
            });
            await seed.SaveChangesAsync();
        }
        var scope = AuthorityScope.Resource(database.Scope, resourceId);
        var generationId = Guid.CreateVersion7();
        await WriteContentSourceAsync(database, database.AdministratorId, new RecordModerationResourceGenerationSource
        {
            OperationId = Guid.CreateVersion7(),
            Target = database.Scope,
            ExpectedQualificationRevision = 1,
            SourceId = generationId,
            ResourceId = resourceId,
            ExpectedResourceRevision = 0,
            ValidUntil = Now.AddMinutes(10)
        });
        var opened = database.OpenCase(scope) with
        { ExpectedQualificationRevision = 2, SubjectKind = ModerationSubjectKind.Content, SubjectId = eventId };
        await WriteContentSourceAsync(database, database.AdministratorId, opened);
        var publication = database.Publication(3, ModerationReferenceProfileId.CFG09);
        publication = publication with
        {
            Proposed = publication.Proposed with
            {
                Profiles = [publication.Proposed.Profiles.Single() with
        { ScopeKinds = [ModerationScopeKind.Resource], SubjectKinds = [ModerationSubjectKind.Content] }]
            }
        };
        await Assert.That((await database.PublishAsync(database.AdministratorId, publication)).Outcome)
            .IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        var profile = publication.Proposed.Profiles.Single();
        var proceduralId = Guid.CreateVersion7();
        await WriteContentSourceAsync(database, database.AdministratorId, new PublishModerationQualificationPolicySource
        {
            OperationId = Guid.CreateVersion7(),
            Target = scope,
            ExpectedQualificationRevision = 4,
            SourceId = proceduralId,
            ExpectedSourceRevision = 0,
            CaseId = opened.CaseId,
            ExpectedCaseRevision = 1,
            MaximumReviewerLifetime = TimeSpan.FromMinutes(30),
            MaximumUrgencyLifetime = TimeSpan.FromMinutes(5),
            ValidUntil = Now.AddHours(1)
        });
        await WriteContentSourceAsync(database, database.UrgencyAssessorId, new AdoptModerationQualificationPolicySource
        {
            OperationId = Guid.CreateVersion7(),
            Target = scope,
            ExpectedQualificationRevision = 5,
            SourceId = proceduralId,
            ExpectedSourceRevision = 1,
            ExpectedWriterGeneration = 5,
            AdoptionId = Guid.CreateVersion7(),
            CaseId = opened.CaseId,
            ExpectedCaseRevision = 1,
            ValidUntil = Now.AddHours(1)
        });
        var reviewerId = Guid.CreateVersion7();
        await WriteContentSourceAsync(database, database.AdministratorId, new AssignModerationReviewerSource
        {
            OperationId = Guid.CreateVersion7(),
            Target = scope,
            ExpectedQualificationRevision = 6,
            AssignmentId = reviewerId,
            CaseId = opened.CaseId,
            ExpectedCaseRevision = 1,
            ReviewerHumanId = database.ReviewerId
        });
        await WriteContentSourceAsync(database, database.AdministratorId, new QualifyModerationReviewerController
        {
            OperationId = Guid.CreateVersion7(),
            Target = scope,
            ExpectedQualificationRevision = 7,
            AssignmentId = reviewerId,
            ExpectedAssignmentRevision = 1,
            ControllerHumanId = database.ReviewerId,
            EvidenceId = Guid.CreateVersion7(),
            Justification = "Independent canonical reviewer control.",
            QualifiedUntil = Now.AddMinutes(5)
        });
        await WriteContentSourceAsync(database, database.AdministratorId, new AssessModerationReviewerConflict
        {
            OperationId = Guid.CreateVersion7(),
            Target = scope,
            ExpectedQualificationRevision = 8,
            AssignmentId = reviewerId,
            ExpectedAssignmentRevision = 2,
            State = ModerationReviewerConflictState.Independent,
            EvidenceId = Guid.CreateVersion7(),
            Justification = "Case-specific independent reviewer and controller."
        });
        var evidenceId = Guid.CreateVersion7();
        await WriteContentSourceAsync(database, database.UrgencyAssessorId, new QualifyModerationCaseUrgency
        {
            OperationId = Guid.CreateVersion7(),
            Target = scope,
            ExpectedQualificationRevision = 9,
            EvidenceId = evidenceId,
            CaseId = opened.CaseId,
            ExpectedCaseRevision = 1,
            Justification = "An independent native assessor qualifies the bounded urgent finding.",
            ValidUntil = Now.AddMinutes(4)
        });
        var findingId = Guid.CreateVersion7();
        await WriteContentSourceAsync(database, database.ReviewerId, new FinalizeModerationFindingSource
        {
            OperationId = Guid.CreateVersion7(),
            Target = scope,
            ExpectedQualificationRevision = 10,
            FindingId = findingId,
            CaseId = opened.CaseId,
            ExpectedCaseRevision = 1,
            PolicyId = publication.Proposed.PolicyId,
            ExpectedPolicyRevision = 1,
            Rule = profile.Rule,
            Reason = profile.Reason,
            EvidenceId = evidenceId,
            Justification = "The independent reviewer finalizes the actual qualified urgency finding."
        });
        // Only the Proposed decision is a historical starting boundary; finding and every source above are native writes.
        var decision = ModerationDecision.Propose(Guid.CreateVersion7(), opened.CaseId,
            SubjectReference.Create(ModerationSubjectKind.Content, eventId, database.Scope),
            SubjectReference.Create(ModerationSubjectKind.Human, database.AdministratorId, database.Instance),
            database.Scope, scope, profile.Rule, [ModerationCapability.ReadResource], Now, null, Now.AddHours(1),
            profile.RequiredApprovals, Now);
        await using (var seed = Read(database)) { seed.ModerationDecisions.Add(decision); await seed.SaveChangesAsync(); }
        var effectId = Guid.CreateVersion7();
        var selectionId = Guid.CreateVersion7();
        var target = new ModerationPolicyTarget(scope, ModerationSubjectKind.Content, eventId, resourceId, 1, "resource.read", null);
        var contribution = new ModerationPolicyContribution(decision.Id, decision.Rule, scope, decision.Subject.Kind,
            decision.Subject.Id, Now, null, profile.Effects.Select(effect => new ModerationPolicyEffectTarget(effect, target)).ToImmutableArray(),
            [new(null, target)]);
        await WriteContentSourceAsync(database, database.AdministratorId, new RecordModerationContributionSelectionSource
        {
            OperationId = Guid.CreateVersion7(),
            Target = scope,
            ExpectedQualificationRevision = 11,
            SelectionId = selectionId,
            EffectId = effectId,
            CaseId = opened.CaseId,
            ExpectedCaseRevision = 1,
            DecisionId = decision.Id,
            ExpectedDecisionRevision = 2,
            PolicyId = publication.Proposed.PolicyId,
            ExpectedPolicyRevision = 1,
            PublicationId = publication.Proposed.PublicationId,
            ProfileId = profile.Id,
            Contribution = contribution,
            ValidUntil = Now.AddMinutes(4)
        });
        long epoch = 12;
        foreach (var binding in ModerationContributionSelectionProjection.Bindings(selectionId, contribution))
            await WriteContentSourceAsync(database, database.UrgencyAssessorId, new AdoptModerationResourceTargetSource
            {
                OperationId = Guid.CreateVersion7(),
                Target = scope,
                ExpectedQualificationRevision = epoch++,
                AdoptionId = Guid.CreateVersion7(),
                SelectionId = selectionId,
                BindingOrdinal = binding.Ordinal,
                GenerationId = generationId,
                ExpectedGenerationRevision = 1,
                CaseId = opened.CaseId,
                ExpectedCaseRevision = 1,
                ValidUntil = Now.AddMinutes(4)
            });
        var contentRepresentative = new ModerationNoticeContentRepresentative(representative.Id, representative.Version, database.SubjectId);
        await WriteContentSourceAsync(database, database.AdministratorId, new ApproveModerationNoticeDisclosureSource
        {
            OperationId = Guid.CreateVersion7(),
            Target = scope,
            ExpectedQualificationRevision = epoch,
            Intent = new(Guid.CreateVersion7(), effectId, decision.Id, 2, opened.CaseId, 1,
                publication.Proposed.PolicyId, 1, publication.Proposed.PublicationId, profile.Id, decision.Id,
                ModerationNoticeSnapshotFactory.SelectionDigest(contribution),
                new(publication.Proposed.Classes.Single().Meaning), new(publication.Proposed.Reasons.Single().Meaning),
                [new("The actual native finding concerns this Content, not its representative.")], new("Protected response."),
                new("Independent review."), new("A human reviewer made the finding."), new("Original finite custody."),
                Now.AddMinutes(4), Now.AddYears(1))
            { ContentRepresentatives = [contentRepresentative] }
        });
        return new(ModerationOperation.Transition(ModerationOperationKind.AcceptFinding, decision.Subject, scope,
            Guid.CreateVersion7(), effectId, decision.Id, 1, 1, findingId), contentRepresentative, Now.AddMinutes(5),
            unrelatedEventId, replacementActorId);
    }

    private static async Task WriteContentSourceAsync(SourceDatabase database, Guid actor, ModerationQualificationMutation mutation) =>
        await Assert.That((await ExecuteContentSourceAsync(database, actor, mutation)).Outcome)
            .IsEqualTo(ModerationQualificationWriteOutcome.Known);

    private static async Task<ModerationQualificationWriteResult> ExecuteContentSourceAsync(
        SourceDatabase database, Guid actor, ModerationQualificationMutation mutation)
    {
        await using var context = database.Context();
        var clock = new CommitClock(database);
        var (admin, machine) = ContentIdentity(context, actor);
        var publications = new ModerationPolicyPublicationRepository(context, admin, machine, database.Inventory, null, clock);
        var activation = new ModerationNativeActivationQualification(context, database.Inventory, null,
            new ModerationQualificationPolicySourceOwner(context, clock), clock);
        return await new ModerationQualificationRepository(context, admin, machine, publications, activation, clock,
            database.Privacy.Authority(context, clock)).ExecuteAsync(mutation);
    }

    private static (AdminContext Admin, MachinePrincipalAccessor Machine) ContentIdentity(ExploreDbContext context, Guid actor)
    {
        var http = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, actor.ToString())], "native-content-capture")) }
        };
        return (new AdminContext(http, new AdministrativeAuthorityRepository(new SecurityTestSupport.AdministrativeAuthorityTestFactory(context)),
            new OrganizationMemberRepository(context), new GroupMemberRepository(context), new ExternalLoginAuthenticationRepository(new SecurityTestSupport.AdministrativeAuthorityTestFactory(context), null),
            NullLogger<AdminContext>.Instance), new MachinePrincipalAccessor(http));
    }

    private sealed record SubstantiveContent(ModerationOperation Operation,
        ModerationNoticeContentRepresentative Representative, DateTimeOffset RepresentativeUntil,
        Guid UnrelatedEventId, Guid ReplacementActorId);

    private sealed class ContentCaptureCheckpoint(SourceDatabase database, SubstantiveContent prepared, int loss)
    {
        internal int CompletePrimaryFlushes;
        internal int DisclosureFlushes;
        internal int PrimaryCommits;
        internal int PrivacyCommits;
        internal bool PrivacyOwned;
        internal DateTimeOffset CaptureDeadline;

        internal async Task ChangeAsync(ExploreDbContext context, int change, CancellationToken token)
        {
            if (change == 1)
                await context.EventRoleAssignments.Where(row => row.Id == prepared.Representative.AssignmentId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.RoleId, (int)RoleEnum.RegistrationManager), token);
            else if (change == 2)
                await context.Events.Where(row => row.Id == prepared.Operation.Subject.Id)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ActorId, prepared.ReplacementActorId), token);
            else if (change == 3) database.EvaluationTime = CaptureDeadline;
        }

        internal async Task ObserveSavedAsync(ExploreDbContext context, CancellationToken token)
        {
            if (CompletePrimaryFlushes != 0 || context.ChangeTracker.HasChanges()
                || !context.ModerationOperationReceipts.Local.Any(row => row.Id == prepared.Operation.OperationId)
                || !context.ModerationNoticeSources.Local.Any(row => row.EffectId == prepared.Operation.EffectId)
                || !await context.ModerationNotices.AnyAsync(row => row.EffectId == prepared.Operation.EffectId, token)) return;
            CompletePrimaryFlushes++;
            PrivacyOwned = context.IdentityFenceOwnsTransaction;
            var source = await context.ModerationNoticeSources.AsNoTracking()
                .SingleAsync(row => row.EffectId == prepared.Operation.EffectId, token);
            CaptureDeadline = System.Text.Json.JsonSerializer.Deserialize<ModerationNoticeCaptureSnapshot>(source.SnapshotJson)!.ValidUntil!.Value;
            if (loss < 4) await ChangeAsync(context, loss, token);
            else
            {
                // Enlist a real, separate Event source so only the outer privacy owner flushes it at the final boundary.
                (await context.Events.SingleAsync(row => row.Id == prepared.UnrelatedEventId, token)).Title = "Prepared disclosure dependency";
                await context.SaveChangesAsync(token);
            }
        }

        internal async Task ObserveCommandAsync(DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken token)
        {
            if (loss < 4 || CompletePrimaryFlushes == 0 || DisclosureFlushes != 0
                || eventData.Context is not ExploreDbContext context) return;
            var entity = context.Model.FindEntityType(typeof(EventDiscoveryRevision))!;
            var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            var sql = context.GetService<ISqlGenerationHelper>();
            var column = sql.DelimitIdentifier(entity.FindProperty(nameof(EventDiscoveryRevision.DisclosureEpoch))!.GetColumnName(table)!);
            if (command.Parameters.Count != 3
                || !command.CommandText.StartsWith($"UPDATE {sql.DelimitIdentifier(table.Name, table.Schema)} SET {column} = ", StringComparison.Ordinal)) return;
            if (result != 1 || !context.IdentityFenceOwnsTransaction || context.Database.CurrentTransaction is null
                || PrimaryCommits != 0 || PrivacyCommits != 0)
                throw new InvalidOperationException("The complete captured graph must remain inside the actual privacy-owned transaction.");
            DisclosureFlushes++;
            await ChangeAsync(context, loss == 4 ? 1 : 3, token);
        }
    }

    private sealed class ObserveContentCapture(ContentCaptureCheckpoint checkpoint) : SaveChangesInterceptor
    {
        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result,
            CancellationToken cancellationToken = default)
        {
            await checkpoint.ObserveSavedAsync((ExploreDbContext)data.Context!, cancellationToken);
            return result;
        }
    }

    private sealed class ContentCaptureFlush(ContentCaptureCheckpoint checkpoint) : DbCommandInterceptor
    {
        public override async ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData data,
            int result, CancellationToken cancellationToken = default)
        {
            await checkpoint.ObserveCommandAsync(command, data, result, cancellationToken);
            return result;
        }
    }

    private sealed class ContentCaptureCommit(ContentCaptureCheckpoint checkpoint) : DbTransactionInterceptor
    {
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData data,
            CancellationToken cancellationToken = default)
        {
            if (data.Context is ExploreDbContext) checkpoint.PrimaryCommits++;
            else checkpoint.PrivacyCommits++;
            return Task.CompletedTask;
        }
    }
}
