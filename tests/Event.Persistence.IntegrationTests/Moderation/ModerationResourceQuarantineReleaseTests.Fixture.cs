using System.Security.Claims;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Handlers.Commands;
using Explore.Domain;
using Explore.Infrastructure.Identity;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using ISLAMU.Moderation.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using SourceDatabase = Event.Persistence.IntegrationTests.Moderation.ModerationQualificationWriterTests.SourceDatabase;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationResourceQuarantineReleaseTests
{
    private static readonly DateTimeOffset OriginalTime = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Starts at native committed operational custody, using EventResource's actual apply method.
    /// This is not original-executor conformance. No original notice or maintenance grant is seeded.
    /// Every fresh case, procedural adoption, reviewer/controller/conflict and safety step goes
    /// through its actual native writer; no fixture-only qualification owner supplies permission.
    /// </summary>
    private sealed partial class Fixture(SourceDatabase database) : IAsyncDisposable
    {
        public SourceDatabase Database { get; } = database;
        public ReleaseClock Clock { get; } = new();
        public Guid ResourceId { get; private set; }
        public Guid MaterialId { get; private set; }
        public Guid CauseId { get; private set; }
        public Guid OtherCauseId { get; private set; }
        public Guid HistoricalFindingId { get; private set; }
        public Guid CaseId { get; private set; }
        public Guid PolicySourceId { get; private set; }
        public Guid PolicyAdoptionId { get; private set; }
        public Guid PurposeId { get; private set; }
        public Guid EvidenceId { get; private set; }
        public Guid ApprovalId { get; private set; }
        public Dictionary<Guid, Guid> Assignments { get; } = [];
        public AuthorityScope Scope => NativeAttribution ? Database.Scope : AuthorityScope.Resource(Database.Scope, ResourceId);
        public bool NativeAttribution { get; private set; }
        public Guid Author => Database.ReviewerId;
        public Guid Approver => Database.UrgencyAssessorId;
        public DateTimeOffset Deadline { get; private set; }

        public ExploreDbContext Context(params IInterceptor[] interceptors)
        {
            if (interceptors.Length == 0) return Database.Context();
            using var borrowed = Database.Context();
            var options = TestDbContextOptions.Create<ExploreDbContext>()
                .UseSqlite(borrowed.Database.GetDbConnection()).UseSnakeCaseNamingConvention()
                .AddInterceptors([SqliteNamedLockTransactionInterceptor.Instance, .. interceptors]).Options;
            return new(options) { TenantContext = Database };
        }

        public static async Task<Fixture> CreateAsync(
            bool storedFile = false, int approvals = 1, bool prepare = true,
            bool nativeAttribution = false, bool attributeSiblingResource = false)
        {
            var database = await SourceDatabase.CreateAsync();
            var fixture = new Fixture(database);
            fixture.ResourceId = await ModerationResourceGenerationWriterTests.SeedAsync(database, storedFile: storedFile);
            Guid eventId;
            await using (var writer = database.Context())
            {
                writer.ModerationTrustDomainId = database.Scope.TrustDomainId;
                var resource = await writer.EventResources.SingleAsync(row => row.Id == fixture.ResourceId);
                eventId = resource.EventId;
                fixture.MaterialId = resource.MaterialVersionId!.Value;
                fixture.CauseId = resource.ApplyModerationQuarantine(Guid.CreateVersion7(), Guid.CreateVersion7(),
                    0, fixture.MaterialId, resource.ConcurrencyStamp, OriginalTime).Id;
                fixture.OtherCauseId = resource.ApplyModerationQuarantine(Guid.CreateVersion7(), Guid.CreateVersion7(),
                    0, fixture.MaterialId, resource.ConcurrencyStamp, OriginalTime).Id;
                var historicalCase = ModerationCase.Open(Guid.CreateVersion7(),
                    SubjectReference.Create(ModerationSubjectKind.Content, eventId, database.Scope),
                    fixture.Scope, Guid.CreateVersion7(), OriginalTime);
                var rule = ModerationRuleReference.Create(database.Scope.Id, "historical-object-control", 1);
                var evidence = ModerationFindingEvidenceReference.QualifiedUrgency(
                    Guid.CreateVersion7(), Guid.CreateVersion7(), historicalCase.Id, 1, OriginalTime);
                var historical = ModerationFinding.Finalize(Guid.CreateVersion7(), historicalCase, 1,
                    historicalCase.Scope, null, null,
                    rule, rule, rule, rule,
                    SubjectReference.Create(ModerationSubjectKind.Human, database.AdministratorId, database.Instance),
                    evidence, "Historical committed finding, not current release permission.", OriginalTime);
                fixture.HistoricalFindingId = historical.Id;
                writer.AddRange(historicalCase, historical);
                await writer.SaveChangesAsync();
            }
            if (nativeAttribution)
            {
                fixture.NativeAttribution = true;
                await fixture.OpenAttributedResourceAsync(eventId, attributeSiblingResource);
            }
            else
            {
                var opened = database.OpenCase(fixture.Scope) with
                {
                    SubjectKind = ModerationSubjectKind.Content,
                    SubjectId = eventId
                };
                fixture.CaseId = opened.CaseId;
                await fixture.SourceAsync(database.AdministratorId, opened);
            }
            fixture.PolicySourceId = Guid.CreateVersion7();
            await fixture.SourceAsync(database.AdministratorId, new PublishModerationQualificationPolicySource
            {
                OperationId = Guid.CreateVersion7(),
                Target = fixture.Scope,
                ExpectedQualificationRevision = 1,
                SourceId = fixture.PolicySourceId,
                CaseId = fixture.CaseId,
                ExpectedCaseRevision = 1,
                ExpectedSourceRevision = 0,
                MaximumReviewerLifetime = TimeSpan.FromHours(1),
                MaximumUrgencyLifetime = TimeSpan.FromMinutes(1),
                ValidUntil = fixture.Clock.Now.AddHours(1)
            });
            long generation;
            await using (var reader = database.Context())
            {
                reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
                generation = await reader.ModerationQualificationPolicySources.Where(row => row.Id == fixture.PolicySourceId)
                    .Select(row => row.WriterGeneration).SingleAsync();
            }
            fixture.PolicyAdoptionId = Guid.CreateVersion7();
            await fixture.SourceAsync(database.UrgencyAssessorId, new AdoptModerationQualificationPolicySource
            {
                OperationId = Guid.CreateVersion7(),
                Target = fixture.Scope,
                ExpectedQualificationRevision = 1,
                SourceId = fixture.PolicySourceId,
                ExpectedSourceRevision = 1,
                ExpectedWriterGeneration = generation,
                AdoptionId = fixture.PolicyAdoptionId,
                CaseId = fixture.CaseId,
                ExpectedCaseRevision = 1,
                ValidUntil = fixture.Clock.Now.AddMinutes(50)
            });
            foreach (var human in new[] { database.ReviewerId, database.UrgencyAssessorId, database.AdministratorId })
            {
                var qualifier = human == database.AdministratorId ? database.UrgencyAssessorId : database.AdministratorId;
                var assignment = Guid.CreateVersion7();
                fixture.Assignments.Add(human, assignment);
                await fixture.SourceAsync(qualifier, new AssignModerationReviewerSource
                {
                    OperationId = Guid.CreateVersion7(),
                    Target = fixture.Scope,
                    ExpectedQualificationRevision = 1,
                    AssignmentId = assignment,
                    CaseId = fixture.CaseId,
                    ExpectedCaseRevision = 1,
                    ReviewerHumanId = human
                });
                await fixture.SourceAsync(qualifier, new QualifyModerationReviewerController
                {
                    OperationId = Guid.CreateVersion7(),
                    Target = fixture.Scope,
                    ExpectedQualificationRevision = 1,
                    AssignmentId = assignment,
                    ExpectedAssignmentRevision = 1,
                    ControllerHumanId = human,
                    EvidenceId = Guid.CreateVersion7(),
                    Justification = "Current independently established canonical controller.",
                    QualifiedUntil = fixture.Clock.Now.AddMinutes(30)
                });
                await fixture.SourceAsync(qualifier, new AssessModerationReviewerConflict
                {
                    OperationId = Guid.CreateVersion7(),
                    Target = fixture.Scope,
                    ExpectedQualificationRevision = 1,
                    AssignmentId = assignment,
                    ExpectedAssignmentRevision = 2,
                    State = ModerationReviewerConflictState.Independent,
                    EvidenceId = Guid.CreateVersion7(),
                    Justification = "Independent current reviewer for this material-safety case."
                });
            }
            fixture.Deadline = fixture.Clock.Now.AddMinutes(10);
            if (prepare) await fixture.PrepareAsync(fixture.CauseId, approvals);
            return fixture;
        }

        public async Task PrepareAsync(Guid causeId, int approvals = 1)
        {
            CauseId = causeId;
            var purpose = Intent(Author, ResourceSafetyReviewStep.ProposePurpose, null) with
            {
                RequiredApprovals = approvals,
                MaximumEvidenceLifetime = TimeSpan.FromMinutes(10)
            };
            await AcceptAsync(Author, purpose);
            PurposeId = purpose.OperationId;
            var adoption = Intent(Approver, ResourceSafetyReviewStep.AdoptPurpose, PurposeId);
            await AcceptAsync(Approver, adoption);
            var evidence = Intent(Author, ResourceSafetyReviewStep.AttestMaterialSafety, adoption.OperationId) with
            {
                Attestation = ResourceSafetyAttestation.IndependentlyInspectedSafeForCurrentAudience
            };
            await AcceptAsync(Author, evidence);
            EvidenceId = evidence.OperationId;
            var approval = Intent(Approver, ResourceSafetyReviewStep.ApproveSafety, EvidenceId);
            await AcceptAsync(Approver, approval);
            ApprovalId = approval.OperationId;
        }

        public ModerationResourceSafetyReviewIntent Intent(Guid human, ResourceSafetyReviewStep step, Guid? predecessor) => new()
        {
            OperationId = Guid.CreateVersion7(),
            QuarantineId = CauseId,
            ResourceId = ResourceId,
            MaterialVersionId = MaterialId,
            CaseId = CaseId,
            ExpectedCaseRevision = 1,
            ReviewerAssignmentId = Assignments[human],
            ExpectedReviewerRevision = 3,
            Step = step,
            PredecessorId = predecessor,
            ValidUntil = Deadline
        };

        public ModerationResourceQuarantineReleaseIntent ReleaseIntent() =>
            new(Guid.CreateVersion7(), CauseId, ResourceId, MaterialId, ApprovalId);

        public async Task AcceptAsync(Guid human, ModerationResourceSafetyReviewIntent intent)
        {
            await using var context = Context();
            var identity = Identity(context, human);
            var response = await new RecordModerationResourceSafetyReviewCommandHandler(
                new ModerationResourceQuarantineReleaseRepository(context, identity.Admin, identity.Machine, Clock))
                .ExecuteAsync(new(intent));
            await Assert.That(response.IsSuccess).IsTrue();
            await Assert.That(response.Id).IsEqualTo(intent.OperationId);
        }

        public async Task<ModerationResourceSafetyReviewResult> ReviewAsync(
            Guid human, ModerationResourceSafetyReviewIntent intent, params IInterceptor[] interceptors)
        {
            await using var context = Context(interceptors);
            var identity = Identity(context, human);
            return await new ModerationResourceQuarantineReleaseRepository(context, identity.Admin, identity.Machine, Clock)
                .ReviewAsync(intent);
        }

        public async Task<ModerationResourceQuarantineReleaseResult> ReleaseAsync(
            ModerationResourceQuarantineReleaseIntent intent, params IInterceptor[] interceptors)
        {
            await using var context = Context(interceptors);
            var identity = Identity(context, Approver);
            return await new ModerationResourceQuarantineReleaseRepository(context, identity.Admin, identity.Machine, Clock)
                .ReleaseAsync(intent);
        }

        public async Task SourceAsync(Guid human, ModerationQualificationMutation mutation)
        {
            var result = await WriteSourceAsync(human, mutation);
            await Assert.That(result.Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        }

        public async Task<ModerationQualificationWriteResult> WriteSourceAsync(
            Guid human, ModerationQualificationMutation mutation)
        {
            await using var context = Context();
            context.ModerationTrustDomainId = Database.Scope.TrustDomainId;
            var generation = await context.ModerationQualificationEpochs.AsNoTracking()
                .Where(row => row.Id == Database.EpochId).Select(row => row.Revision).SingleAsync();
            context.ModerationTrustDomainId = null;
            var identity = Identity(context, human);
            var caller = new AttributionCaller(human);
            context.CurrentUserService = caller;
            var publications = new ModerationPolicyPublicationRepository(context, identity.Admin, identity.Machine,
                Database.Inventory, null, Clock);
            var activation = new ModerationNativeActivationQualification(context, Database.Inventory, null,
                new ModerationQualificationPolicySourceOwner(context, Clock), Clock);
            return await new ModerationQualificationRepository(context, identity.Admin, identity.Machine,
                publications, activation, Clock, Database.Privacy.Authority(context, Clock),
                caseAttribution: NativeAttribution ? AttributionOwner(context, caller) : null)
                .ExecuteAsync(mutation with { ExpectedQualificationRevision = generation });
        }

        public ValueTask DisposeAsync() => Database.DisposeAsync();
    }

    private static (AdminContext Admin, MachinePrincipalAccessor Machine) Identity(ExploreDbContext context, Guid human)
    {
        var http = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, human.ToString()), new Claim(ClaimTypes.Role, "platform.admin")],
                    "resource-safety-witness"))
            }
        };
        return (new AdminContext(http, new PlatformUserRoleRepository(context), new TenantUserRoleGrantRepository(context),
            new OrganizationMemberRepository(context), new GroupMemberRepository(context), new UserExternalLoginRepository(context),
            NullLogger<AdminContext>.Instance), new MachinePrincipalAccessor(http));
    }

    private sealed class ReleaseClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = OriginalTime.AddDays(2);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class ExpireAfterFlush(ReleaseClock clock, DateTimeOffset deadline) : SaveChangesInterceptor
    {
        public bool Observed { get; private set; }
        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            Observed = true;
            clock.Now = deadline;
            return ValueTask.FromResult(result);
        }
    }
}
