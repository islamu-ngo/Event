using System.Collections.Immutable;
using System.Data;
using System.Security.Claims;
using System.Text.Json;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Policy;
using Explore.Application.Services;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Explore.Infrastructure.Identity;
using Explore.Infrastructure.Services.Moderation;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using ISLAMU.Moderation.Application;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationCaseNativeProofTests
{
    [Test]
    public async Task Tenant_custody_retains_exact_resource_finding_for_original_recipe_and_completion()
    {
        await using var fixture = await OriginalCaseScopeFixture.CreateAsync();
        await using (var reader = fixture.Context())
        {
            var owner = await reader.ModerationCases.AsNoTracking().SingleAsync(row => row.Id == fixture.Original.Case.Id);
            var finding = await reader.ModerationFindings.AsNoTracking().SingleAsync(row => row.Id == fixture.Finding.Id);
            var attribution = await reader.ModerationCaseAttributionSources.AsNoTracking()
                .SingleAsync(row => row.Id == finding.AttributionSourceId);
            await Assert.That(owner.Scope.Kind).IsEqualTo(ModerationScopeKind.Tenant);
            await Assert.That(finding.Scope).IsEqualTo(AuthorityScope.Resource(owner.Scope, fixture.Database.ResourceId));
            await Assert.That(finding.CaseId).IsEqualTo(owner.Id);
            await Assert.That(finding.CaseOccurrenceId).IsEqualTo(owner.OccurrenceId);
            await Assert.That(finding.CaseCreatedAt).IsEqualTo(owner.CreatedAt);
            await Assert.That(attribution.CaseId).IsEqualTo(owner.Id);
            await Assert.That(finding.AttributionGeneration).IsEqualTo(attribution.Generation);
            await Assert.That(attribution.Generation > 0).IsTrue();
            await Assert.That(attribution.OccurrenceCaseId).IsEqualTo(owner.OccurrenceCaseId!.Value);
            await Assert.That(attribution.OccurrenceId).IsEqualTo(owner.OccurrenceId);
            await Assert.That(attribution.EvidenceCaseRevision).IsEqualTo(owner.OccurrenceEvidenceRevision!.Value);
            await Assert.That(attribution.SubjectKind).IsEqualTo(finding.SubjectKind);
            await Assert.That(attribution.SubjectId).IsEqualTo(finding.SubjectId);
            await Assert.That(attribution.TenantId).IsEqualTo(fixture.Database.TenantId);
            await Assert.That(attribution.TrustDomainId).IsEqualTo(fixture.Database.TrustId);
            var host = await reader.ModerationHostBindings.AsNoTracking().SingleAsync();
            var epoch = await reader.ModerationQualificationEpochs.AsNoTracking().SingleAsync();
            await Assert.That(attribution.HostBindingId).IsEqualTo(host.Id);
            await Assert.That(attribution.QualificationEpochId).IsEqualTo(epoch.Id);
            await Assert.That(attribution.QualifiedAt <= finding.FinalizedAt).IsTrue();
            await Assert.That(finding.FinalizedAt < attribution.ValidUntil).IsTrue();
            var root = await reader.ModerationCases.AsNoTracking().Include(row => row.Evidence)
                .SingleAsync(row => row.Id == attribution.OccurrenceCaseId);
            var evidence = root.Evidence.Single(row => row.CaseRevision == attribution.EvidenceCaseRevision);
            await Assert.That(attribution.EvidenceSourceId).IsEqualTo(evidence.SourceId);
            await Assert.That(attribution.EvidenceSourceVersion).IsEqualTo(evidence.SourceVersion);
            await Assert.That(attribution.MaterialFactId).IsEqualTo(evidence.MaterialFactId);
        }
        await Assert.That(await fixture.AcquireRecipeAsync()).IsTrue();
        var result = await fixture.CompleteAsync();
        await Assert.That(result.Outcome).IsEqualTo(ModerationOriginalEffectCompletionOutcome.Pending);
        await Assert.That(result.PendingReason)
            .IsEqualTo(ModerationOriginalEffectCompletionPendingReason.OriginalCauseAccounting);
        await fixture.AssertUncompletedAsync();
    }

    [Test]
    [Arguments("case")]
    [Arguments("resource")]
    [Arguments("identity")]
    public async Task Retained_original_finding_cannot_be_substituted_across_case_resource_or_identity(string change)
    {
        await using var fixture = await OriginalCaseScopeFixture.CreateAsync();
        // Establish that every other recipe/authority prerequisite is usable before substitution.
        await Assert.That(await fixture.AcquireRecipeAsync()).IsTrue();
        await fixture.SubstituteFindingAsync(change);
        await Assert.That(await fixture.AcquireRecipeAsync()).IsFalse();
        var result = await fixture.CompleteAsync();
        await Assert.That(result.Outcome).IsEqualTo(ModerationOriginalEffectCompletionOutcome.Pending);
        await Assert.That(result.PendingReason)
            .IsEqualTo(ModerationOriginalEffectCompletionPendingReason.OriginalRecipe);
        await fixture.AssertUncompletedAsync();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Original_completion_retains_the_finding_case_revision_after_closure(bool rebaseSelection)
    {
        await using var fixture = await OriginalCaseScopeFixture.CreateAsync();
        await Assert.That(await fixture.AcquireRecipeAsync()).IsTrue();
        await using (var writer = fixture.Context())
        {
            await using var transaction = await writer.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var owner = await writer.ModerationCases.SingleAsync(row => row.Id == fixture.Original.Case.Id);
            var selection = await writer.ModerationContributionSelections.AsNoTracking()
                .SingleAsync(row => row.CaseId == owner.Id);
            var source = await writer.ModerationNoticeSources.AsNoTracking()
                .SingleAsync(row => row.EffectId == selection.EffectId);
            owner.Close(owner.Revision, fixture.Database.Clock.Now);
            await writer.SaveChangesAsync();
            await Assert.That(owner.State).IsEqualTo(ModerationCaseState.Closed);
            await Assert.That(owner.Revision).IsEqualTo(fixture.Finding.CaseRevision + 1);
            if (rebaseSelection)
            {
                // An adversarial rewrite of both retained rows still cannot change
                // the case revision owned by the original captured finding.
                await writer.ModerationContributionSelections.Where(row => row.Id == selection.Id)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.CaseRevision, owner.Revision));
                await writer.ModerationProfileAdoptions.Where(row => row.SelectionId == selection.Id)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.CaseRevision, owner.Revision));
            }
            var unchanged = await writer.ModerationNoticeSources.AsNoTracking()
                .SingleAsync(row => row.EffectId == selection.EffectId);
            await Assert.That(unchanged.SnapshotJson).IsEqualTo(source.SnapshotJson);
            await Assert.That(unchanged.SnapshotHash).IsEqualTo(source.SnapshotHash);
            await Assert.That(unchanged.EffectBindingHash).IsEqualTo(source.EffectBindingHash);
            await transaction.CommitAsync();
        }
        await Assert.That(await fixture.AcquireRecipeAsync()).IsEqualTo(!rebaseSelection);
        var result = await fixture.CompleteAsync();
        await Assert.That(result.Outcome).IsEqualTo(ModerationOriginalEffectCompletionOutcome.Pending);
        await Assert.That(result.PendingReason).IsEqualTo(rebaseSelection
            ? ModerationOriginalEffectCompletionPendingReason.OriginalRecipe
            : ModerationOriginalEffectCompletionPendingReason.OriginalCauseAccounting);
        await fixture.AssertUncompletedAsync();
        await using var reader = fixture.Context();
        var retained = await reader.ModerationCases.AsNoTracking()
            .SingleAsync(row => row.Id == fixture.Original.Case.Id);
        await Assert.That(retained.State).IsEqualTo(ModerationCaseState.Closed);
        await Assert.That(retained.Revision).IsEqualTo(fixture.Finding.CaseRevision + 1);
    }

    [Test]
    public async Task Original_recipe_terminal_recheck_rejects_a_later_selection_case_revision()
    {
        await using var fixture = await OriginalCaseScopeFixture.CreateAsync();
        await Assert.That(await fixture.AcquireRecipeAsync()).IsTrue();
        await Assert.That(await fixture.AcquireRecipeAsync(rebaseAfterAcquisition: true)).IsFalse();
        await fixture.AssertUncompletedAsync();
    }

    /// <summary>
    /// Reuses primary native intake, attribution, reviewer qualification and protected-response
    /// writers. As in the original execution fixture, canonical finding/decision/sanction factories
    /// seed the committed lifecycle boundary; they do not witness activation or conformance.
    /// Completion must reach missing original-cause accounting, never a fabricated completed effect.
    /// </summary>
    private sealed partial class OriginalCaseScopeFixture(PreFindingFixture original) : IAsyncDisposable
    {
        public PreFindingFixture Original => original;
        public NativeDatabase Database => original.Database;
        public ModerationFinding Finding { get; private set; } = null!;
        private Guid EffectId { get; set; }
        private Guid LeaseId { get; set; }
        private long LeaseRevision { get; set; }
        private ApiKeyPrincipalContext Principal { get; set; } = null!;
        private static readonly ModerationCapabilityRegistry Inventory =
            ModerationCapabilityRegistry.Parse(ModerationCapabilityRegistry.ReadInventory());

        public ExploreDbContext Context(params IInterceptor[] interceptors) =>
            Database.Context([SqliteNamedLockTransactionInterceptor.Instance, .. interceptors]);

        public (AdminContext Admin, MachinePrincipalAccessor Machine) Identity(ExploreDbContext context, Guid? human = null)
        {
            context.ModerationTrustDomainId = null;
            context.ChangeTracker.Clear();
            var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
            if (human is { } user)
            {
                context.CurrentUserService = new NativeCaller(user);
                http.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, user.ToString())], "original-case-scope"));
            }
            else
                context.CurrentUserService = new NativeCaller(Guid.Empty) { UserId = null, IsAuthenticated = false };
            var admin = new AdminContext(http, new AdministrativeAuthorityRepository(new SecurityTestSupport.AdministrativeAuthorityTestFactory(context)),
                new OrganizationMemberRepository(context),
                new GroupMemberRepository(context), new ExternalLoginAuthenticationRepository(new SecurityTestSupport.AdministrativeAuthorityTestFactory(context), null), NullLogger<AdminContext>.Instance);
            var machine = new MachinePrincipalAccessor(http);
            if (human is null) machine.SetPrincipal(Principal);
            return (admin, machine);
        }

        private ModerationMachineSubjectBindingRepository Bindings(
            ExploreDbContext context, AdminContext admin, MachinePrincipalAccessor machine) =>
            new(context, admin, machine,
                new ExternalApiKeyAuthenticationRepository(new SecurityTestSupport.AdministrativeAuthorityTestFactory(context)),
                new ModerationNativeAuthorityParticipantTests.UnconfiguredKeys(), Database.Clock);

        private ModerationPolicyPublicationRepository Publications(
            ExploreDbContext context, AdminContext admin, MachinePrincipalAccessor machine) =>
            new(context, admin, machine, Inventory, null, Database.Clock);

        public async Task AcceptAsync(Guid human, ModerationQualificationMutation mutation,
            ModerationQualificationWriteOutcome expectedOutcome = ModerationQualificationWriteOutcome.Known)
        {
            await using var context = Context();
            var epoch = await context.ModerationQualificationEpochs.AsNoTracking().SingleAsync();
            var (admin, machine) = Identity(context, human);
            var repository = new ModerationQualificationRepository(context, admin, machine,
                Publications(context, admin, machine),
                new ModerationNativeActivationQualification(context, Inventory, null,
                    new ModerationQualificationPolicySourceOwner(context, Database.Clock), Database.Clock),
                Database.Clock, original.Privacy.Authority(context, Database.Clock),
                responseProtection: Database.NarrativeProtection,
                caseAttribution: new ModerationCaseTargetProofOwner(context, context.CurrentUserService!, machine, Database.Clock));
            await Assert.That((await repository.ExecuteAsync(
                mutation with { ExpectedQualificationRevision = epoch.Revision }, default)).Outcome)
                .IsEqualTo(expectedOutcome);
        }

        public static async Task<OriginalCaseScopeFixture> CreateAsync()
        {
            var original = await PreFindingFixture.CreateAsync(ModerationCaseTargetKind.Resource);
            var fixture = new OriginalCaseScopeFixture(original);
            var database = fixture.Database;
            var owner = original.Case;
            var scope = AuthorityScope.Resource(owner.Scope, database.ResourceId);
            var now = database.Clock.Now;
            var until = now.AddSeconds(10);
            var reviewerId = original.Review.Caller.UserId!.Value;
            await Assert.That((await original.ExecuteAsync(database.CallerId, original.Response())).Outcome)
                .IsEqualTo(ModerationQualificationWriteOutcome.Known);
            Guid adopter;
            ModerationHostBinding host;
            ModerationQualificationEpoch epoch;
            await using (var read = fixture.Context())
            {
                adopter = (await read.Set<ModerationPreFindingCustody>().SingleAsync()).PrivacyOwnerHumanId;
                host = await read.ModerationHostBindings.AsNoTracking().SingleAsync();
                epoch = await read.ModerationQualificationEpochs.AsNoTracking().SingleAsync();
            }
            var reference = ModerationReferenceCatalogue.GetProfile(ModerationReferenceProfileId.CFG09);
            var response = reference.Responses[0];
            ModerationRuleReference Rule(string code) => ModerationRuleReference.Create(owner.Scope.Id, code, 1);
            var classification = Rule("original-resource-class");
            var reason = Rule("original-resource-reason");
            var profile = new ModerationPolicyProfileDefinition(Guid.CreateVersion7(), Rule("original-resource"),
                reason, reference.Id, reference.Revision, response.Grade, [ModerationScopeKind.Resource],
                [ModerationSubjectKind.Content], reference.Effects.ToImmutableArray(), reference.Bundle,
                reference.ProtectionPurpose, [], new(response.Timing.Kind, TimeSpan.FromHours(1)),
                response.RequiredApprovals, 6, ModerationReferenceRecognition.Never);
            var proposed = new ModerationPolicyProposedState(Guid.CreateVersion7(), 0, Guid.CreateVersion7(),
                owner.Scope, ModerationReferenceCatalogue.Revision, Inventory.Revision, Inventory.Fingerprint,
                ModerationPolicyRequestedState.Draft, [ModerationNativeMode.Embedded],
                [new(classification, "Native source classification.", reference.Ground, reference.Basis)],
                [new(reason, "Current source authority and protected response.", classification)], [profile], [profile.Rule]);
            ModerationPolicyPublication publication;
            await using (var publish = fixture.Context())
            {
                var (admin, machine) = fixture.Identity(publish, original.Assessor);
                var result = await fixture.Publications(publish, admin, machine).PublishAsync(
                    new(Guid.CreateVersion7(), host.Id, epoch.Id, epoch.Revision,
                        ModerationPolicyPublicationProjection.WriterGeneration, null, proposed));
                await Assert.That(result.Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Known);
                publication = result.Publication!;
            }
            var generationId = Guid.CreateVersion7();
            await fixture.AcceptAsync(original.Assessor, new RecordModerationResourceGenerationSource
            {
                OperationId = Guid.CreateVersion7(),
                Target = owner.Scope,
                ExpectedQualificationRevision = 1,
                SourceId = generationId,
                ResourceId = database.ResourceId,
                ExpectedResourceRevision = 0,
                ValidUntil = until
            });
            var evidenceId = Guid.CreateVersion7();
            await fixture.AcceptAsync(original.Assessor, new QualifyModerationCaseUrgency
            {
                OperationId = Guid.CreateVersion7(),
                Target = scope,
                ExpectedQualificationRevision = 1,
                EvidenceId = evidenceId,
                CaseId = owner.Id,
                ExpectedCaseRevision = 1,
                Justification = "Independent urgency for the exact native resource.",
                ValidUntil = until
            });
            ModerationDecision decision;
            await using (var writer = fixture.Context())
            {
                var attribution = await writer.ModerationCaseAttributionSources.AsNoTracking()
                    .SingleAsync(row => row.CaseId == owner.Id);
                var evidence = await writer.ModerationQualificationEvidence.AsNoTracking().SingleAsync(row => row.Id == evidenceId);
                var qualification = await writer.ModerationQualificationMutationReceipts.AsNoTracking()
                    .SingleAsync(row => row.SourceId == evidenceId);
                fixture.Finding = ModerationFinding.Finalize(Guid.CreateVersion7(), owner, 1, scope,
                    attribution.Id, attribution.Generation, profile.Rule, profile.Rule, profile.Reason, profile.Reason,
                    SubjectReference.Create(ModerationSubjectKind.Human, reviewerId, owner.Scope.Parent!),
                    ModerationFindingEvidenceReference.QualifiedUrgency(evidence.Id, qualification.Id,
                        owner.Id, 1, evidence.RecordedAt), "Final finding for the exact native resource.", now);
                decision = ModerationDecision.Propose(Guid.CreateVersion7(), owner.Id, owner.Subject!,
                    SubjectReference.Create(ModerationSubjectKind.Human, original.Assessor, owner.Scope.Parent!),
                    owner.Scope, scope, profile.Rule, [ModerationCapability.ReadResource], now, null,
                    now.AddHours(1), profile.RequiredApprovals, now);
                decision.AcceptUrgentFinding(fixture.Finding, 1, now);
                decision.Approve(SubjectReference.Create(ModerationSubjectKind.Human, reviewerId, owner.Scope.Parent!),
                    owner.Scope, 2, now);
                writer.AddRange(fixture.Finding, decision);
                await writer.SaveChangesAsync();
            }
            fixture.EffectId = Guid.CreateVersion7();
            var selectionId = Guid.CreateVersion7();
            var target = new ModerationPolicyTarget(scope, decision.Subject.Kind, decision.Subject.Id,
                database.ResourceId, 1, "resource.read", null);
            var contribution = new ModerationPolicyContribution(decision.Id, decision.Rule, scope,
                decision.Subject.Kind, decision.Subject.Id, now, null,
                profile.Effects.Select(effect => new ModerationPolicyEffectTarget(effect, target)).ToImmutableArray(),
                [new(null, target)]);
            await fixture.AcceptAsync(original.Assessor, new RecordModerationContributionSelectionSource
            {
                OperationId = Guid.CreateVersion7(),
                Target = scope,
                ExpectedQualificationRevision = 1,
                SelectionId = selectionId,
                EffectId = fixture.EffectId,
                CaseId = owner.Id,
                ExpectedCaseRevision = 1,
                DecisionId = decision.Id,
                ExpectedDecisionRevision = 4,
                PolicyId = publication.PolicyId,
                ExpectedPolicyRevision = 1,
                PublicationId = publication.Id,
                ProfileId = profile.Id,
                Contribution = contribution,
                ValidUntil = until
            });
            foreach (var binding in ModerationContributionSelectionProjection.Bindings(selectionId, contribution))
                await fixture.AcceptAsync(adopter, new AdoptModerationResourceTargetSource
                {
                    OperationId = Guid.CreateVersion7(),
                    Target = scope,
                    ExpectedQualificationRevision = 1,
                    AdoptionId = Guid.CreateVersion7(),
                    SelectionId = selectionId,
                    BindingOrdinal = binding.Ordinal,
                    GenerationId = generationId,
                    ExpectedGenerationRevision = 1,
                    CaseId = owner.Id,
                    ExpectedCaseRevision = 1,
                    ValidUntil = until
                });
            await fixture.AcceptAsync(adopter, new AdoptModerationProfileSource
            {
                OperationId = Guid.CreateVersion7(),
                Target = scope,
                ExpectedQualificationRevision = 1,
                AdoptionId = Guid.CreateVersion7(),
                SelectionId = selectionId,
                Mode = ModerationNativeMode.Embedded,
                CaseId = owner.Id,
                ExpectedCaseRevision = 1,
                ValidUntil = until
            });
            var representative = new ModerationNoticeContentRepresentative(database.RoleId, 7, database.CallerId);
            var intent = new ModerationNoticeDisclosureIntent(Guid.CreateVersion7(), fixture.EffectId, decision.Id, 4,
                owner.Id, 1, publication.PolicyId, 1, publication.Id, profile.Id, decision.Id,
                ModerationNoticeSnapshotFactory.SelectionDigest(contribution),
                new("Native source classification."), new("Current source authority and protected response."),
                [new("Approved exact material explanation.")], new("Protected review remains available."),
                new("Request independent review."), new("No automated finding."), new("Finite original retention."),
                until, now.AddYears(1))
            { ContentRepresentatives = [representative] };
            await fixture.AcceptAsync(reviewerId, new ApproveModerationNoticeDisclosureSource
            {
                OperationId = Guid.CreateVersion7(),
                Target = scope,
                ExpectedQualificationRevision = 1,
                Intent = intent
            });
            await using (var writer = fixture.Context())
            {
                epoch = await writer.ModerationQualificationEpochs.AsNoTracking().SingleAsync();
                decision = await writer.ModerationDecisions.SingleAsync(row => row.Id == decision.Id);
                var sanction = decision.Activate(Guid.CreateVersion7(), 3, now);
                var effect = ModerationEffectIntent.Create(fixture.EffectId, Guid.CreateVersion7(), decision.Id,
                    decision.Revision, sanction.Id, sanction.Revision, database.TrustId, (int)scope.Kind, scope.Id,
                    (int)decision.Subject.Kind, decision.Subject.Id, (int)ModerationOperationKind.Activate,
                    1, epoch.Revision, now, null, null);
                var generation = await writer.ModerationResourceGenerations.AsNoTracking().SingleAsync(row => row.Id == generationId);
                var policy = ModerationPolicyPublicationProjection.RestoreHistory(publication)!;
                var composed = ModerationPolicyProposedStateCompiler.ComposeRetained(policy, [contribution], now);
                await Assert.That(composed.Outcome).IsEqualTo(ModerationPolicyCompositionOutcome.Preview);
                var originalTarget = new ModerationNoticeTarget(ModerationNoticeScope.From(scope), decision.Subject.Kind,
                    decision.Subject.Id, database.ResourceId, 1, target.Operation, null, "event.resource-generation",
                    new(generation.Id, generation.Revision,
                        generation.WriterGeneration.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                ModerationNoticeRule NoticeRule(ModerationRuleReference rule) => new(rule.OwnerId, rule.Code, rule.Revision);
                var notice = new ModerationNoticeSnapshot(1, effect.Id, effect.OperationId, decision.Id, decision.Revision,
                    decision.State, sanction.Id, sanction.Revision, sanction.State, null, null, effect.OperationKind,
                    ModerationNoticeScope.From(scope), decision.Subject.Kind, decision.Subject.Id,
                    ModerationNoticeScope.From(decision.Subject.Origin), publication.PolicyId, publication.Revision,
                    publication.Id, publication.CatalogueRevision, profile.Id, profile.ReferenceProfile, profile.ReferenceRevision,
                    new(publication.Id, publication.Revision, publication.WriterGeneration), intent.ApprovalId, 1,
                    contribution.Id, NoticeRule(profile.Rule), NoticeRule(profile.Reason), intent.RuleMeaning,
                    intent.ReasonMeaning, intent.Facts, intent.RemainingActions, intent.Recourse, intent.Automation, intent.Retention,
                    composed.Effects.Select(row => new ModerationNoticeEffect(row.Effect, originalTarget, row.Interval)).ToImmutableArray(),
                    composed.Consequences.Select(row => new ModerationNoticeConsequence(
                        row.Strategy, row.ProtectionPurpose, originalTarget, row.Interval, row.Plan)).ToImmutableArray(),
                    now, intent.RetainUntil, profile.ContestationCalendarMonths);
                var reviewer = await writer.ModerationReviewerAssignments.AsNoTracking()
                    .SingleAsync(row => row.CaseId == owner.Id && row.ReviewerHumanId == reviewerId);
                var qualificationPolicy = await writer.ModerationQualificationPolicySources.AsNoTracking()
                    .SingleAsync(row => row.CaseId == owner.Id);
                var capture = new ModerationNoticeCaptureSnapshot(2, "event.moderation.capture.v2",
                    host.Id, host.SolutionId, host.InstanceId, database.TenantId, epoch.Id, epoch.Revision,
                    new(decision.Id, decision.Revision, owner.Id, decision.State, notice.Scope,
                        decision.Subject.Kind, decision.Subject.Id, notice.SubjectOrigin, notice.Rule,
                        decision.CapabilityMask, decision.StartsAt, decision.EndsAt, decision.ReviewAt,
                        fixture.Finding.Id, decision.Approvals.Count, sanction.Id, null),
                    new(fixture.Finding.Id, fixture.Finding.Revision, fixture.Finding.State, notice.Reason), notice,
                    [new(reviewer.Id, reviewer.Revision, reviewer.ReviewerHumanId, reviewer.ControllerHumanId!.Value,
                        reviewer.IdentityEvidenceId!.Value, 1, reviewer.QualifiedByHumanId!.Value,
                        reviewer.ConflictEvidenceId!.Value, 1, reviewer.AssessedByHumanId!.Value,
                        reviewer.QualifiedUntil!.Value)],
                    until, new(qualificationPolicy.Id, qualificationPolicy.Revision, "event.native-governance"))
                {
                    ContentRepresentatives = [representative]
                };
                await Assert.That(capture.Matches(effect)).IsTrue();
                writer.AddRange(sanction, effect,
                    ModerationOperationReceipt.Complete(effect.OperationId, new string('a', 64),
                        effect.TrustDomainId, effect.ScopeKind, effect.ScopeId, effect.SubjectKind, effect.SubjectId,
                        original.Assessor, new string('b', 64), decision.Id, sanction.Id, decision.Revision,
                        effect.PolicyRevision, epoch.Revision, now),
                    ModerationNoticeSource.Capture(effect, database.TenantId, JsonSerializer.Serialize(capture), notice.RetainUntil));
                var keyId = ApiKeyHashing.CreateKeyId();
                writer.ExternalApiKeys.Add(new ExternalApiKey
                {
                    Id = Guid.CreateVersion7(),
                    KeyId = keyId,
                    Name = "Original native case scope",
                    SecretHash = ApiKeyHashing.ComputeHash(ApiKeyHashing.CreateSecret()),
                    Scopes = "events:read",
                    OwnerType = ExternalApiKeyOwnerType.InstanceAdmin,
                    OwnerId = original.Assessor,
                    ExternalApiKeyStatusId = (int)ExternalApiKeyStatusEnum.Active,
                    ExternalApiKeyStatus = null!,
                    ExternalApiKeyCreditPeriodId = (int)ExternalApiKeyCreditPeriodEnum.None,
                    ExternalApiKeyCreditPeriod = null!
                });
                writer.ModerationMachineSubjectBindings.Add(ModerationMachineSubjectBinding.Establish(
                    Guid.CreateVersion7(), host.Id, ModerationMachineCredentialSource.Persisted, keyId,
                    null, ExternalApiKeyOwnerType.InstanceAdmin, original.Assessor, host.MaintenanceServicePrincipalId));
                await writer.SaveChangesAsync();
                fixture.Principal = new(keyId, null, ExternalApiKeyOwnerType.InstanceAdmin, original.Assessor, [])
                {
                    CredentialSource = ApiKeyCredentialSource.Persisted
                };
            }
            await using (var claim = fixture.Context())
            {
                var (admin, machine) = fixture.Identity(claim);
                var result = await new ModerationEffectLeaseRepository(claim, machine, fixture.Bindings(claim, admin, machine),
                    new ExternalApiKeyAuthenticationRepository(new SecurityTestSupport.AdministrativeAuthorityTestFactory(claim)),
                    database.Clock).ClaimAsync(fixture.EffectId, 1);
                await Assert.That(result.Outcome).IsEqualTo(ModerationEffectLeaseOutcome.Known);
                fixture.LeaseId = result.Effect!.LeaseId!.Value;
                fixture.LeaseRevision = result.Effect.Revision;
            }
            return fixture;
        }

        public async Task<bool> AcquireRecipeAsync(bool rebaseAfterAcquisition = false)
        {
            await using var context = Context();
            var (admin, machine) = Identity(context);
            var privacy = original.Privacy.Authority(context, Database.Clock);
            return await privacy.ExecuteSerializedAsync(async token =>
            {
                await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
                var authority = await new ModerationMaintenanceAuthority(context, machine, Bindings(context, admin, machine),
                    new ExternalApiKeyAuthenticationRepository(new SecurityTestSupport.AdministrativeAuthorityTestFactory(context)), Database.Clock)
                    .AcquireForEffectAsync(EffectId, LeaseId, LeaseRevision, privacy, token);
                if (authority is null) throw new InvalidOperationException("The real retained maintenance authority was unavailable.");
                var source = await context.ModerationNoticeSources.AsNoTracking().SingleAsync(row => row.EffectId == EffectId, token);
                var recipe = await ModerationOriginalRecipeAuthority.AcquireAsync(context, authority, transaction.TransactionId,
                    new(EffectId, source.SnapshotHash, source.EffectBindingHash), privacy, Database.Clock, token);
                if (recipe is null) return false;
                await Assert.That(recipe.Anchor.EffectId).IsEqualTo(EffectId);
                if (rebaseAfterAcquisition)
                {
                    await context.ModerationContributionSelections.Where(row => row.EffectId == EffectId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.CaseRevision, Finding.CaseRevision + 1), token);
                }
                return await recipe.CurrentAsync(token);
            }, CancellationToken.None);
        }

        public async Task<ModerationOriginalEffectCompletionResult> CompleteAsync(params IInterceptor[] interceptors)
        {
            await using var context = Context(interceptors);
            var (admin, machine) = Identity(context);
            var definitions = new ModerationPolicyDefinitionSourceOwner(context, admin, machine, Inventory, Database.Clock);
            var deployment = new ModerationDeploymentSourceOwner(context, definitions, Inventory, [], Database.Clock);
            var sources = new ModerationActivationSourceOwner(context,
                new(context, deployment, Database.Clock), new(context, definitions, Database.Clock),
                new(context, definitions, Database.Clock), new(context, definitions, Database.Clock),
                new(context, definitions, Database.Clock), new(context, definitions, Inventory, Database.Clock),
                null, [], Database.Clock);
            var result = await new ModerationOriginalEffectCompletionRepository(context, machine, Bindings(context, admin, machine),
                new ExternalApiKeyAuthenticationRepository(new SecurityTestSupport.AdministrativeAuthorityTestFactory(context)),
                Database.Clock, original.Privacy.Authority(context, Database.Clock),
                Inventory, Publications(context, admin, machine), sources, [], new ModerationMeasurementReceiptVerifier())
                .CompleteAsync(EffectId, LeaseId, LeaseRevision);
            await Assert.That(context.Database.CurrentTransaction).IsNull();
            await Assert.That(context.ModerationTrustDomainId).IsNull();
            await Assert.That(context.ChangeTracker.Entries().Any()).IsFalse();
            return result;
        }

        public async Task<(Guid Approver, Guid PurposeId, DateTimeOffset Deadline, ModerationResourceSafetyReviewIntent Approval)>
            PrepareNativeUnsafeReviewAsync(Guid causeId, ModerationResourceGeneration generation)
        {
            ModerationReviewerAssignment author;
            ModerationReviewerAssignment approver;
            await using (var reader = Context())
            {
                var assignments = await reader.ModerationReviewerAssignments.AsNoTracking()
                    .Where(row => row.CaseId == Original.Case.Id).ToArrayAsync();
                author = assignments.Single(row => row.ReviewerHumanId == Original.Review.Caller.UserId);
                approver = assignments.Single(row => row.ReviewerHumanId != author.ReviewerHumanId);
            }
            await Assert.That(author.IsQualifiedAt(Database.Clock.Now)).IsTrue();
            await Assert.That(approver.IsQualifiedAt(Database.Clock.Now)).IsTrue();
            await Assert.That(author.ControllerHumanId).IsNotEqualTo(approver.ControllerHumanId);
            var deadline = Database.Clock.Now.AddSeconds(20);
            ModerationResourceSafetyReviewIntent Intent(
                ModerationReviewerAssignment reviewer, ResourceSafetyReviewStep step, Guid? predecessor) => new()
                {
                    OperationId = Guid.CreateVersion7(),
                    QuarantineId = causeId,
                    ResourceId = generation.ResourceId,
                    MaterialVersionId = generation.MaterialVersionId,
                    CaseId = Original.Case.Id,
                    ExpectedCaseRevision = Original.Case.Revision,
                    ReviewerAssignmentId = reviewer.Id,
                    ExpectedReviewerRevision = reviewer.Revision,
                    Step = step,
                    PredecessorId = predecessor,
                    ValidUntil = deadline
                };
            var purpose = Intent(author, ResourceSafetyReviewStep.ProposePurpose, null) with
            {
                RequiredApprovals = 1,
                MaximumEvidenceLifetime = TimeSpan.FromSeconds(20)
            };
            await RequireNativeReviewAsync(author.ReviewerHumanId, purpose);
            var adoption = Intent(approver, ResourceSafetyReviewStep.AdoptPurpose, purpose.OperationId);
            await RequireNativeReviewAsync(approver.ReviewerHumanId, adoption);
            var inspection = Intent(author, ResourceSafetyReviewStep.AttestMaterialSafety, adoption.OperationId) with
            {
                Attestation = ResourceSafetyAttestation.IndependentlyInspectedUnsafeForCurrentAudience
            };
            await RequireNativeReviewAsync(author.ReviewerHumanId, inspection);
            return (approver.ReviewerHumanId, purpose.OperationId, deadline,
                Intent(approver, ResourceSafetyReviewStep.ApproveContinuedQuarantine, inspection.OperationId));
        }

        public async Task<ModerationResourceSafetyReviewResult> NativeReviewAsync(
            Guid human, ModerationResourceSafetyReviewIntent intent, params IInterceptor[] interceptors)
        {
            await using var context = Context(interceptors);
            var (admin, machine) = Identity(context, human);
            return await new ModerationResourceQuarantineReleaseRepository(context, admin, machine, Database.Clock)
                .ReviewAsync(intent);
        }

        private async Task RequireNativeReviewAsync(Guid human, ModerationResourceSafetyReviewIntent intent)
        {
            var result = await NativeReviewAsync(human, intent);
            await Assert.That(result.Outcome).IsEqualTo(ModerationResourceSafetyOutcome.Known);
            await Assert.That(result.Receipt!.Id).IsEqualTo(intent.OperationId);
        }

        public async Task SubstituteFindingAsync(string change)
        {
            await using var writer = Context();
            await using var transaction = await writer.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var retainedSource = await writer.ModerationNoticeSources.AsNoTracking().SingleAsync(row => row.EffectId == EffectId);
            var owner = original.Case;
            var scope = Finding.Scope;
            if (change == "case")
            {
                // Adversarial alias: same subject, occurrence, creation time and real positive source
                // generation, but another case ID. The real source still belongs to the original case.
                var root = await writer.ModerationCases.AsNoTracking().Include(row => row.Evidence)
                    .SingleAsync(row => row.Id == owner.OccurrenceCaseId);
                owner = ModerationCase.OpenAttributed(Guid.CreateVersion7(), Finding.Subject, root,
                    owner.OccurrenceEvidenceRevision!.Value, owner.AttributionSourceId!.Value, owner.CreatedAt);
                writer.ModerationCases.Add(owner);
            }
            if (change == "resource")
            {
                var resourceId = Guid.CreateVersion7();
                var resource = EventResource.CreateDraft(resourceId, Database.TenantId, Database.EventId, null,
                    new EventResourceMetadata
                    {
                        Title = "Another native resource",
                        Kind = EventResourceKindEnum.GeneralDocument,
                        DisclosureMode = EventResourceDisclosureModeEnum.EligibleOnly
                    }, EventResourceDeliveryTypeEnum.ExternalLink, EventResourceAvailability.Create(),
                    [EventResourceAudienceRule.Create(Database.TenantId, Database.EventId, resourceId,
                        EventResourceAudienceKindEnum.Organizer)], Database.CallerId, Database.Clock.Now.UtcDateTime);
                writer.EventResources.Add(resource);
                scope = AuthorityScope.Resource(owner.Scope, resourceId);
            }
            var replacement = ModerationFinding.Finalize(change == "identity" ? Guid.CreateVersion7() : Finding.Id,
                owner, 1, scope, Finding.AttributionSourceId, Finding.AttributionGeneration,
                Finding.Rule, Finding.Rule, Finding.Reason, Finding.Reason, Finding.Adjudicator,
                ModerationFindingEvidenceReference.QualifiedUrgency(Finding.EvidenceReferenceId,
                    Finding.UrgencyQualificationId!.Value, owner.Id, 1, Finding.EvidenceRecordedAt),
                Finding.Justification, Finding.FinalizedAt);
            await writer.ModerationFindings.Where(row => row.Id == Finding.Id).ExecuteDeleteAsync();
            writer.ModerationFindings.Add(replacement);
            await writer.SaveChangesAsync();
            var unchanged = await writer.ModerationNoticeSources.AsNoTracking().SingleAsync(row => row.EffectId == EffectId);
            await Assert.That(unchanged.SnapshotJson).IsEqualTo(retainedSource.SnapshotJson);
            await Assert.That(unchanged.SnapshotHash).IsEqualTo(retainedSource.SnapshotHash);
            await Assert.That(unchanged.EffectBindingHash).IsEqualTo(retainedSource.EffectBindingHash);
            await transaction.CommitAsync();
        }

        public async Task AssertUncompletedAsync()
        {
            await using var reader = Context();
            await Assert.That((await reader.ModerationEffectIntents.SingleAsync(row => row.Id == EffectId)).CompletedAt).IsNull();
            await Assert.That(await reader.ModerationOriginalEffectCompletionReceipts.AnyAsync(row => row.EffectId == EffectId)).IsFalse();
            await Assert.That(await reader.EventResourceModerationQuarantines.AnyAsync()).IsFalse();
        }

        public ValueTask DisposeAsync() => original.DisposeAsync();
    }
}
