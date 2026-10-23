using System.Data;
using System.Security.Claims;
using System.Text.Json;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Notifications;
using Explore.Application.Services;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Identity;
using Explore.Persistence;
using Explore.Persistence.Privacy.ErasureAuthority.Repositories;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using ISLAMU.Moderation.Application;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SourceDatabase = Event.Persistence.IntegrationTests.Moderation.ModerationQualificationWriterTests.SourceDatabase;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationNoticeReadTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    internal sealed partial class NoticeDatabase(
        SourceDatabase database, ModerationNoticePrivacyFixture privacy) : IAsyncDisposable
    {
        public SourceDatabase Database { get; } = database;
        internal ModerationNoticePrivacyFixture Privacy { get; } = privacy;
        public TestClock Clock { get; } = new();
        public Guid SubjectNoticeId { get; private set; }
        public Guid ReviewerNoticeId { get; private set; }
        public Guid DecisionId { get; private set; }
        public Guid EffectId { get; private set; }
        public ModerationNoticeDisclosureApproval Approval { get; private set; } = null!;
        public string SnapshotHash { get; private set; } = string.Empty;
        public string PrivateJustification { get; } = "Private finding custody statement excluded from subject disclosure.";

        public static async Task<NoticeDatabase> CreateAsync(
            string? substitution = null, bool content = false, DateTimeOffset? representativeUntil = null,
            RoleEnum representativeRole = RoleEnum.EventOwner)
        {
            var database = await SourceDatabase.CreateAsync();
            var fixture = new NoticeDatabase(database, await ModerationNoticePrivacyFixture.CreateAsync());
            var nativeScope = database.Scope;
            var subjectId = database.SubjectId;
            ModerationNoticeContentRepresentative? representative = null;
            if (content)
            {
                var resourceId = await ModerationResourceGenerationWriterTests.SeedAsync(database);
                nativeScope = AuthorityScope.Resource(database.Scope, resourceId);
                await using var native = database.Context();
                subjectId = await native.EventResources.Where(row => row.Id == resourceId).Select(row => row.EventId).SingleAsync();
                var assignment = EventRoleAssignment.Create(database.TenantId, subjectId, database.SubjectId,
                    (int)representativeRole, EventRoleAssignmentStatus.Active,
                    Now.AddMinutes(-1).UtcDateTime, (representativeUntil ?? Now.AddDays(1)).UtcDateTime, database.AdministratorId);
                native.EventRoleAssignments.Add(assignment);
                await native.SaveChangesAsync();
                representative = new(assignment.Id, assignment.Version, assignment.UserId);
            }
            var publicationRequest = database.Publication(1,
                content ? ModerationReferenceProfileId.CFG09 : ModerationReferenceProfileId.CFG06);
            var publication = await database.PublishAsync(database.AdministratorId, publicationRequest);
            if (publication.Outcome != ModerationPolicyPublicationOutcome.Known)
                throw new InvalidOperationException("The notice fixture requires a real native publication.");
            var subject = SubjectReference.Create(content ? ModerationSubjectKind.Content : ModerationSubjectKind.Human,
                subjectId, content ? database.Scope : database.Instance);
            var issuer = SubjectReference.Create(ModerationSubjectKind.Human, database.AdministratorId, database.Instance);
            var reviewerSubject = SubjectReference.Create(ModerationSubjectKind.Human, database.ReviewerId, database.Instance);
            var allegation = ModerationCase.Open(Guid.CreateVersion7(), subject, nativeScope, Guid.CreateVersion7(), Now);
            var profile = publicationRequest.Proposed.Profiles.Single();
            var rule = profile.Rule;
            var finding = ModerationFinding.Finalize(Guid.CreateVersion7(), allegation, 1,
                allegation.Scope, null, null, rule, rule, rule, rule,
                reviewerSubject, ModerationFindingEvidenceReference.Response(Guid.CreateVersion7(), allegation.Id, 1, Now),
                fixture.PrivateJustification, Now);
            var decision = ModerationDecision.Propose(Guid.CreateVersion7(), allegation.Id, subject, issuer,
                database.Instance, nativeScope, rule,
                [content ? ModerationCapability.ReadResource : ModerationCapability.Publish], Now, Now.AddDays(1), null, 1, Now);
            decision.RecordResponse(1, Now);
            decision.AcceptFinding(finding.Id, 2, Now);
            decision.Approve(reviewerSubject, database.Scope, 3, Now);
            var sanction = decision.Activate(Guid.CreateVersion7(), 4, Now);
            var effect = ModerationEffectIntent.Create(Guid.CreateVersion7(), Guid.CreateVersion7(),
                decision.Id, decision.Revision, sanction.Id, sanction.Revision,
                database.Scope.TrustDomainId, (int)nativeScope.Kind, nativeScope.Id,
                (int)subject.Kind, subject.Id, 1, 1, 2, Now, null, null);
            fixture.DecisionId = decision.Id;
            fixture.EffectId = effect.Id;
            var safe = new ModerationNoticeSafeText("Original independently approved explanation.");
            var disclosure = new ModerationNoticeDisclosureIntent(Guid.CreateVersion7(), effect.Id, decision.Id,
                decision.Revision, allegation.Id, 1, publicationRequest.Proposed.PolicyId, 1, publication.Publication!.Id,
                profile.Id, Guid.CreateVersion7(), new string('a', 64),
                safe, safe, [safe], safe, safe, safe, safe, Now.AddMinutes(1), Now.AddMonths(7))
            {
                ContentRepresentatives = representative is null ? [] : [representative]
            };
            fixture.Approval = ModerationNoticeDisclosureApproval.Approve(disclosure, database.HostId,
                database.Scope.TrustDomainId, database.TenantId, database.AdministratorId, 1, Now);
            var policy = ModerationQualificationPolicySource.Publish(Guid.CreateVersion7(), database.HostId,
                database.Scope.TrustDomainId, database.TenantId, allegation.Id, 1, 2,
                TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(5), database.AdministratorId, Now, Now.AddHours(1));
            var adoption = ModerationQualificationPolicyAdoption.Adopt(
                Guid.CreateVersion7(), policy, database.UrgencyAssessorId, Now, Now.AddHours(1));
            var reviewer = ModerationReviewerAssignment.Assign(
                Guid.CreateVersion7(), allegation, 1, database.ReviewerId, database.AdministratorId, Now);
            var identity = ModerationQualificationEvidence.QualifyController(Guid.CreateVersion7(), allegation.Id,
                database.Scope.TrustDomainId, database.TenantId, database.ReviewerId, database.ReviewerId,
                database.UrgencyAssessorId, "Independent controller qualification.", Now.AddMinutes(30), Now);
            var conflict = ModerationQualificationEvidence.AssessConflict(Guid.CreateVersion7(), allegation.Id,
                database.Scope.TrustDomainId, database.TenantId, database.ReviewerId, database.ReviewerId,
                database.UrgencyAssessorId, (int)ModerationReviewerConflictState.Independent,
                "Independent conflict qualification.", Now.AddMinutes(30), Now);
            reviewer.QualifyController(1, database.ReviewerId, identity.Id, database.UrgencyAssessorId, Now.AddMinutes(30), Now);
            reviewer.RecordConflictAssessment(reviewer.Revision, ModerationReviewerConflictState.Independent,
                conflict.Id, database.UrgencyAssessorId, Now);
            var stamp = new ModerationNoticeSourceStamp(policy.Id, 1, "event.moderation.qualification-policy.v1:2");
            var scope = ModerationNoticeScope.From(nativeScope);
            var origin = ModerationNoticeScope.From(subject.Origin);
            var originalRule = new ModerationNoticeRule(rule.OwnerId, rule.Code, rule.Revision);
            var target = new ModerationNoticeTarget(scope, subject.Kind, subject.Id, Guid.CreateVersion7(),
                1, "event.publish", null, "event.publication", stamp);
            var reference = ModerationReferenceCatalogue.GetProfile(
                content ? ModerationReferenceProfileId.CFG09 : ModerationReferenceProfileId.CFG06);
            var interval = new ModerationReferenceInterval(ModerationReferenceTimingKind.Finite, Now, Now.AddDays(1), null, null);
            var modes = new ModerationConsequenceModes(
                ModerationContentMode.PendingPublicationReview, ModerationManagementMode.CurrentAuthority,
                ModerationResourceReadMode.CurrentAudience, ModerationNewAdmissionMode.CurrentEligibility,
                ModerationExistingAdmissionMode.CurrentEligibility, ModerationPrivateDisclosureMode.CurrentAudience,
                ModerationSettlementMode.NoBookingChange, ModerationCapacityMode.NoSeatChange,
                ModerationCredentialMode.CurrentCredentialLifecycle, ModerationNotificationMode.DurableSafeSubjectAndAdmin,
                ModerationRecognitionMode.NoCaptureFromThisControl, ModerationPendingWorkMode.CurrentPublicationApprovalRequired,
                ModerationRestorationMode.RecomputeCurrentRightsOnly);
            var notice = new ModerationNoticeSnapshot(1, effect.Id, effect.OperationId, decision.Id,
                decision.Revision, decision.State, sanction.Id, sanction.Revision, sanction.State, null, null, 1,
                scope, subject.Kind, subject.Id, origin, disclosure.PolicyId, 1, disclosure.PublicationId, 1,
                disclosure.ProfileId, reference.Id, reference.Revision, stamp, disclosure.ApprovalId, 1,
                disclosure.ContributionId, originalRule, originalRule, safe, safe, [safe], safe, safe, safe, safe,
                [new(reference.Effects[0], target, interval)], [new(null, null, target, interval, modes)], Now, Now.AddMonths(7), 6);
            var original = new ModerationNoticeOriginalDecision(decision.Id, decision.Revision, allegation.Id,
                decision.State, scope, subject.Kind, subject.Id, origin, originalRule, decision.CapabilityMask,
                Now, Now.AddDays(1), null, finding.Id, 1, sanction.Id, null);
            var capture = new ModerationNoticeCaptureSnapshot(2, "event.moderation.capture.v2",
                database.HostId, database.Instance.Parent!.Id, database.Instance.Id, database.TenantId,
                database.EpochId, 2, original, new(finding.Id, 1, finding.State, originalRule), notice,
                [new(reviewer.Id, reviewer.Revision, database.ReviewerId, database.ReviewerId,
                    identity.Id, 1, database.UrgencyAssessorId, conflict.Id, 1,
                    database.UrgencyAssessorId, Now.AddMinutes(30))], Now.AddMinutes(1), stamp)
            {
                ContentRepresentatives = disclosure.ContentRepresentatives
            };
            if (substitution == "tenant")
                capture = capture with { WriterTenantId = Guid.CreateVersion7() };
            if (substitution == "origin")
                capture = capture with { Decision = original with { SubjectOrigin = scope } };
            var source = ModerationNoticeSource.Capture(effect, database.TenantId, JsonSerializer.Serialize(capture), notice.RetainUntil);
            fixture.SnapshotHash = source.SnapshotHash;
            var subjectNotice = ModerationNotice.Create(Guid.CreateVersion7(), source, ModerationNoticeAudience.Subject,
                (int)subject.Kind, subject.Id, null, content ? ModerationNoticeDeliveryDisposition.NoHumanProjection
                    : ModerationNoticeDeliveryDisposition.NativeRoutingUnavailable);
            var representativeNotice = content ? ModerationNotice.Create(Guid.CreateVersion7(), source,
                ModerationNoticeAudience.ContentRepresentative, 1, database.SubjectId, null,
                ModerationNoticeDeliveryDisposition.NativeRoutingUnavailable) : null;
            var reviewerNotice = ModerationNotice.Create(Guid.CreateVersion7(), source, ModerationNoticeAudience.Reviewer,
                1, database.ReviewerId, null, ModerationNoticeDeliveryDisposition.NativeRoutingUnavailable);
            fixture.SubjectNoticeId = representativeNotice?.Id ?? subjectNotice.Id;
            fixture.ReviewerNoticeId = reviewerNotice.Id;
            await using var context = database.Context();
            context.AddRange(allegation, finding, decision, sanction, effect, fixture.Approval,
                policy, adoption, reviewer, identity, conflict, source, subjectNotice, reviewerNotice,
                ModerationQualificationMutationReceipt.Complete(Guid.CreateVersion7(), new string('A', 64),
                    database.HostId, database.Scope.TrustDomainId, database.TenantId,
                    ModerationQualificationSourceKind.QualificationPolicy, policy.Id, database.AdministratorId, 1, 2, Now),
                ModerationQualificationMutationReceipt.Complete(Guid.CreateVersion7(), new string('B', 64),
                    database.HostId, database.Scope.TrustDomainId, database.TenantId,
                    ModerationQualificationSourceKind.QualificationPolicyAdoption, adoption.Id, database.UrgencyAssessorId, 1, 3, Now));
            if (representativeNotice is not null) context.Add(representativeNotice);
            await context.SaveChangesAsync();
            return fixture;
        }

        public async Task MutateAsync(Func<ExploreDbContext, Task> change)
        {
            await using var context = Database.Context();
            context.ModerationTrustDomainId = Database.Scope.TrustDomainId;
            await change(context);
            await context.SaveChangesAsync();
        }
        public async ValueTask DisposeAsync()
        {
            await Privacy.DisposeAsync();
            await Database.DisposeAsync();
        }
    }

    private static async Task<(CorrectionApprovalPending Pending, GrantModerationAdjudicatedCaseRead Grant)>
        ApprovedCorrectionPreparationAsync(NoticeDatabase fixture, ModerationNoticePrivacyFixture? privacy = null,
            bool shortGrant = false, bool issueGrant = true)
    {
        var pending = await PrepareCorrectionApprovalAsync(fixture);
        var approved = await WriteCorrectionApprovalAsync(fixture, pending.Request, privacy);
        await Assert.That(approved.Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        await Assert.That(approved.Receipt).IsNotNull();
        var grant = AdjudicatedGrantRequest(fixture, pending.Original) with
        {
            GranteeUserId = fixture.Database.ReviewerId,
            ValidUntil = shortGrant ? fixture.Clock.GetUtcNow().AddSeconds(5) : fixture.Clock.GetUtcNow().AddMinutes(10)
        };
        if (issueGrant)
            await IssueCorrectionPreparationGrantAsync(fixture, grant, privacy);
        return (pending, grant);
    }

    private static async Task IssueCorrectionPreparationGrantAsync(NoticeDatabase fixture,
        GrantModerationAdjudicatedCaseRead request, ModerationNoticePrivacyFixture? privacy = null)
    {
        await using (var writer = fixture.Database.Context())
        {
            var grant = await AdjudicatedGrants(writer, fixture, fixture.Database.AdministratorId, privacy).GrantAsync(request);
            await Assert.That(grant).IsNotNull();
            await Assert.That(grant!.RecipientUserId).IsEqualTo(request.GranteeUserId);
            await Assert.That(grant.IssuerUserId).IsEqualTo(fixture.Database.AdministratorId);
        }
        await using var reader = fixture.Database.Context();
        await Assert.That(await AdjudicatedGrants(reader, fixture, request.GranteeUserId, privacy)
            .ReadCurrentAsync(request.OperationId, request.CaseId, request.ExpectedCaseRevision)).IsNotNull();
    }

    private static ModerationNoticeCorrectionPreparationRequest CorrectionPreparationRequest(
        CorrectionApprovalPending pending, GrantModerationAdjudicatedCaseRead grant) => new(
        pending.Effect.Id, pending.Request.ExpectedPendingSnapshotHash, pending.Request.ExpectedPendingEffectBindingHash,
        pending.Request.Intent.ApprovalId, grant.OperationId, grant.CaseId, grant.ExpectedCaseRevision,
        pending.Original.ReviewerNotice.Id);

    private static async Task<CorrectionPreparationLedger> CorrectionPreparationLedgerAsync(
        NoticeDatabase fixture, CorrectionApprovalPending pending)
    {
        var state = await CorrectionApprovalStateAsync(fixture, pending);
        await using var context = fixture.Database.Context();
        context.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        return new(state,
            JsonSerializer.Serialize(await context.ModerationNoticeDisclosureApprovals.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync()),
            JsonSerializer.Serialize(await context.ModerationQualificationMutationReceipts.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync()),
            JsonSerializer.Serialize(await context.ModerationQualificationEpochs.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync()),
            JsonSerializer.Serialize(await context.Set<ModerationAdjudicatedCaseReadGrant>().AsNoTracking().OrderBy(row => row.Id).ToArrayAsync()),
            JsonSerializer.Serialize(await context.OutboxMessages.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync()),
            JsonSerializer.Serialize(await context.PlatformUserRoles.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync()));
    }

    private sealed record CorrectionPreparationLedger(CorrectionApprovalState State, string Approvals,
        string QualificationReceipts, string Epochs, string Grants, string Outbox, string IssuerRoles);

    private static ModerationNoticeReadRepository CorrectionDisclosureReader(ExploreDbContext context,
        NoticeDatabase fixture, Guid caller, EmbeddedPrivacyErasureAuthorityRepository privacy)
    {
        var (identity, machine) = CorrectionIdentity(context, caller);
        var activation = new ModerationNativeActivationQualification(context, fixture.Database.Inventory, null,
            new ModerationQualificationPolicySourceOwner(context, fixture.Clock), fixture.Clock);
        return new(context, identity, machine, new ModerationNoticeRecipientRepository(context), activation, fixture.Clock, privacy);
    }

    private static async Task AssertCorrectionExplanationAsync(ReadOnlyMemory<byte> bytes, ModerationNoticeDisclosureIntent intent)
    {
        await Assert.That(bytes.IsEmpty).IsFalse();
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        await Assert.That(root.ValueKind).IsEqualTo(JsonValueKind.Object);
        await Assert.That(root.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal)
            .SequenceEqual(new[] { "Automation", "Facts", "Reason", "Recourse", "RemainingActions", "Retention", "Rule" })).IsTrue();
        foreach (var (name, expected) in new[]
        {
            ("Rule", intent.RuleMeaning.Value), ("Reason", intent.ReasonMeaning.Value),
            ("RemainingActions", intent.RemainingActions.Value), ("Recourse", intent.Recourse.Value),
            ("Automation", intent.Automation.Value), ("Retention", intent.Retention.Value)
        })
        {
            await Assert.That(root.GetProperty(name).ValueKind).IsEqualTo(JsonValueKind.String);
            await Assert.That(root.GetProperty(name).GetString()).IsEqualTo(expected);
        }
        var facts = root.GetProperty("Facts");
        await Assert.That(facts.ValueKind).IsEqualTo(JsonValueKind.Array);
        await Assert.That(facts.EnumerateArray().All(fact => fact.ValueKind == JsonValueKind.String)).IsTrue();
        await Assert.That(facts.EnumerateArray().Select(fact => fact.GetString())
            .SequenceEqual(intent.Facts.Select(fact => fact.Value))).IsTrue();
    }

    private static async Task AssertCorrectionDisclosureBufferClearedAsync(ReadOnlyMemory<byte> borrowed)
    {
        await Assert.That(borrowed.IsEmpty).IsFalse();
        await Assert.That(borrowed.ToArray().All(value => value == 0)).IsTrue();
    }

    private static async Task AssertCorrectionDisclosureReleasedAsync(ExploreDbContext reader,
        EmbeddedPrivacyErasureAuthorityRepository privacy, Guid caller)
    {
        await Assert.That(reader.Database.CurrentTransaction).IsNull();
        await Assert.That(reader.ChangeTracker.HasChanges()).IsFalse();
        await Assert.That(reader.ModerationTrustDomainId).IsNull();
        await Assert.That(reader.IdentityFenceOwnsTransaction).IsFalse();
        await Assert.That(async () => { await privacy.IsSubjectFencedAsync(caller, default); })
            .Throws<InvalidOperationException>();
    }

    private static async Task<CorrectionApprovalPending> PrepareCorrectionApprovalAsync(NoticeDatabase fixture)
    {
        var (original, originalIntent) = await SeedCorrectionApprovalOriginalAsync(fixture);
        await ReplaceCorrectionReviewerAsync(fixture);
        await using var context = fixture.Database.Context();
        context.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        var decision = await context.ModerationDecisions.AsNoTracking().SingleAsync(row => row.Id == fixture.DecisionId);
        context.ModerationTrustDomainId = null;
        var operation = ModerationOperation.Transition(ModerationOperationKind.Revoke, decision.Subject, decision.Scope,
            Guid.CreateVersion7(), Guid.CreateVersion7(), decision.Id, original.Effect.SanctionRevision!.Value,
            original.Effect.PolicyRevision, original.Effect.SanctionId!.Value);
        await Assert.That((await CorrectionService(context, fixture).ExecuteAsync(operation)).Outcome)
            .IsEqualTo(ModerationOperationOutcome.Known);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await using var read = fixture.Database.Context();
        read.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        var effect = await read.ModerationEffectIntents.AsNoTracking().SingleAsync(row => row.Id == operation.EffectId);
        var source = await read.ModerationNoticeSources.AsNoTracking().SingleAsync(row => row.EffectId == effect.Id);
        var capture = JsonSerializer.Deserialize<ModerationNoticeCaptureSnapshot>(source.SnapshotJson)!;
        await Assert.That(source.Matches(effect) && capture.Matches(effect)).IsTrue();
        await Assert.That(capture.Notice).IsNull();
        await AssertCorrectionObligationsAsync(fixture, original, capture.PendingCorrection);
        await Assert.That(source.RetainUntil).IsEqualTo(original.Source.RetainUntil);
        await Assert.That(await read.ModerationContributionSelections.AnyAsync()).IsFalse();
        await Assert.That(await read.ModerationProfileAdoptions.AnyAsync()).IsFalse();
        await Assert.That(await read.ModerationEventTargetAdoptions.AnyAsync()).IsFalse();
        await Assert.That(await read.ModerationResourceTargetAdoptions.AnyAsync()).IsFalse();
        var epoch = await read.ModerationQualificationEpochs.AsNoTracking().SingleAsync();
        var intent = originalIntent with
        {
            ApprovalId = Guid.CreateVersion7(), EffectId = effect.Id, ExpectedDecisionRevision = effect.DecisionRevision,
            RemainingActions = new("The named sanction is revoked; current rights must be recomputed independently."),
            ValidUntil = fixture.Clock.GetUtcNow().AddSeconds(20), RetainUntil = original.Source.RetainUntil!.Value,
            ContentRepresentatives = []
        };
        return new(original, effect, new ApproveModerationNoticeCorrectionDisclosureSource
        {
            OperationId = Guid.CreateVersion7(), Target = decision.Scope, ExpectedQualificationRevision = epoch.Revision,
            Intent = intent, ExpectedPendingSnapshotHash = source.SnapshotHash,
            ExpectedPendingEffectBindingHash = source.EffectBindingHash
        });
    }

    // Historical creating custody is an explicit factory seed, not evidence of a native activation or approval producer.
    private static async Task<(CorrectionCreatingLedger Original, ModerationNoticeDisclosureIntent Intent)>
        SeedCorrectionApprovalOriginalAsync(NoticeDatabase fixture)
    {
        await using var context = fixture.Database.Context();
        context.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        var templateEffect = await context.ModerationEffectIntents.AsNoTracking().SingleAsync(row => row.Id == fixture.EffectId);
        var templateSource = await context.ModerationNoticeSources.AsNoTracking().SingleAsync(row => row.EffectId == templateEffect.Id);
        var template = JsonSerializer.Deserialize<ModerationNoticeCaptureSnapshot>(templateSource.SnapshotJson)!;
        var publication = await ModerationPolicyPublicationGraph.Include(context.ModerationPolicyPublications.AsNoTracking())
            .SingleAsync(row => row.Id == fixture.Approval.PublicationId);
        var draft = ModerationPolicyPublicationProjection.RestoreHistory(publication)!;
        await Assert.That(draft).IsNotNull();
        var profile = draft.Profiles.Single(row => row.Definition.Id == fixture.Approval.ProfileId).Definition;
        var reason = draft.Proposed.Reasons.Single(row => row.Reference == profile.Reason);
        var classification = draft.Proposed.Classes.Single(row => row.Reference == reason.Class);
        var capturedAt = fixture.Clock.GetUtcNow().AddSeconds(1);
        var effect = ModerationEffectIntent.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), templateEffect.DecisionId,
            templateEffect.DecisionRevision, templateEffect.SanctionId, templateEffect.SanctionRevision,
            templateEffect.TrustDomainId, templateEffect.ScopeKind, templateEffect.ScopeId, templateEffect.SubjectKind,
            templateEffect.SubjectId, (int)ModerationOperationKind.Activate, templateEffect.PolicyRevision,
            templateEffect.QualificationRevision, capturedAt, null, null);
        var intent = fixture.Approval.Intent with
        {
            ApprovalId = Guid.CreateVersion7(), EffectId = effect.Id,
            RuleMeaning = new(classification.Meaning), ReasonMeaning = new(reason.Meaning)
        };
        var approval = ModerationNoticeDisclosureApproval.Approve(intent, fixture.Database.HostId,
            effect.TrustDomainId, fixture.Database.TenantId, fixture.Database.AdministratorId, 1, capturedAt);
        var capture = template with
        {
            Finding = template.Finding! with { Reason = new(profile.Reason.OwnerId, profile.Reason.Code, profile.Reason.Revision) },
            Notice = template.Notice! with
            {
                EffectId = effect.Id, OperationId = effect.OperationId, OperationKind = (int)ModerationOperationKind.Activate,
                CapturedAt = capturedAt, ApprovalId = approval.Id, CatalogueRevision = publication.CatalogueRevision,
                ProfileSource = new(publication.Id, publication.Revision, publication.WriterGeneration),
                Reason = new(profile.Reason.OwnerId, profile.Reason.Code, profile.Reason.Revision),
                RuleMeaning = intent.RuleMeaning, ReasonMeaning = intent.ReasonMeaning
            }
        };
        var source = ModerationNoticeSource.Capture(effect, fixture.Database.TenantId,
            JsonSerializer.Serialize(capture), templateSource.RetainUntil);
        ModerationNotice[] notices =
        [
            ModerationNotice.Create(Guid.CreateVersion7(), source, ModerationNoticeAudience.Subject,
                effect.SubjectKind, effect.SubjectId, null, ModerationNoticeDeliveryDisposition.NativeRoutingUnavailable),
            ModerationNotice.Create(Guid.CreateVersion7(), source, ModerationNoticeAudience.Reviewer,
                (int)ModerationSubjectKind.Human, fixture.Database.ReviewerId, null, ModerationNoticeDeliveryDisposition.NativeRoutingUnavailable)
        ];
        context.AddRange(effect, approval, source);
        context.AddRange(notices);
        await context.SaveChangesAsync();
        await Assert.That(source.Matches(effect) && capture.Matches(effect)).IsTrue();
        fixture.Clock.UtcNow = capturedAt.AddSeconds(1);
        return (new(effect, source, capture, notices), intent);
    }

    private static async Task<ModerationQualificationWriteResult> WriteCorrectionApprovalAsync(NoticeDatabase fixture,
        ModerationQualificationMutation request, ModerationNoticePrivacyFixture? privacy = null, IInterceptor? interceptor = null)
    {
        await using var context = fixture.Database.Context(interceptor);
        var (admin, machine) = CorrectionIdentity(context, fixture.Database.AdministratorId);
        var clock = fixture.Clock;
        var publications = new ModerationPolicyPublicationRepository(context, admin, machine, fixture.Database.Inventory, null, clock);
        var activation = new ModerationNativeActivationQualification(context, fixture.Database.Inventory, null,
            new ModerationQualificationPolicySourceOwner(context, clock), clock);
        var repository = new ModerationQualificationRepository(context, admin, machine, publications, activation,
            clock, (privacy ?? fixture.Privacy).Authority(context, clock));
        var result = await repository.ExecuteAsync(request);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await Assert.That(context.ChangeTracker.HasChanges()).IsFalse();
        await Assert.That(context.ModerationTrustDomainId).IsNull();
        return result;
    }

    private static async Task<CorrectionApprovalState> CorrectionApprovalStateAsync(NoticeDatabase fixture, CorrectionApprovalPending pending)
    {
        await using var context = fixture.Database.Context();
        context.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        return new(await context.ModerationNoticeDisclosureApprovals.CountAsync(),
            await context.ModerationQualificationMutationReceipts.CountAsync(),
            (await context.ModerationQualificationEpochs.SingleAsync()).Revision,
            JsonSerializer.Serialize(await context.ModerationNoticeSources.AsNoTracking().OrderBy(row => row.EffectId).ToArrayAsync()),
            JsonSerializer.Serialize(await context.ModerationNotices.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync()),
            JsonSerializer.Serialize(await context.ModerationEffectIntents.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync()),
            await context.ModerationOperationReceipts.CountAsync(), await context.ModerationProfileAdoptions.CountAsync(),
            await context.ModerationEventTargetAdoptions.CountAsync(), await context.ModerationResourceTargetAdoptions.CountAsync(),
            await context.ModerationContributionSelections.CountAsync(), await context.Set<ModerationAdjudicatedCaseReadGrant>().CountAsync(),
            await context.Set<ModerationNoticeNotificationReceipt>().CountAsync(), await context.NotificationIntents.CountAsync(),
            await context.Notifications.CountAsync(), await context.NotificationDeliveries.CountAsync(),
            await context.EmailDispatchOutbox.CountAsync(), await context.OutboxMessages.CountAsync(),
            JsonSerializer.Serialize(await context.ModerationSanctions.AsNoTracking().SingleAsync(row => row.Id == pending.Effect.SanctionId)));
    }

    private static async Task AssertCorrectionApprovalHasNoDeliveryAsync(NoticeDatabase fixture, CorrectionApprovalPending pending)
    {
        await using var context = fixture.Database.Context();
        context.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        var source = await context.ModerationNoticeSources.AsNoTracking().SingleAsync(row => row.EffectId == pending.Effect.Id);
        var capture = JsonSerializer.Deserialize<ModerationNoticeCaptureSnapshot>(source.SnapshotJson)!;
        await AssertCorrectionObligationsAsync(fixture, pending.Original, capture.PendingCorrection);
        await Assert.That(capture.Notice).IsNull();
        await Assert.That(capture.Finding).IsNull();
        await Assert.That((await context.ModerationEffectIntents.SingleAsync(row => row.Id == pending.Effect.Id)).CompletedAt).IsNull();
        await Assert.That(await context.Set<ModerationNoticeNotificationReceipt>().AnyAsync()).IsFalse();
        await Assert.That(await context.NotificationIntents.AnyAsync()).IsFalse();
        await Assert.That(await context.Notifications.AnyAsync()).IsFalse();
        await Assert.That(await context.NotificationDeliveries.AnyAsync()).IsFalse();
        await Assert.That(await context.EmailDispatchOutbox.AnyAsync()).IsFalse();
        await Assert.That(await context.OutboxMessages.CountAsync()).IsEqualTo(1);
    }

    private sealed record CorrectionApprovalPending(CorrectionCreatingLedger Original, ModerationEffectIntent Effect,
        ApproveModerationNoticeCorrectionDisclosureSource Request);

    private sealed record CorrectionApprovalState(int Approvals, int QualificationReceipts, long QualificationRevision,
        string Sources, string Notices, string Effects, int OperationReceipts, int ProfileAdoptions, int EventTargetAdoptions,
        int ResourceTargetAdoptions, int ContributionSelections, int ReadGrants, int Acknowledgements, int NotificationIntents,
        int Notifications, int Deliveries, int Emails, int OutboxMessages, string Sanction);

    private static ModerationOperationService CorrectionService(ExploreDbContext context, NoticeDatabase fixture,
        ModerationNoticePrivacyFixture? privacyFixture = null)
    {
        var (admin, machine) = CorrectionIdentity(context, fixture.Database.AdministratorId);
        var clock = fixture.Clock;
        var privacy = (privacyFixture ?? fixture.Privacy).Authority(context, clock);
        var publications = new ModerationPolicyPublicationRepository(context, admin, machine, fixture.Database.Inventory, null, clock);
        var activation = new ModerationNativeActivationQualification(context, fixture.Database.Inventory, null,
            new ModerationQualificationPolicySourceOwner(context, clock), clock);
        var keys = new ExternalApiKeyRepository(context);
        var bindings = new ModerationMachineSubjectBindingRepository(context, admin, machine, keys,
            new ModerationNativeAuthorityParticipantTests.UnconfiguredKeys(), clock);
        var participant = new ModerationAuthorityParticipant(context, admin, machine, bindings, keys,
            publications, fixture.Database.Inventory, activation, clock);
        var selections = new ModerationPolicySelectionSource(context, publications, fixture.Database.Inventory);
        var sources = new ModerationPolicySelectionNoticeSourceOwner(context, selections, participant, clock);
        var unit = new EfCoreUnitOfWork(context);
        var mutationLock = new RelationalSettingMutationLock(context, unit);
        var materializer = new RecipientNotificationMaterializer(new NotificationIntentRepository(context, mutationLock, clock),
            unit, mutationLock, new PrivacyErasureStateRepository(context));
        var capture = new ModerationNoticeCaptureRepository(context, privacy, sources, selections,
            new ModerationNoticeRecipientRepository(context), new ModerationNoticeNativeMaterializer(
                new DefaultNotificationOwnershipResolver(Options.Create(new NotificationRoutingOptions())),
                new NotificationPreferenceResolver(context), materializer), clock, participant);
        return new(new ModerationStore(context, participant, clock, privacy, capture),
            participant, new ModerationEffectPlanner(), clock);
    }

    private static async Task ReplaceCorrectionReviewerAsync(NoticeDatabase fixture)
    {
        await fixture.MutateAsync(async context =>
        {
            var original = await context.ModerationReviewerAssignments.SingleAsync(row =>
                row.ReviewerHumanId == fixture.Database.ReviewerId);
            var now = fixture.Clock.GetUtcNow();
            original.Withdraw(original.Revision, fixture.Database.AdministratorId, now);
            var allegation = await context.ModerationCases.SingleAsync(row => row.Id == original.CaseId);
            var replacement = ModerationReviewerAssignment.Assign(Guid.CreateVersion7(), allegation, allegation.Revision,
                fixture.Database.UrgencyAssessorId, fixture.Database.AdministratorId, now);
            var identity = ModerationQualificationEvidence.QualifyController(Guid.CreateVersion7(), allegation.Id,
                allegation.Scope.TrustDomainId, fixture.Database.TenantId, fixture.Database.UrgencyAssessorId,
                fixture.Database.UrgencyAssessorId, fixture.Database.AdministratorId,
                "Independent replacement controller.", now.AddMinutes(15), now);
            var conflict = ModerationQualificationEvidence.AssessConflict(Guid.CreateVersion7(), allegation.Id,
                allegation.Scope.TrustDomainId, fixture.Database.TenantId, fixture.Database.UrgencyAssessorId,
                fixture.Database.UrgencyAssessorId, fixture.Database.AdministratorId,
                (int)ModerationReviewerConflictState.Independent, "Independent replacement reviewer.", now.AddMinutes(15), now);
            replacement.QualifyController(1, fixture.Database.UrgencyAssessorId, identity.Id,
                fixture.Database.AdministratorId, now.AddMinutes(15), now);
            replacement.RecordConflictAssessment(replacement.Revision, ModerationReviewerConflictState.Independent,
                conflict.Id, fixture.Database.AdministratorId, now);
            context.AddRange(replacement, identity, conflict);
            await Assert.That(original.IsQualifiedAt(now)).IsFalse();
            await Assert.That(replacement.IsQualifiedAt(now)).IsTrue();
        });

        await using var verify = fixture.Database.Context();
        verify.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        await using var transaction = await verify.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var decision = await verify.ModerationDecisions.SingleAsync(row => row.Id == fixture.DecisionId);
        var allegationNow = await verify.ModerationCases.SingleAsync(row => row.Id == decision.CaseId);
        var nowVerified = fixture.Clock.GetUtcNow();
        var activation = new ModerationNativeActivationQualification(verify, fixture.Database.Inventory, null,
            new ModerationQualificationPolicySourceOwner(verify, fixture.Clock), fixture.Clock);
        var procedural = await activation.AcquireQualificationPolicyAsync(fixture.Database.HostId,
            allegationNow, nowVerified, CancellationToken.None);
        await Assert.That(procedural).IsNotNull();
        var approved = procedural!.Approval;
        var current = procedural.Current;
        var qualification = new ModerationNoticeReviewerQualification(decision.CaseId, decision.Scope,
            new(approved.ApprovedSourceId, approved.ApprovedSourceRevision, approved.ApprovedWriterGeneration),
            new(current.SourceId, current.SourceRevision, current.WriterGeneration), current.MaximumReviewerLifetime,
            nowVerified, procedural.ValidUntil);
        var currentReviewers = await new ModerationNoticeRecipientRepository(verify)
            .ReadReviewersAsync(decision, qualification, nowVerified, CancellationToken.None);
        await Assert.That(currentReviewers.Count).IsEqualTo(1);
        await Assert.That(currentReviewers.Single().ReviewerHumanId).IsEqualTo(fixture.Database.UrgencyAssessorId);
    }

    private static async Task AssertCorrectionObligationsAsync(NoticeDatabase fixture,
        CorrectionCreatingLedger original, ModerationNoticeCorrectionObligations? obligations)
    {
        await Assert.That(obligations).IsNotNull();
        await Assert.That(obligations!.OriginalEffectId).IsEqualTo(original.Effect.Id);
        await Assert.That(obligations.OriginalSnapshotHash).IsEqualTo(original.Source.SnapshotHash);
        await Assert.That(obligations.OriginalEffectBindingHash).IsEqualTo(original.Source.EffectBindingHash);
        await Assert.That(obligations.RetainUntil).IsEqualTo(original.Source.RetainUntil!.Value);
        await Assert.That(obligations.Recipients.Length).IsEqualTo(original.Notices.Length);
        foreach (var notice in original.Notices)
        {
            var recipient = obligations.Recipients.Single(row => row.OriginalNoticeId == notice.Id);
            await Assert.That(recipient.Audience).IsEqualTo(notice.Audience);
            await Assert.That(recipient.RecipientSubjectKind).IsEqualTo(notice.RecipientSubjectKind);
            await Assert.That(recipient.RecipientSubjectId).IsEqualTo(notice.RecipientSubjectId);
            await Assert.That(recipient.Disposition).IsEqualTo(ModerationNoticeDeliveryDisposition.RecipientUnavailable);
        }
        await Assert.That(obligations.Recipients.Single(row => row.Audience == ModerationNoticeAudience.Reviewer)
            .RecipientSubjectId).IsEqualTo(fixture.Database.ReviewerId);
    }

    private static (AdminContext Admin, MachinePrincipalAccessor Machine) CorrectionIdentity(ExploreDbContext context, Guid caller)
    {
        var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        http.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, caller.ToString())], "correction-native-witness"));
        return (new AdminContext(http, new PlatformUserRoleRepository(context), new TenantUserRoleGrantRepository(context),
            new OrganizationMemberRepository(context), new GroupMemberRepository(context), new UserExternalLoginRepository(context),
            NullLogger<AdminContext>.Instance), new MachinePrincipalAccessor(http));
    }

    private sealed record CorrectionCreatingLedger(ModerationEffectIntent Effect, ModerationNoticeSource Source,
        ModerationNoticeCaptureSnapshot Capture, ModerationNotice[] Notices)
    {
        public ModerationNotice SubjectNotice => Notices.Single(row => row.Audience == ModerationNoticeAudience.Subject);
        public ModerationNotice ReviewerNotice => Notices.Single(row => row.Audience == ModerationNoticeAudience.Reviewer);
    }

    private static GrantModerationAdjudicatedCaseRead AdjudicatedGrantRequest(
        NoticeDatabase fixture, CorrectionCreatingLedger original) => new(Guid.CreateVersion7(),
        original.Capture.Decision.CaseId, 1, fixture.Database.UrgencyAssessorId,
        new ModerationOriginalEffectAnchor(original.Effect.Id, original.Source.SnapshotHash, original.Source.EffectBindingHash),
        fixture.Clock.GetUtcNow().AddMinutes(10));

    private static IModerationAdjudicatedCaseReadGrantRepository AdjudicatedGrants(ExploreDbContext context,
        NoticeDatabase fixture, Guid caller, ModerationNoticePrivacyFixture? privacy = null)
    {
        var (admin, machine) = CorrectionIdentity(context, caller);
        return new ModerationAdjudicatedCaseReadGrantRepository(context,
            (privacy ?? fixture.Privacy).Authority(context, fixture.Clock), admin, machine, fixture.Clock);
    }

    internal sealed class TestClock : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = Now;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
