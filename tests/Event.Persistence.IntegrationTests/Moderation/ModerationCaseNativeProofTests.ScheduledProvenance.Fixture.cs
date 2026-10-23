using System.Collections.Immutable;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Policy;
using Explore.Application.Services;
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
using Microsoft.Extensions.Logging.Abstractions;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationCaseNativeProofTests
{
    /// <summary>
    /// A real attributed Human response and scheduled contribution. The selected policy is
    /// an inactive native draft: this witnesses permission-time provenance, not activation.
    /// No deployment, conformance, prerequisite or credential owner is replaced by a mock.
    /// </summary>
    private sealed class ScheduledProvenanceFixture(PreFindingFixture original, ModerationCase owner)
        : IAsyncDisposable
    {
        public PreFindingFixture Original => original;
        public NativeDatabase Database => original.Database;
        public ModerationCase Case => owner;
        public Guid PrivacyOwner { get; private set; }
        public Guid CustodyId { get; private set; }
        public Guid NoticeId { get; private set; }
        public Guid ResponseId { get; private set; }
        public DateTimeOffset Until { get; private set; }
        public ModerationDecision Decision { get; private set; } = null!;
        public ModerationSanction Sanction { get; private set; } = null!;
        public ApiKeyPrincipalContext Principal { get; private set; } = null!;
        public Guid MaintenanceId { get; private set; }
        public Guid KeyOwnerId { get; private set; }
        private static readonly ModerationCapabilityRegistry Inventory =
            ModerationCapabilityRegistry.Parse(ModerationCapabilityRegistry.ReadInventory());

        public ModerationOperation Reconcile() => ModerationOperation.Reconcile(
            Case.Subject!, Case.Scope, Guid.CreateVersion7(), Guid.CreateVersion7(),
            Decision.Id, Decision.Revision, 1, Sanction.Id, Sanction.Revision);

        public ModerationQualificationRepository Responses(ExploreDbContext context)
        {
            var (admin, machine) = Identity(context);
            return new(context, admin, machine, null!,
                new ModerationNativeActivationQualification(context, Inventory, null,
                    new ModerationQualificationPolicySourceOwner(context, Database.Clock), Database.Clock),
                Database.Clock, original.Privacy.Authority(context, Database.Clock),
                responseProtection: Database.NarrativeProtection,
                caseAttribution: new ModerationCaseTargetProofOwner(
                    context, context.CurrentUserService!, machine, Database.Clock));
        }

        public ModerationCaseTargetProofOwner Attribution(ExploreDbContext context)
        {
            var (_, machine) = Identity(context);
            return new(context, context.CurrentUserService!, machine, Database.Clock);
        }

        private (AdminContext Admin, MachinePrincipalAccessor Machine) Identity(ExploreDbContext context)
        {
            // There is deliberately no human claim, including the key owner's identity.
            var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
            var caller = new NativeCaller(Guid.Empty) { UserId = null, IsAuthenticated = false };
            context.CurrentUserService = caller;
            var admin = new AdminContext(http, new PlatformUserRoleRepository(context),
                new TenantUserRoleGrantRepository(context), new OrganizationMemberRepository(context),
                new GroupMemberRepository(context), new UserExternalLoginRepository(context),
                NullLogger<AdminContext>.Instance);
            var machine = new MachinePrincipalAccessor(http);
            machine.SetPrincipal(Principal);
            return (admin, machine);
        }

        public async Task<ModerationOperationResult> ObserveAsync(
            ExploreDbContext context, RecordingScheduledResponseOwner recorder,
            ModerationOperation? operation = null)
        {
            var (admin, machine) = Identity(context);
            var keys = new ExternalApiKeyRepository(context);
            var bindings = new ModerationMachineSubjectBindingRepository(context, admin, machine, keys,
                new ModerationNativeAuthorityParticipantTests.UnconfiguredKeys(), Database.Clock);
            var activation = new ModerationNativeActivationQualification(context, Inventory, null,
                new ModerationQualificationPolicySourceOwner(context, Database.Clock), Database.Clock);
            var publications = new ModerationPolicyPublicationRepository(
                context, admin, machine, Inventory, null, Database.Clock);
            var participant = new ModerationAuthorityParticipant(context, admin, machine, bindings, keys,
                publications, Inventory, activation, Database.Clock,
                caseAttribution: Attribution(context), preFindingResponses: recorder);
            context.ModerationTrustDomainId = null;
            var privacy = original.Privacy.Authority(context, Database.Clock);
            return await new ModerationStore(context, participant, Database.Clock, privacy,
                ModerationLedgerPersistenceTests.Capture(context, privacy, participant, Database.Clock))
                .ExecuteAsync(operation ?? Reconcile(), (_, _) =>
                    throw new InvalidOperationException("Inactive policy cannot enter lifecycle mutation."), default);
        }

        private async Task QualifyAsync(Guid human, ModerationQualificationMutation mutation)
        {
            await using var context = Database.Context();
            var epoch = await context.ModerationQualificationEpochs.AsNoTracking().SingleAsync();
            var result = await original.Repository(context, human).ExecuteAsync(
                mutation with { ExpectedQualificationRevision = epoch.Revision }, default);
            await Assert.That(result.Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        }

        public async Task<(ModerationOperationResult Result, Guid DecisionId)> RespondAsync(ExploreDbContext context)
        {
            var subject = Case.Subject!;
            var human = subject.Id;
            var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
            http.HttpContext.User = new System.Security.Claims.ClaimsPrincipal(
                new System.Security.Claims.ClaimsIdentity(
                    [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, human.ToString())],
                    "native-response"));
            var caller = new NativeCaller(human);
            context.CurrentUserService = caller;
            var admin = new AdminContext(http, new PlatformUserRoleRepository(context),
                new TenantUserRoleGrantRepository(context), new OrganizationMemberRepository(context),
                new GroupMemberRepository(context), new UserExternalLoginRepository(context), NullLogger<AdminContext>.Instance);
            var machine = new MachinePrincipalAccessor(http);
            var keys = new ExternalApiKeyRepository(context);
            var bindings = new ModerationMachineSubjectBindingRepository(context, admin, machine, keys,
                new ModerationNativeAuthorityParticipantTests.UnconfiguredKeys(), Database.Clock);
            var activation = new ModerationNativeActivationQualification(context, Inventory, null,
                new ModerationQualificationPolicySourceOwner(context, Database.Clock), Database.Clock);
            var attribution = new ModerationCaseTargetProofOwner(context, caller, machine, Database.Clock);
            var privacy = original.Privacy.Authority(context, Database.Clock);
            var responses = new ModerationQualificationRepository(context, admin, machine, null!, activation,
                Database.Clock, privacy, responseProtection: Database.NarrativeProtection, caseAttribution: attribution);
            var publications = new ModerationPolicyPublicationRepository(
                context, admin, machine, Inventory, null, Database.Clock);
            var now = Database.Clock.Now;
            // This historical starting proposal establishes no finding, approval or activation.
            var proposed = ModerationDecision.Propose(Guid.CreateVersion7(), Case.Id, subject,
                Decision.Issuer, Decision.IssuingScope, Case.Scope, Decision.Rule, Decision.Capabilities,
                now.AddSeconds(1), now.AddDays(7), null, Decision.RequiredApprovals, now);
            context.ModerationTrustDomainId = Database.TrustId;
            context.ModerationDecisions.Add(proposed);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            context.ModerationTrustDomainId = null;
            var participant = new ModerationAuthorityParticipant(context, admin, machine, bindings, keys,
                publications, Inventory, activation, Database.Clock, caseAttribution: attribution,
                preFindingResponses: responses);
            var operation = ModerationOperation.Transition(ModerationOperationKind.RecordResponse,
                subject, Case.Scope, Guid.CreateVersion7(), Guid.CreateVersion7(), proposed.Id, 1, 1, Guid.Empty);
            var result = await new ModerationOperationService(
                new ModerationStore(context, participant, Database.Clock, privacy,
                    ModerationLedgerPersistenceTests.Capture(context, privacy, participant, Database.Clock)),
                participant, new ModerationEffectPlanner(), Database.Clock).ExecuteAsync(operation);
            return (result, proposed.Id);
        }

        public static async Task<ScheduledProvenanceFixture> CreateAsync(bool endedInterval = false)
        {
            var original = await PreFindingFixture.CreateAsync();
            var database = original.Database;
            ModerationCase owner;
            await using (var context = database.Context())
                owner = await AttributionWriter(context, database, original.Review.Caller).AttributeAsync(
                    original.Review.Request(ModerationResponsibilityBasis.EventSubmission,
                        ModerationSubjectKind.Human, database.CallerId), default)
                    ?? throw new InvalidOperationException("Native Human attribution failed.");
            var fixture = new ScheduledProvenanceFixture(original, owner);
            await using (var read = database.Context())
                fixture.PrivacyOwner = (await read.Set<ModerationPreFindingCustody>()
                    .SingleAsync(row => row.CaseId == original.Case.Id)).PrivacyOwnerHumanId;
            var policyId = Guid.CreateVersion7();
            var qualifiedUntil = database.Clock.Now.AddSeconds(50);
            await fixture.QualifyAsync(original.Assessor, new PublishModerationQualificationPolicySource
            {
                OperationId = Guid.CreateVersion7(), Target = owner.Scope, ExpectedQualificationRevision = 1,
                SourceId = policyId, CaseId = owner.Id, ExpectedCaseRevision = 1, ExpectedSourceRevision = 0,
                MaximumReviewerLifetime = TimeSpan.FromMinutes(1), MaximumUrgencyLifetime = TimeSpan.FromMinutes(1),
                ValidUntil = qualifiedUntil
            });
            await using (var read = database.Context())
            {
                var policy = await read.ModerationQualificationPolicySources.SingleAsync(row => row.Id == policyId);
                await fixture.QualifyAsync(fixture.PrivacyOwner, new AdoptModerationQualificationPolicySource
                {
                    OperationId = Guid.CreateVersion7(), Target = owner.Scope, ExpectedQualificationRevision = 1,
                    SourceId = policyId, CaseId = owner.Id, ExpectedCaseRevision = 1, ExpectedSourceRevision = 1,
                    ExpectedWriterGeneration = policy.WriterGeneration, AdoptionId = Guid.CreateVersion7(),
                    ValidUntil = qualifiedUntil
                });
            }
            foreach (var reviewer in new[] { original.Review.Caller.UserId!.Value, fixture.PrivacyOwner })
            {
                var assignmentId = Guid.CreateVersion7();
                await fixture.QualifyAsync(original.Assessor, new AssignModerationReviewerSource
                {
                    OperationId = Guid.CreateVersion7(), Target = owner.Scope, ExpectedQualificationRevision = 1,
                    AssignmentId = assignmentId, CaseId = owner.Id, ExpectedCaseRevision = 1, ReviewerHumanId = reviewer
                });
                await fixture.QualifyAsync(original.Assessor, new QualifyModerationReviewerController
                {
                    OperationId = Guid.CreateVersion7(), Target = owner.Scope, ExpectedQualificationRevision = 1,
                    AssignmentId = assignmentId, ExpectedAssignmentRevision = 1, ControllerHumanId = reviewer,
                    EvidenceId = Guid.CreateVersion7(), Justification = "Independent canonical Human controller.",
                    QualifiedUntil = qualifiedUntil
                });
                await fixture.QualifyAsync(original.Assessor, new AssessModerationReviewerConflict
                {
                    OperationId = Guid.CreateVersion7(), Target = owner.Scope, ExpectedQualificationRevision = 1,
                    AssignmentId = assignmentId, ExpectedAssignmentRevision = 2,
                    State = ModerationReviewerConflictState.Independent, EvidenceId = Guid.CreateVersion7(),
                    Justification = "Independent of the attributed Human and original reporter."
                });
            }
            fixture.CustodyId = Guid.CreateVersion7();
            fixture.NoticeId = Guid.CreateVersion7();
            fixture.Until = database.Clock.Now.AddSeconds(20);
            var proposal = new ProposeModerationPreFindingCustody(fixture.CustodyId, owner.Id, 1, fixture.PrivacyOwner,
                new(TimeSpan.FromSeconds(30), 8192, "Protected Human response",
                    "Independent purpose-limited native regression custody."), qualifiedUntil);
            await Assert.That((await original.ExecuteAsync(original.Review.Caller.UserId!.Value, proposal)).Outcome)
                .IsEqualTo(ModerationQualificationWriteOutcome.Known);
            await Assert.That((await original.ExecuteAsync(fixture.PrivacyOwner, new AcceptModerationPreFindingCustody(
                Guid.CreateVersion7(), owner.Id, 1, fixture.CustodyId, 1))).Outcome)
                .IsEqualTo(ModerationQualificationWriteOutcome.Known);
            await Assert.That((await original.ExecuteAsync(original.Review.Caller.UserId!.Value,
                new IssueModerationPreFindingNotice(fixture.NoticeId, owner.Id, 1, fixture.CustodyId,
                    new(Guid.Empty, 0, database.CallerId),
                    new(new("A specific Human allegation."), new("The applicable rule is disputed."),
                        new("Submit a protected explanation.")), fixture.Until))).Outcome)
                .IsEqualTo(ModerationQualificationWriteOutcome.Known);
            fixture.ResponseId = Guid.CreateVersion7();
            await Assert.That((await original.ExecuteAsync(database.CallerId,
                new RespondToModerationPreFindingNotice(fixture.ResponseId, owner.Id, 1,
                    fixture.NoticeId, 1, "The original Human allegation is disputed."))).Outcome)
                .IsEqualTo(ModerationQualificationWriteOutcome.Known);

            await using (var context = database.Context())
            {
                var host = await context.ModerationHostBindings.SingleAsync();
                var epoch = await context.ModerationQualificationEpochs.SingleAsync();
                fixture.MaintenanceId = host.MaintenanceServicePrincipalId;
                var instance = owner.Subject!.Origin;
                var reference = ModerationReferenceCatalogue.GetProfile(ModerationReferenceProfileId.CFG06);
                var response = reference.Responses[0];
                // CFG06 permits shortening its finite interval. Procedural custody stays
                // unchanged so the terminal fixture measures expiry, not activation.
                var duration = endedInterval ? TimeSpan.FromSeconds(1) : TimeSpan.FromDays(7);
                ModerationRuleReference Rule(string code) => ModerationRuleReference.Create(owner.Scope.Id, code, 1);
                var rule = new ModerationPolicyProfileDefinition(Guid.CreateVersion7(), Rule("scheduled-human"),
                    Rule("scheduled-human-reason"), reference.Id, reference.Revision, response.Grade,
                    [ModerationScopeKind.Tenant], [ModerationSubjectKind.Human], reference.Effects.ToImmutableArray(),
                    reference.Bundle, reference.ProtectionPurpose, [], new(response.Timing.Kind, duration),
                    response.RequiredApprovals, 6, ModerationReferenceRecognition.Never);
                var proposed = new ModerationPolicyProposedState(Guid.CreateVersion7(), 0, Guid.CreateVersion7(),
                    owner.Scope, ModerationReferenceCatalogue.Revision, Inventory.Revision, Inventory.Fingerprint,
                    ModerationPolicyRequestedState.Draft, [ModerationNativeMode.Embedded],
                    [new(Rule("scheduled-human-class"), "Native scheduled provenance.", reference.Ground, reference.Basis)],
                    [new(rule.Reason, "Independent Human finding.", Rule("scheduled-human-class"))], [rule], [rule.Rule]);
                var publication = new ModerationPolicyPublicationRequest(Guid.CreateVersion7(), host.Id, epoch.Id,
                    epoch.Revision, ModerationPolicyPublicationProjection.WriterGeneration, null, proposed);
                // Use the same real human identity construction as the response fixture.
                var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
                http.HttpContext.User = new System.Security.Claims.ClaimsPrincipal(
                    new System.Security.Claims.ClaimsIdentity(
                        [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier,
                            original.Assessor.ToString())], "scheduled-policy"));
                var admin = new AdminContext(http, new PlatformUserRoleRepository(context),
                    new TenantUserRoleGrantRepository(context), new OrganizationMemberRepository(context),
                    new GroupMemberRepository(context), new UserExternalLoginRepository(context), NullLogger<AdminContext>.Instance);
                context.ChangeTracker.Clear();
                context.ModerationTrustDomainId = null;
                await Assert.That((await new ModerationPolicyPublicationRepository(context, admin,
                    new MachinePrincipalAccessor(http), Inventory, null, database.Clock).PublishAsync(publication)).Outcome)
                    .IsEqualTo(ModerationPolicyPublicationOutcome.Known);
                context.ModerationTrustDomainId = database.TrustId;
                var now = database.Clock.Now;
                var adjudicator = SubjectReference.Create(ModerationSubjectKind.Human,
                    original.Review.Caller.UserId!.Value, instance);
                // Historical starting finding/decision/sanction and credential setup below
                // is not measured activation or conformance evidence.
                var finding = ModerationFinding.Finalize(Guid.CreateVersion7(), owner, 1,
                    owner.Scope, owner.AttributionSourceId, 1,
                    rule.Rule, rule.Rule, rule.Reason, rule.Reason, adjudicator,
                    ModerationFindingEvidenceReference.Response(fixture.ResponseId, owner.Id, 1, now),
                    "The independently adjudicated original protected response.", now);
                fixture.Decision = ModerationDecision.Propose(Guid.CreateVersion7(), owner.Id, owner.Subject,
                    SubjectReference.Create(ModerationSubjectKind.Human, original.Assessor, instance),
                    instance, owner.Scope, rule.Rule, [ModerationCapability.Publish],
                    now.AddSeconds(1), now.AddSeconds(1).Add(duration), null, response.RequiredApprovals, now);
                fixture.Decision.RecordResponse(1, now);
                fixture.Decision.AcceptFinding(finding.Id, 2, now);
                fixture.Decision.Approve(adjudicator, owner.Scope, 3, now);
                if (response.RequiredApprovals == 2)
                    fixture.Decision.Approve(SubjectReference.Create(ModerationSubjectKind.Human,
                        fixture.PrivacyOwner, instance), owner.Scope, 4, now);
                fixture.Sanction = fixture.Decision.Activate(Guid.CreateVersion7(), fixture.Decision.Revision, now);
                context.AddRange(finding, fixture.Decision, fixture.Sanction);
                fixture.KeyOwnerId = Guid.CreateVersion7();
                context.Users.Add(new User { Id = fixture.KeyOwnerId, Pii = new UserPii
                {
                    UserId = fixture.KeyOwnerId, Email = $"{fixture.KeyOwnerId:N}@example.invalid",
                    FirstName = "Unrelated", LastName = "CredentialOwner"
                }});
                context.PlatformUserRoles.Add(new PlatformUserRole
                {
                    Id = Guid.CreateVersion7(), UserId = fixture.KeyOwnerId, User = null!,
                    RoleId = (int)RoleEnum.Admin, Role = null!, GrantedAt = now.UtcDateTime
                });
                var keyId = Guid.CreateVersion7().ToString("N");
                context.ExternalApiKeys.Add(new ExternalApiKey
                {
                    Id = Guid.CreateVersion7(), KeyId = keyId, Name = "Scheduled native maintenance",
                    SecretHash = ApiKeyHashing.ComputeHash(ApiKeyHashing.CreateSecret()), Scopes = "events:read",
                    OwnerType = ExternalApiKeyOwnerType.InstanceAdmin, OwnerId = fixture.KeyOwnerId,
                    ExternalApiKeyStatusId = (int)ExternalApiKeyStatusEnum.Active, ExternalApiKeyStatus = null!,
                    ExternalApiKeyCreditPeriodId = (int)ExternalApiKeyCreditPeriodEnum.None, ExternalApiKeyCreditPeriod = null!
                });
                context.ModerationMachineSubjectBindings.Add(ModerationMachineSubjectBinding.Establish(
                    Guid.CreateVersion7(), host.Id, ModerationMachineCredentialSource.Persisted, keyId,
                    null, ExternalApiKeyOwnerType.InstanceAdmin, fixture.KeyOwnerId, fixture.MaintenanceId));
                await context.SaveChangesAsync();
                fixture.Principal = new(keyId, null, ExternalApiKeyOwnerType.InstanceAdmin, fixture.KeyOwnerId, [])
                {
                    CredentialSource = ApiKeyCredentialSource.Persisted
                };
            }
            database.Clock.Now = database.Clock.Now.AddSeconds(2);
            return fixture;
        }

        public ValueTask DisposeAsync() => original.DisposeAsync();
    }

    /// <summary>Records the actual native permission-time session, preserving every real owner check.</summary>
    private sealed class RecordingScheduledResponseOwner(
        ModerationQualificationRepository owner,
        Func<IModerationFindingProvenanceSession, ModerationFindingEvidenceReference?, CancellationToken, Task>? observe = null)
        : IModerationPreFindingResponseOwner
    {
        public IModerationFindingProvenanceSession? Session { get; private set; }
        public ModerationFindingEvidenceReference? Reference { get; private set; }
        public IReadOnlyList<Guid>? Humans { get; private set; }

        public async Task<IReadOnlyList<Guid>?> DiscoverResponseHumansAsync(Guid caseId, CancellationToken token) =>
            Humans = await owner.DiscoverResponseHumansAsync(caseId, token);

        public Task<ModerationFindingEvidenceReference?> AcquireCurrentResponseAsync(
            Guid caseId, Guid responseId, CancellationToken token) =>
            owner.AcquireCurrentResponseAsync(caseId, responseId, token);

        public async Task<ModerationFindingEvidenceReference?> AcquireCurrentResponseAsync(
            IModerationFindingProvenanceSession provenance, Guid caseId, Guid responseId, CancellationToken token)
        {
            var reference = await owner.AcquireCurrentResponseAsync(provenance, caseId, responseId, token);
            if (Session is null)
            {
                Session = provenance;
                Reference = reference;
                if (observe is not null) await observe(provenance, reference, token);
            }
            return reference;
        }

        public Task<bool> RecheckResponseAsync(CancellationToken token) => owner.RecheckResponseAsync(token);
    }
}
