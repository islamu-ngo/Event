using System.Security.Claims;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Handlers.Commands;
using Explore.Application.Features.Moderation.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Identity;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using ISLAMU.Moderation.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed class ModerationQualificationWriterTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Exact_retry_returns_retained_source_revision_before_stale_generation_rejection()
    {
        await using var database = await SourceDatabase.CreateAsync();
        var command = database.OpenCase();
        var first = await database.ExecuteAsync(database.AdministratorId, command);
        var repeated = await database.ExecuteAsync(database.AdministratorId, command);
        await Assert.That(first.IsSuccess).IsTrue();
        await Assert.That(repeated.IsSuccess).IsTrue();
        await Assert.That(repeated.Id).IsEqualTo(first.Id);
        await Assert.That(repeated.Revision).IsEqualTo(first.Revision);
        await Assert.That(repeated.QualificationRevision).IsEqualTo(2L);
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That(await reader.ModerationCases.CountAsync()).IsEqualTo(1);
        await Assert.That(await reader.ModerationQualificationMutationReceipts.CountAsync()).IsEqualTo(1);
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(2L);
    }

    [Test]
    public async Task Changed_request_reuse_cannot_return_another_sources_receipt()
    {
        await using var database = await SourceDatabase.CreateAsync();
        var command = database.OpenCase();
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, command)).IsSuccess).IsTrue();
        var changed = await database.ExecuteAsync(database.AdministratorId, command with { CaseId = Guid.CreateVersion7() });
        await Assert.That(changed.FailureCode).IsEqualTo(FailureCodes.ConcurrencyConflict);
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That(await reader.ModerationCases.CountAsync()).IsEqualTo(1);
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(2L);
    }

    [Test]
    public async Task Stale_new_operation_leaves_sources_receipts_and_generation_unchanged()
    {
        await using var database = await SourceDatabase.CreateAsync();
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, database.OpenCase())).IsSuccess).IsTrue();
        var stale = await database.ExecuteAsync(database.AdministratorId, database.OpenCase());
        await Assert.That(stale.FailureCode).IsEqualTo(FailureCodes.ConcurrencyConflict);
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That(await reader.ModerationCases.CountAsync()).IsEqualTo(1);
        await Assert.That(await reader.ModerationQualificationMutationReceipts.CountAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task Claimed_platform_admin_without_actual_native_assignment_cannot_write_sources()
    {
        await using var database = await SourceDatabase.CreateAsync();
        var denied = await database.ExecuteAsync(database.SubjectId, database.OpenCase());
        await Assert.That(denied.FailureCode).IsEqualTo(FailureCodes.AdminRequired);
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That(await reader.ModerationCases.CountAsync()).IsEqualTo(0);
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(1L);
    }

    [Test]
    public async Task Machine_owned_key_cannot_borrow_its_native_human_administrators_privilege()
    {
        await using var database = await SourceDatabase.CreateAsync();
        var denied = await database.ExecuteAsync(database.AdministratorId, database.OpenCase(), machineCaller: true);
        await Assert.That(denied.FailureCode).IsEqualTo(FailureCodes.AdminRequired);
        await using var reader = database.Context();
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(1L);
    }

    [Test]
    public async Task Revoked_native_role_denies_an_identical_previously_committed_operation()
    {
        await using var database = await SourceDatabase.CreateAsync();
        var command = database.OpenCase();
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, command)).IsSuccess).IsTrue();
        await using (var writer = database.Context())
        {
            writer.PlatformUserRoles.Remove(await writer.PlatformUserRoles.SingleAsync(row => row.UserId == database.AdministratorId));
            await writer.SaveChangesAsync();
        }
        var denied = await database.ExecuteAsync(database.AdministratorId, command);
        await Assert.That(denied.FailureCode).IsEqualTo(FailureCodes.AdminRequired);
    }

    [Test]
    public async Task Foreign_tenant_body_cannot_reparent_the_bound_primary_store()
    {
        await using var database = await SourceDatabase.CreateAsync();
        var foreign = AuthorityScope.Tenant(database.Instance, Guid.CreateVersion7());
        var denied = await database.ExecuteAsync(database.AdministratorId, database.OpenCase() with { Target = foreign });
        await Assert.That(denied.FailureCode).IsEqualTo(FailureCodes.AdminRequired);
        await using var reader = database.Context();
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(1L);
    }

    [Test]
    public async Task Actual_subject_response_remains_available_without_admin_or_active_membership()
    {
        await using var database = await SourceDatabase.CreateAsync();
        var opened = database.OpenCase();
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, opened)).IsSuccess).IsTrue();
        await using (var writer = database.Context())
        {
            var membership = await writer.TenantUsers.SingleAsync(row => row.UserId == database.SubjectId);
            membership.StatusId = (int)TenantUserStatusEnum.Suspended;
            await writer.SaveChangesAsync();
        }
        var response = new RecordModerationCaseResponse
        {
            OperationId = Guid.CreateVersion7(), Target = database.Scope, ExpectedQualificationRevision = 2,
            EvidenceId = Guid.CreateVersion7(), CaseId = opened.CaseId, ExpectedCaseRevision = 1,
            Statement = "I dispute the recorded occurrence."
        };
        var accepted = await database.ExecuteAsync(database.SubjectId, response);
        await Assert.That(accepted.IsSuccess).IsTrue();
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        var evidence = await reader.ModerationQualificationEvidence.SingleAsync();
        await Assert.That(evidence.AssertedByHumanId).IsEqualTo(database.SubjectId);
        await Assert.That(evidence.SubjectHumanId).IsEqualTo((Guid?)database.SubjectId);
    }

    [Test]
    public async Task Native_administrator_cannot_fabricate_the_subjects_response()
    {
        await using var database = await SourceDatabase.CreateAsync();
        var opened = database.OpenCase();
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, opened)).IsSuccess).IsTrue();
        var denied = await database.ExecuteAsync(database.AdministratorId, new RecordModerationCaseResponse
        {
            OperationId = Guid.CreateVersion7(), Target = database.Scope, ExpectedQualificationRevision = 2,
            EvidenceId = Guid.CreateVersion7(), CaseId = opened.CaseId, ExpectedCaseRevision = 1,
            Statement = "An administrator cannot author this subject response."
        });
        await Assert.That(denied.FailureCode).IsEqualTo(FailureCodes.AdminRequired);
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That(await reader.ModerationQualificationEvidence.CountAsync()).IsEqualTo(0);
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(2L);
    }

    [Test]
    public async Task Native_policy_replacement_preserves_immutable_meaning_and_stays_inactive()
    {
        await using var database = await SourceDatabase.CreateAsync();
        var adoption = database.AdoptPolicy(1);
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, adoption)).IsSuccess).IsTrue();
        var replacement = adoption with
        {
            OperationId = Guid.CreateVersion7(), ExpectedQualificationRevision = 2,
            ExpectedPolicyRevision = 1, PublicationId = Guid.CreateVersion7()
        };
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, replacement)).IsSuccess).IsTrue();
        var changed = replacement with
        {
            OperationId = Guid.CreateVersion7(), ExpectedQualificationRevision = 3,
            ExpectedPolicyRevision = 2, PublicationId = Guid.CreateVersion7(),
            Rules = [replacement.Rules.Single() with { Meaning = "A silently changed adopted meaning." }]
        };
        var rejected = await database.ExecuteAsync(database.AdministratorId, changed);
        await Assert.That(rejected.FailureCode).IsEqualTo(FailureCodes.ConcurrencyConflict);
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        var policy = await reader.ModerationPolicies.Include(row => row.Rules).SingleAsync();
        policy.ValidatePersistedState();
        await Assert.That(policy.Revision).IsEqualTo(2L);
        await Assert.That(policy.Rules.Single().Meaning).IsEqualTo(adoption.Rules.Single().Meaning);
        await Assert.That(policy.IsActive).IsFalse();
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(3L);
    }

    [Test]
    public async Task Reviewer_cannot_attest_their_own_controller_even_with_native_admin_role()
    {
        await using var database = await SourceDatabase.CreateAsync();
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, database.AdoptPolicy(1))).IsSuccess).IsTrue();
        var opened = database.OpenCase() with { ExpectedQualificationRevision = 2 };
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, opened)).IsSuccess).IsTrue();
        var assignmentId = Guid.CreateVersion7();
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, new AssignModerationReviewerSource
        {
            OperationId = Guid.CreateVersion7(), Target = database.Scope, ExpectedQualificationRevision = 3,
            AssignmentId = assignmentId, CaseId = opened.CaseId, ExpectedCaseRevision = 1,
            ReviewerHumanId = database.ReviewerId
        })).IsSuccess).IsTrue();
        var denied = await database.ExecuteAsync(database.ReviewerId, new QualifyModerationReviewerController
        {
            OperationId = Guid.CreateVersion7(), Target = database.Scope, ExpectedQualificationRevision = 4,
            AssignmentId = assignmentId, ExpectedAssignmentRevision = 1, ControllerHumanId = database.ReviewerId,
            EvidenceId = Guid.CreateVersion7(), Justification = "Attempted self-qualification.",
            QualifiedUntil = Now.AddMinutes(30)
        });
        await Assert.That(denied.FailureCode).IsEqualTo(FailureCodes.AdminRequired);
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That((await reader.ModerationReviewerAssignments.SingleAsync()).ConflictState)
            .IsEqualTo(ModerationReviewerConflictState.Unresolved);
        await Assert.That(await reader.ModerationQualificationEvidence.CountAsync()).IsEqualTo(0);
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(4L);
    }

    [Test]
    public async Task Qualified_native_finding_and_reversal_preserve_actual_response_reviewer_and_audit()
    {
        await using var database = await SourceDatabase.CreateAsync();
        var adoption = database.AdoptPolicy(1);
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, adoption)).IsSuccess).IsTrue();
        var opened = database.OpenCase() with { ExpectedQualificationRevision = 2 };
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, opened)).IsSuccess).IsTrue();
        var responseId = Guid.CreateVersion7();
        await Assert.That((await database.ExecuteAsync(database.SubjectId, new RecordModerationCaseResponse
        {
            OperationId = Guid.CreateVersion7(), Target = database.Scope, ExpectedQualificationRevision = 3,
            EvidenceId = responseId, CaseId = opened.CaseId, ExpectedCaseRevision = 1,
            Statement = "The actual subject responds before the final finding."
        })).IsSuccess).IsTrue();
        var assignmentId = Guid.CreateVersion7();
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, new AssignModerationReviewerSource
        {
            OperationId = Guid.CreateVersion7(), Target = database.Scope, ExpectedQualificationRevision = 4,
            AssignmentId = assignmentId, CaseId = opened.CaseId, ExpectedCaseRevision = 1,
            ReviewerHumanId = database.ReviewerId
        })).IsSuccess).IsTrue();
        var controllerEvidenceId = Guid.CreateVersion7();
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, new QualifyModerationReviewerController
        {
            OperationId = Guid.CreateVersion7(), Target = database.Scope, ExpectedQualificationRevision = 5,
            AssignmentId = assignmentId, ExpectedAssignmentRevision = 1, ControllerHumanId = database.ReviewerId,
            EvidenceId = controllerEvidenceId, Justification = "The independent native assessor records canonical control.",
            QualifiedUntil = Now.AddMinutes(30)
        })).IsSuccess).IsTrue();
        var conflictEvidenceId = Guid.CreateVersion7();
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, new AssessModerationReviewerConflict
        {
            OperationId = Guid.CreateVersion7(), Target = database.Scope, ExpectedQualificationRevision = 6,
            AssignmentId = assignmentId, ExpectedAssignmentRevision = 2, State = ModerationReviewerConflictState.Independent,
            EvidenceId = conflictEvidenceId, Justification = "The native assessor records the current case-specific independence."
        })).IsSuccess).IsTrue();
        var findingId = Guid.CreateVersion7();
        var rule = adoption.Rules.Single();
        var finalized = await database.ExecuteAsync(database.ReviewerId, new FinalizeModerationFindingSource
        {
            OperationId = Guid.CreateVersion7(), Target = database.Scope, ExpectedQualificationRevision = 7,
            FindingId = findingId, CaseId = opened.CaseId, ExpectedCaseRevision = 1,
            PolicyId = adoption.PolicyId, ExpectedPolicyRevision = 1,
            Rule = ModerationRuleReference.Create(database.Scope.Id, rule.Code, rule.Revision),
            EvidenceId = responseId, Justification = "The qualified native reviewer finalizes the adopted rule finding."
        });
        await Assert.That(finalized.IsSuccess).IsTrue();
        var reversalId = Guid.CreateVersion7();
        var reversed = await database.ExecuteAsync(database.AdministratorId, new ReverseModerationFindingSource
        {
            OperationId = Guid.CreateVersion7(), Target = database.Scope, ExpectedQualificationRevision = 8,
            FindingId = findingId, ExpectedFindingRevision = 1, ReversalReferenceId = reversalId,
            Justification = "The native correction preserves the adjudication and its response."
        });
        await Assert.That(reversed.IsSuccess).IsTrue();
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        var finding = await reader.ModerationFindings.SingleAsync();
        finding.ValidatePersistedState();
        await Assert.That(finding.State).IsEqualTo(ModerationFindingState.Reversed);
        await Assert.That(finding.Revision).IsEqualTo(2L);
        await Assert.That(finding.AdjudicatorId).IsEqualTo(database.ReviewerId);
        await Assert.That(finding.EvidenceReferenceId).IsEqualTo(responseId);
        await Assert.That(finding.ReversalReferenceId).IsEqualTo((Guid?)reversalId);
        await Assert.That(finding.ReversedById).IsEqualTo((Guid?)database.AdministratorId);
        await Assert.That(await reader.ModerationQualificationEvidence.CountAsync()).IsEqualTo(3);
        await Assert.That(await reader.ModerationQualificationMutationReceipts.CountAsync()).IsEqualTo(8);
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(9L);
    }

    private sealed class SourceDatabase : IAsyncDisposable, ITenantContext
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<ExploreDbContext> _options;

        private SourceDatabase(SqliteConnection connection)
        {
            _connection = connection;
            _options = TestDbContextOptions.Create<ExploreDbContext>().UseSqlite(connection)
                .UseSnakeCaseNamingConvention().AddInterceptors(SqliteNamedLockTransactionInterceptor.Instance).Options;
            Instance = AuthorityScope.Instance(
                AuthorityScope.Solution(AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()),
                Guid.CreateVersion7());
            Scope = AuthorityScope.Tenant(Instance, TenantId);
        }

        public Guid TenantId { get; } = Guid.CreateVersion7();
        public Guid AdministratorId { get; } = Guid.CreateVersion7();
        public Guid ReviewerId { get; } = Guid.CreateVersion7();
        public Guid SubjectId { get; } = Guid.CreateVersion7();
        public AuthorityScope Instance { get; }
        public AuthorityScope Scope { get; }
        public ExploreDbContext Context() => new(_options) { TenantContext = this };

        public static async Task<SourceDatabase> CreateAsync()
        {
            var connection = await SqliteTestDatabaseFactory.CreateOpenIsolatedConnectionAsync();
            await LocalIdentitySqliteTemplate.CopySeededApplicationToAsync(connection, CancellationToken.None);
            var database = new SourceDatabase(connection);
            await using var context = database.Context();
            var status = await context.TenantStatuses.SingleAsync(row => row.IsActiveState);
            context.Tenants.Add(new Tenant
            {
                Id = database.TenantId, FullName = "Qualification source tenant", Slug = $"source-{database.TenantId:N}",
                TenantStatusId = status.Id, TenantStatus = status
            });
            var role = await context.Roles.SingleAsync(row =>
                row.RoleScopeId == (int)RoleScopeEnum.Platform && row.MasterCode == "platform.admin");
            foreach (var id in new[] { database.AdministratorId, database.ReviewerId, database.SubjectId })
            {
                var user = new User
                {
                    Id = id, Pii = new UserPii
                    {
                        UserId = id, Email = $"{id:N}@example.invalid", FirstName = "Source", LastName = "Witness"
                    }
                };
                context.Users.Add(user);
                if (id != database.SubjectId)
                    context.PlatformUserRoles.Add(new PlatformUserRole
                    {
                        Id = Guid.CreateVersion7(), UserId = id, User = user, RoleId = role.Id, Role = role
                    });
                context.TenantUsers.Add(new TenantUser
                {
                    Id = Guid.CreateVersion7(), TenantId = database.TenantId, Tenant = null!,
                    UserId = id, User = user, StatusId = (int)TenantUserStatusEnum.Active
                });
            }
            var principal = new ServicePrincipal
            {
                Id = Guid.CreateVersion7(), Code = $"maintenance-{database.TenantId:N}", DisplayName = "Source maintenance"
            };
            context.ServicePrincipals.Add(principal);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            var identity = Identity(context, database.AdministratorId, false);
            var setup = await new EstablishModerationAuthoritySourcesCommandHandler(
                new ModerationAuthoritySourceRepository(context, identity.Admin, identity.Machine))
                .ExecuteAsync(new EstablishModerationAuthoritySourcesCommand
                {
                    HostBindingId = Guid.CreateVersion7(), TrustDomainId = database.Instance.TrustDomainId,
                    SolutionId = database.Instance.Parent!.Id, InstanceId = database.Instance.Id,
                    MaintenanceServicePrincipalId = principal.Id, TenantId = database.TenantId,
                    EpochId = Guid.CreateVersion7()
                });
            if (!setup.IsSuccess)
                throw new InvalidOperationException("The real native authority fixture did not install.");
            return database;
        }

        public OpenModerationCaseSource OpenCase() => new()
        {
            OperationId = Guid.CreateVersion7(), Target = Scope, ExpectedQualificationRevision = 1,
            CaseId = Guid.CreateVersion7(), SubjectKind = ModerationSubjectKind.Human,
            SubjectId = SubjectId, OccurrenceId = Guid.CreateVersion7()
        };

        public AdoptModerationPolicySource AdoptPolicy(long generation) => new()
        {
            OperationId = Guid.CreateVersion7(), Target = Scope, ExpectedQualificationRevision = generation,
            PolicyId = Guid.CreateVersion7(), ExpectedPolicyRevision = 0, PublicationId = Guid.CreateVersion7(),
            Rules =
            [
                new ModerationPolicyRuleDraft
                {
                    Id = Guid.CreateVersion7(), Code = "native-source-witness", Revision = 1,
                    Meaning = "Current source authority and protected response.",
                    Purposes = [ModerationPolicyOperationPurpose.Propose, ModerationPolicyOperationPurpose.Activate],
                    Capabilities = [ModerationCapability.Publish], MaximumFiniteDuration = TimeSpan.FromDays(1),
                    AllowsIndefinite = false, MaximumReviewDelay = TimeSpan.FromHours(1),
                    MinimumApprovals = 1, MaximumApprovals = 2
                }
            ]
        };

        public async Task<ModerationQualificationResponse> ExecuteAsync(
            Guid actor, ModerationQualificationMutation mutation, bool machineCaller = false)
        {
            await using var context = Context();
            var identity = Identity(context, actor, machineCaller);
            var handler = new ApplyModerationQualificationCommandHandler(
                new ModerationQualificationRepository(context, identity.Admin, identity.Machine, new FixedClock()));
            var response = await handler.ExecuteAsync(new(mutation));
            if (context.ModerationTrustDomainId is not null || context.ChangeTracker.HasChanges())
                throw new InvalidOperationException("The source writer did not clear its acquisition boundary.");
            return response;
        }

        private static (AdminContext Admin, MachinePrincipalAccessor Machine) Identity(
            ExploreDbContext context, Guid userId, bool machineCaller)
        {
            var http = new HttpContextAccessor
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim(ClaimTypes.Role, "platform.admin")],
                        "source-witness"))
                }
            };
            var admin = new AdminContext(http, new PlatformUserRoleRepository(context),
                new TenantUserRoleGrantRepository(context), new OrganizationMemberRepository(context),
                new GroupMemberRepository(context), new UserExternalLoginRepository(context), NullLogger<AdminContext>.Instance);
            var machine = new MachinePrincipalAccessor(http);
            if (machineCaller)
                machine.SetPrincipal(new ApiKeyPrincipalContext(Guid.CreateVersion7().ToString(), context.TenantFilterTenantId,
                    ExternalApiKeyOwnerType.User, userId, []));
            return (admin, machine);
        }

        public ValueTask DisposeAsync() => _connection.DisposeAsync();
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
