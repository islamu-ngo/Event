using System.Collections.Immutable;
using System.Text.Json;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Features.Moderation.Policy;
using Explore.Application.Services;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Identity;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
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

public sealed partial class ModerationResourceQuarantineExecutionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Starts at the committed lifecycle boundary, not the activation/readiness owner.
    /// Sources, selection and independent adoptions use their native writers. Canonical
    /// decision/sanction transitions and the original typed capture seed the accepted input,
    /// as in the lease fixture; no execution grant or mocked internal executor is injected.
    /// </summary>
    private sealed class Fixture(SourceDatabase database) : IAsyncDisposable
    {
        public SourceDatabase Database { get; } = database;
        public ExecutionClock Clock { get; } = new();
        public Guid EffectId { get; private set; }
        public Guid ResourceId { get; private set; }
        public Guid MaterialId { get; private set; }
        public Guid OtherTenantId { get; } = Guid.CreateVersion7();
        public Guid AdoptionId { get; private set; }
        public Guid LeaseId { get; private set; }
        public long LeaseRevision { get; private set; }
        public string KeyId { get; } = ApiKeyHashing.CreateKeyId();
        public string Credential { get; private set; } = string.Empty;
        public DateTimeOffset PurposeDeadline { get; } = Now.AddMinutes(1);

        public ExploreDbContext Context(params IInterceptor[] interceptors)
        {
            if (interceptors.Length == 0) return Database.Context();
            using var borrowed = Database.Context();
            var options = TestDbContextOptions.Create<ExploreDbContext>()
                .UseSqlite(borrowed.Database.GetDbConnection()).UseSnakeCaseNamingConvention()
                .AddInterceptors([SqliteNamedLockTransactionInterceptor.Instance, .. interceptors]).Options;
            return new(options) { TenantContext = Database };
        }

        public static async Task<Fixture> CreateAsync(bool storedFile = false)
        {
            var database = await SourceDatabase.CreateAsync();
            var fixture = new Fixture(database);
            fixture.ResourceId = await ModerationResourceGenerationWriterTests.SeedAsync(database, storedFile: storedFile);
            var scope = AuthorityScope.Resource(database.Scope, fixture.ResourceId);
            Guid eventId;
            await using (var reader = database.Context())
                eventId = await reader.EventResources.Where(row => row.Id == fixture.ResourceId)
                    .Select(row => row.EventId).SingleAsync();
            var representative = EventRoleAssignment.Create(database.TenantId, eventId, database.SubjectId,
                (int)RoleEnum.EventOwner, EventRoleAssignmentStatus.Active,
                Now.AddMinutes(-1).UtcDateTime, Now.AddYears(1).UtcDateTime, database.AdministratorId);
            await using (var writer = database.Context())
            {
                writer.EventRoleAssignments.Add(representative);
                await writer.SaveChangesAsync();
            }
            var representativeProof = new ModerationNoticeContentRepresentative(
                representative.Id, representative.Version, representative.UserId);
            var generationId = Guid.CreateVersion7();
            await Accept(database.AdministratorId, new RecordModerationResourceGenerationSource
            {
                OperationId = Guid.CreateVersion7(),
                Target = database.Scope,
                ExpectedQualificationRevision = 1,
                SourceId = generationId,
                ResourceId = fixture.ResourceId,
                ExpectedResourceRevision = 0,
                ValidUntil = Now.AddMinutes(10)
            });
            var opened = database.OpenCase(scope) with
            {
                ExpectedQualificationRevision = 2,
                SubjectKind = ModerationSubjectKind.Content,
                SubjectId = eventId
            };
            await Accept(database.AdministratorId, opened);
            var request = database.Publication(3, ModerationReferenceProfileId.CFG09);
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
            var published = await database.PublishAsync(database.AdministratorId, request);
            await Assert.That(published.Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Known);
            var publication = published.Publication!;
            var profile = request.Proposed.Profiles.Single();
            var evidenceId = Guid.CreateVersion7();
            await Accept(database.UrgencyAssessorId, new QualifyModerationCaseUrgency
            {
                OperationId = Guid.CreateVersion7(),
                Target = scope,
                ExpectedQualificationRevision = 4,
                EvidenceId = evidenceId,
                CaseId = opened.CaseId,
                ExpectedCaseRevision = 1,
                Justification = "Independently qualified unsafe native resource.",
                ValidUntil = Now.AddMinutes(30)
            });
            var assignmentId = Guid.CreateVersion7();
            await Accept(database.AdministratorId, new AssignModerationReviewerSource
            {
                OperationId = Guid.CreateVersion7(),
                Target = scope,
                ExpectedQualificationRevision = 5,
                AssignmentId = assignmentId,
                CaseId = opened.CaseId,
                ExpectedCaseRevision = 1,
                ReviewerHumanId = database.ReviewerId
            });
            var identityId = Guid.CreateVersion7();
            await Accept(database.AdministratorId, new QualifyModerationReviewerController
            {
                OperationId = Guid.CreateVersion7(),
                Target = scope,
                ExpectedQualificationRevision = 6,
                AssignmentId = assignmentId,
                ExpectedAssignmentRevision = 1,
                ControllerHumanId = database.ReviewerId,
                EvidenceId = identityId,
                Justification = "Independent canonical reviewer control.",
                QualifiedUntil = Now.AddMinutes(30)
            });
            var conflictId = Guid.CreateVersion7();
            await Accept(database.AdministratorId, new AssessModerationReviewerConflict
            {
                OperationId = Guid.CreateVersion7(),
                Target = scope,
                ExpectedQualificationRevision = 7,
                AssignmentId = assignmentId,
                ExpectedAssignmentRevision = 2,
                State = ModerationReviewerConflictState.Independent,
                EvidenceId = conflictId,
                Justification = "Independent reviewer for this exact resource case."
            });
            var findingId = Guid.CreateVersion7();
            await Accept(database.ReviewerId, new FinalizeModerationFindingSource
            {
                OperationId = Guid.CreateVersion7(),
                Target = scope,
                ExpectedQualificationRevision = 8,
                FindingId = findingId,
                CaseId = opened.CaseId,
                ExpectedCaseRevision = 1,
                PolicyId = publication.PolicyId,
                ExpectedPolicyRevision = publication.Revision,
                Rule = profile.Rule,
                Reason = profile.Reason,
                EvidenceId = evidenceId,
                Justification = "Final finding for the exact observed unsafe material."
            });
            ModerationDecision decision;
            ModerationFinding finding;
            await using (var writer = database.Context())
            {
                writer.ModerationTrustDomainId = database.Scope.TrustDomainId;
                finding = await writer.ModerationFindings.AsNoTracking().SingleAsync(row => row.Id == findingId);
                decision = ModerationDecision.Propose(Guid.CreateVersion7(), opened.CaseId,
                    SubjectReference.Create(ModerationSubjectKind.Content, eventId, database.Scope),
                    SubjectReference.Create(ModerationSubjectKind.Human, database.AdministratorId, database.Instance),
                    database.Scope, scope, profile.Rule, [ModerationCapability.ReadResource],
                    Now, null, Now.AddHours(1), profile.RequiredApprovals, Now);
                decision.AcceptUrgentFinding(finding, 1, Now);
                decision.Approve(SubjectReference.Create(ModerationSubjectKind.Human, database.ReviewerId, database.Instance),
                    database.Scope, 2, Now);
                writer.ModerationDecisions.Add(decision);
                await writer.SaveChangesAsync();
            }
            fixture.EffectId = Guid.CreateVersion7();
            var selectionId = Guid.CreateVersion7();
            var target = new ModerationPolicyTarget(scope, decision.Subject.Kind, decision.Subject.Id,
                fixture.ResourceId, 1, "resource.read", null);
            var contribution = new ModerationPolicyContribution(decision.Id, decision.Rule, decision.Scope,
                decision.Subject.Kind, decision.Subject.Id, decision.StartsAt, null,
                profile.Effects.Select(effect => new ModerationPolicyEffectTarget(effect, target)).ToImmutableArray(),
                [new(null, target)]);
            await Accept(database.AdministratorId, new RecordModerationContributionSelectionSource
            {
                OperationId = Guid.CreateVersion7(),
                Target = scope,
                ExpectedQualificationRevision = 9,
                SelectionId = selectionId,
                EffectId = fixture.EffectId,
                CaseId = opened.CaseId,
                ExpectedCaseRevision = 1,
                DecisionId = decision.Id,
                ExpectedDecisionRevision = 4,
                PolicyId = publication.PolicyId,
                ExpectedPolicyRevision = 1,
                PublicationId = publication.Id,
                ProfileId = profile.Id,
                Contribution = contribution,
                ValidUntil = Now.AddMinutes(10)
            });
            var epoch = 10L;
            foreach (var binding in ModerationContributionSelectionProjection.Bindings(selectionId, contribution))
            {
                var id = Guid.CreateVersion7();
                if (binding.Ordinal == 0) fixture.AdoptionId = id;
                await Accept(database.UrgencyAssessorId, new AdoptModerationResourceTargetSource
                {
                    OperationId = Guid.CreateVersion7(),
                    Target = scope,
                    ExpectedQualificationRevision = epoch++,
                    AdoptionId = id,
                    SelectionId = selectionId,
                    BindingOrdinal = binding.Ordinal,
                    GenerationId = generationId,
                    ExpectedGenerationRevision = 1,
                    CaseId = opened.CaseId,
                    ExpectedCaseRevision = 1,
                    ValidUntil = fixture.PurposeDeadline
                });
            }
            await Accept(database.UrgencyAssessorId, new AdoptModerationProfileSource
            {
                OperationId = Guid.CreateVersion7(),
                Target = scope,
                ExpectedQualificationRevision = epoch++,
                AdoptionId = Guid.CreateVersion7(),
                SelectionId = selectionId,
                Mode = ModerationNativeMode.Embedded,
                CaseId = opened.CaseId,
                ExpectedCaseRevision = 1,
                ValidUntil = Now.AddMinutes(4)
            });
            var intent = new ModerationNoticeDisclosureIntent(Guid.CreateVersion7(), fixture.EffectId, decision.Id, 4,
                opened.CaseId, 1, publication.PolicyId, 1, publication.Id, profile.Id, decision.Id,
                ModerationNoticeSnapshotFactory.SelectionDigest(contribution),
                new("Native source classification."), new("Current source authority and protected response."),
                [new("Approved exact material explanation.")], new("Protected review remains available."),
                new("Request independent review."), new("No automated finding."), new("Finite original retention."),
                Now.AddMinutes(10), Now.AddYears(1))
            { ContentRepresentatives = [representativeProof] };
            await Accept(database.ReviewerId, new ApproveModerationNoticeDisclosureSource
            {
                OperationId = Guid.CreateVersion7(),
                Target = scope,
                ExpectedQualificationRevision = epoch++,
                Intent = intent
            });
            await using (var writer = database.Context())
            {
                writer.ModerationTrustDomainId = database.Scope.TrustDomainId;
                decision = await writer.ModerationDecisions.SingleAsync(row => row.Id == decision.Id);
                var sanction = decision.Activate(Guid.CreateVersion7(), 3, Now);
                writer.ModerationSanctions.Add(sanction);
                var effect = ModerationEffectIntent.Create(fixture.EffectId, Guid.CreateVersion7(), decision.Id,
                    decision.Revision, sanction.Id, sanction.Revision, database.Scope.TrustDomainId, (int)scope.Kind, scope.Id,
                    (int)decision.Subject.Kind, decision.Subject.Id, (int)ModerationOperationKind.Activate,
                    1, epoch, Now, null, null);
                var generation = await writer.ModerationResourceGenerations.AsNoTracking().SingleAsync();
                fixture.MaterialId = generation.MaterialVersionId;
                var draft = ModerationPolicyPublicationProjection.RestoreHistory(publication)!;
                var composed = ModerationPolicyProposedStateCompiler.ComposeRetained(draft, [contribution], Now);
                await Assert.That(composed.Outcome).IsEqualTo(ModerationPolicyCompositionOutcome.Preview);
                var stamp = new ModerationNoticeSourceStamp(publication.Id, publication.Revision, publication.WriterGeneration);
                var originalTarget = new ModerationNoticeTarget(ModerationNoticeScope.From(scope), decision.Subject.Kind,
                    decision.Subject.Id, fixture.ResourceId, 1, target.Operation, null, "event.resource-generation",
                    new(generation.Id, generation.Revision, generation.WriterGeneration.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                ModerationNoticeRule Rule(ModerationRuleReference rule) => new(rule.OwnerId, rule.Code, rule.Revision);
                var notice = new ModerationNoticeSnapshot(1, effect.Id, effect.OperationId, decision.Id, decision.Revision,
                    decision.State, sanction.Id, sanction.Revision, sanction.State, null, null, effect.OperationKind,
                    ModerationNoticeScope.From(scope), decision.Subject.Kind, decision.Subject.Id,
                    ModerationNoticeScope.From(decision.Subject.Origin), publication.PolicyId, publication.Revision,
                    publication.Id, publication.CatalogueRevision, profile.Id, profile.ReferenceProfile, profile.ReferenceRevision,
                    stamp, intent.ApprovalId, 1, contribution.Id, Rule(profile.Rule), Rule(profile.Reason),
                    intent.RuleMeaning, intent.ReasonMeaning, intent.Facts, intent.RemainingActions, intent.Recourse,
                    intent.Automation, intent.Retention,
                    composed.Effects.Select(row => new ModerationNoticeEffect(row.Effect, originalTarget, row.Interval)).ToImmutableArray(),
                    composed.Consequences.Select(row => new ModerationNoticeConsequence(
                        row.Strategy, row.ProtectionPurpose, originalTarget, row.Interval, row.Plan)).ToImmutableArray(),
                    Now, intent.RetainUntil, profile.ContestationCalendarMonths);
                var reviewer = await writer.ModerationReviewerAssignments.AsNoTracking().SingleAsync(row => row.Id == assignmentId);
                var capture = new ModerationNoticeCaptureSnapshot(2, "event.moderation.capture.v2",
                    database.HostId, database.Instance.Parent!.Id, database.Instance.Id, database.TenantId,
                    database.EpochId, epoch,
                    new(decision.Id, decision.Revision, opened.CaseId, decision.State, notice.Scope, decision.Subject.Kind,
                        decision.Subject.Id, notice.SubjectOrigin, notice.Rule, decision.CapabilityMask,
                        decision.StartsAt, decision.EndsAt, decision.ReviewAt, finding.Id, decision.Approvals.Count, sanction.Id, null),
                    new(finding.Id, finding.Revision, finding.State, notice.Reason), notice,
                    [new(reviewer.Id, reviewer.Revision, reviewer.ReviewerHumanId, reviewer.ControllerHumanId!.Value,
                        identityId, 1, reviewer.QualifiedByHumanId!.Value, conflictId, 1,
                        reviewer.AssessedByHumanId!.Value, reviewer.QualifiedUntil!.Value)],
                    fixture.PurposeDeadline, new(database.ProceduralSourceId, 1, "event.native-governance"))
                {
                    ContentRepresentatives = [representativeProof]
                };
                await Assert.That(capture.Matches(effect)).IsTrue();
                writer.AddRange(effect, ModerationOperationReceipt.Complete(effect.OperationId, new string('a', 64),
                    effect.TrustDomainId, effect.ScopeKind, effect.ScopeId, effect.SubjectKind, effect.SubjectId,
                    database.AdministratorId, new string('b', 64), decision.Id, sanction.Id, decision.Revision,
                    effect.PolicyRevision, epoch, Now),
                    ModerationNoticeSource.Capture(effect, database.TenantId, JsonSerializer.Serialize(capture), notice.RetainUntil));
                var secret = ApiKeyHashing.CreateSecret();
                fixture.Credential = ApiKeyHashing.FormatPersistedApiKey(fixture.KeyId, secret);
                var host = await writer.ModerationHostBindings.AsNoTracking().SingleAsync();
                var tenantStatus = await writer.TenantStatuses.SingleAsync(row => row.IsActiveState);
                var foreignTenant = new Tenant
                {
                    Id = fixture.OtherTenantId,
                    Slug = $"quarantine-{fixture.OtherTenantId:N}",
                    FullName = "Other quarantine tenant",
                    TenantStatusId = tenantStatus.Id,
                    TenantStatus = tenantStatus
                };
                writer.AddRange(foreignTenant, ModerationQualificationEpoch.Initialize(Guid.CreateVersion7(), host, foreignTenant));
                writer.ExternalApiKeys.Add(new ExternalApiKey
                {
                    Id = Guid.CreateVersion7(),
                    KeyId = fixture.KeyId,
                    Name = "Native quarantine maintenance",
                    SecretHash = ApiKeyHashing.ComputeHash(secret),
                    Scopes = "events:read",
                    OwnerType = ExternalApiKeyOwnerType.InstanceAdmin,
                    OwnerId = database.AdministratorId,
                    ExternalApiKeyStatusId = (int)ExternalApiKeyStatusEnum.Active,
                    ExternalApiKeyStatus = null!,
                    ExternalApiKeyCreditPeriodId = (int)ExternalApiKeyCreditPeriodEnum.None,
                    ExternalApiKeyCreditPeriod = null!
                });
                writer.ModerationMachineSubjectBindings.Add(ModerationMachineSubjectBinding.Establish(
                    Guid.CreateVersion7(), host.Id, ModerationMachineCredentialSource.Persisted, fixture.KeyId,
                    null, ExternalApiKeyOwnerType.InstanceAdmin, database.AdministratorId, host.MaintenanceServicePrincipalId));
                await writer.SaveChangesAsync();
            }
            await fixture.ClaimAsync();
            return fixture;

            async Task Accept(Guid human, ModerationQualificationMutation mutation) =>
                await Assert.That((await database.ExecuteAsync(human, mutation)).IsSuccess).IsTrue();
        }

        public async Task<(MachinePrincipalAccessor Machine, ModerationMachineSubjectBindingRepository Bindings)> AuthenticateAsync(
            ExploreDbContext context)
        {
            var settings = new ModerationReconciliationOptions { Enabled = true, TenantId = Database.TenantId };
            var credential = await new ModerationReconciliationCredentialProvider(new Settings(settings),
                new ExternalApiKeyAuthenticationRepository(new SecurityTestSupport.AdministrativeAuthorityTestFactory(context)),
                new Secret(this), Clock).AuthenticateAsync(Database.TenantId, default);
            var http = new HttpContextAccessor();
            var machine = new MachinePrincipalAccessor(http);
            machine.SetPrincipal(credential ?? throw new InvalidOperationException("Installed maintenance authentication failed."));
            var admin = new AdminContext(http, new PlatformUserRoleRepository(context), new TenantUserRoleGrantRepository(context),
                new OrganizationMemberRepository(context), new GroupMemberRepository(context),
                new UserExternalLoginRepository(context), NullLogger<AdminContext>.Instance);
            return (machine, new(context, admin, machine,
                new ExternalApiKeyAuthenticationRepository(new SecurityTestSupport.AdministrativeAuthorityTestFactory(context)),
                new ModerationNativeAuthorityParticipantTests.UnconfiguredKeys(), Clock));
        }

        public async Task ClaimAsync()
        {
            await using var context = Context();
            var identity = await AuthenticateAsync(context);
            var result = await new ModerationEffectLeaseRepository(context, identity.Machine, identity.Bindings,
                new ExternalApiKeyAuthenticationRepository(new SecurityTestSupport.AdministrativeAuthorityTestFactory(context)),
                Clock).ClaimAsync(EffectId, LeaseRevision == 0 ? 1 : LeaseRevision);
            await Assert.That(result.Outcome).IsEqualTo(ModerationEffectLeaseOutcome.Known);
            LeaseId = result.Effect!.LeaseId!.Value;
            LeaseRevision = result.Effect.Revision;
        }

        public async Task<ModerationResourceQuarantineResult> ExecuteAsync(
            Guid? resourceId = null, Guid? materialId = null, Guid? leaseId = null, long? revision = null,
            int ordinal = 0, Guid? tenantId = null, bool revokeKeyAfterAuthentication = false,
            params IInterceptor[] interceptors)
        {
            await using var context = Context(interceptors);
            var identity = await AuthenticateAsync(context);
            if (revokeKeyAfterAuthentication)
            {
                await using var writer = Context();
                await writer.ExternalApiKeys.Where(row => row.KeyId == KeyId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.ExpiresAt, Clock.UtcNow.UtcDateTime));
            }
            if (tenantId is { } foreign) context.TenantContext = new SelectedTenant(foreign);
            return await new ModerationResourceQuarantineRepository(context, identity.Machine, identity.Bindings,
                new ExternalApiKeyAuthenticationRepository(new SecurityTestSupport.AdministrativeAuthorityTestFactory(context)),
                Clock, Database.Privacy.Authority(context, Clock))
                .ExecuteAsync(EffectId, leaseId ?? LeaseId,
                    revision ?? LeaseRevision, ordinal, resourceId ?? ResourceId, materialId ?? MaterialId);
        }

        public ValueTask DisposeAsync() => Database.DisposeAsync();
    }

    private sealed class ExecutionClock : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = Now;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
    private sealed class SelectedTenant(Guid id) : ITenantContext { public Guid TenantId => id; }
    private sealed class Settings(ModerationReconciliationOptions options) : IOptionsMonitor<ModerationReconciliationOptions>
    {
        public ModerationReconciliationOptions CurrentValue => options;
        public ModerationReconciliationOptions Get(string? name) => options;
        public IDisposable? OnChange(Action<ModerationReconciliationOptions, string?> listener) => null;
    }
    private sealed class Secret(Fixture fixture) : IModerationReconciliationSecretProvider
    {
        public ValueTask<string?> ReadAsync(CancellationToken cancellationToken) => ValueTask.FromResult<string?>(fixture.Credential);
    }
}
