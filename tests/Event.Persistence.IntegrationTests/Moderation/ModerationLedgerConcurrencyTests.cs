using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Models.InternalEvents;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using ISLAMU.Moderation.Application;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NSubstitute;
using System.Text.Json;

namespace Event.Persistence.IntegrationTests.Moderation;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class ModerationLedgerConcurrencyTests(PostgreSqlContainerFixture fixture)
{
    private static readonly TimeProvider Clock = new CaptureClock();

    [Test]
    public async Task Inert_proposal_captures_original_state_atomically_without_disclosure_or_completion()
    {
        var seed = await SeedAsync();
        var operation = await ProposalAsync(seed);
        await using var context = BoundContext(seed.Scope);
        await using var privacyFixture = await ModerationNoticePrivacyFixture.CreateAsync();
        var privacyAuthority = privacyFixture.Authority(context, Clock);
        var authority = new RoleAuthority(context, seed);
        var result = await Service(context, authority, privacyAuthority).ExecuteAsync(operation);
        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Known);

        context.ModerationTrustDomainId = seed.Scope.TrustDomainId;
        var original = await context.ModerationNoticeSources.AsNoTracking().SingleAsync();
        var captured = JsonSerializer.Deserialize<ModerationNoticeCaptureSnapshot>(original.SnapshotJson)!;
        await Assert.That(captured.HasNoSubstantiveWork).IsTrue();
        await Assert.That(captured.Decision.Revision).IsEqualTo(1);
        await Assert.That(captured.Notice).IsNull();
        await Assert.That(await context.ModerationNotices.AnyAsync()).IsFalse();
        await Assert.That((await context.ModerationEffectIntents.AsNoTracking().SingleAsync()).CompletedAt).IsNull();
        await Assert.That(await context.ModerationOperationReceipts.CountAsync()).IsEqualTo(1);
        context.ModerationTrustDomainId = null;

        var response = ModerationOperation.Transition(ModerationOperationKind.RecordResponse,
            seed.Subject, seed.Scope, Guid.CreateVersion7(), Guid.CreateVersion7(),
            operation.DecisionId, 1, 1, Guid.Empty);
        await Assert.That((await Service(context, authority, privacyAuthority).ExecuteAsync(response)).Outcome)
            .IsEqualTo(ModerationOperationOutcome.Known);
        await Assert.That((await Service(context, authority, privacyAuthority).ExecuteAsync(operation)).Revision).IsEqualTo(1);
        context.ModerationTrustDomainId = seed.Scope.TrustDomainId;
        var retained = await context.ModerationNoticeSources.AsNoTracking()
            .SingleAsync(row => row.EffectId == operation.EffectId);
        await Assert.That(retained.SnapshotJson).IsEqualTo(original.SnapshotJson);
        await Assert.That(await context.ModerationEffectIntents.CountAsync()).IsEqualTo(2);
        await Assert.That(await context.ModerationNoticeSources.CountAsync()).IsEqualTo(2);
        await Assert.That(await context.OutboxMessages.CountAsync(row =>
            row.EventType == ModerationEffectOutboxMessageFactory.EventType)).IsEqualTo(2);
    }

    [Test]
    public async Task Abort_after_capture_and_shared_flush_rolls_back_every_writer_artifact()
    {
        var seed = await SeedAsync();
        var operation = await ProposalAsync(seed);
        await using var context = BoundContext(seed.Scope);
        await using var privacyFixture = await ModerationNoticePrivacyFixture.CreateAsync();
        var privacyAuthority = privacyFixture.Authority(context, Clock);
        var authority = new RoleAuthority(context, seed);
        var capture = ModerationLedgerPersistenceTests.Capture(context, privacyAuthority, authority, Clock);
        var store = new ModerationStore(context, authority, Clock, privacyAuthority, capture);
        var result = await store.ExecuteAsync(operation, async (session, token) =>
        {
            var grant = (await authority.ResolveAsync(session, operation, token)).Grant!;
            var proposal = operation.Proposal!;
            var decision = ModerationDecision.Propose(operation.DecisionId, proposal.CaseId,
                seed.Subject, grant.Principal, seed.Scope, seed.Scope, proposal.Rule, proposal.Capabilities,
                proposal.StartsAt, proposal.EndsAt, proposal.ReviewAt, proposal.RequiredApprovals, Clock.GetUtcNow());
            var effect = new ChangePointer().Plan(operation, decision, null, null);
            await session.SaveAsync(decision, null, null, grant, effect, token);
            var first = context.ModerationNoticeSources.Local.Single();
            var repeated = await capture.CaptureInCurrentTransactionAsync(session, effect.Id, grant, token);
            await Assert.That(repeated!.SnapshotHash).IsEqualTo(first.SnapshotHash);
            await context.SaveChangesAsync(token);
            return ModerationOperationResult.Unavailable();
        }, CancellationToken.None);
        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Unavailable);
        context.ModerationTrustDomainId = seed.Scope.TrustDomainId;
        await Assert.That(await context.ModerationDecisions.AnyAsync()).IsFalse();
        await Assert.That(await context.ModerationEffectIntents.AnyAsync()).IsFalse();
        await Assert.That(await context.ModerationOperationReceipts.AnyAsync()).IsFalse();
        await Assert.That(await context.ModerationNoticeSources.AnyAsync()).IsFalse();
        await Assert.That(await context.OutboxMessages.AnyAsync()).IsFalse();
    }

    [Test]
    public async Task A_finding_bearing_transition_without_an_owned_finding_is_denied_before_capture()
    {
        var seed = await SeedAsync();
        var proposal = await ProposalAsync(seed);
        await using var context = BoundContext(seed.Scope);
        await using var privacyFixture = await ModerationNoticePrivacyFixture.CreateAsync();
        var privacyAuthority = privacyFixture.Authority(context, Clock);
        var authority = new RoleAuthority(context, seed);
        await Assert.That((await Service(context, authority, privacyAuthority).ExecuteAsync(proposal)).Outcome)
            .IsEqualTo(ModerationOperationOutcome.Known);
        var response = ModerationOperation.Transition(ModerationOperationKind.RecordResponse,
            seed.Subject, seed.Scope, Guid.CreateVersion7(), Guid.CreateVersion7(), proposal.DecisionId, 1, 1, Guid.Empty);
        await Assert.That((await Service(context, authority, privacyAuthority).ExecuteAsync(response)).Outcome)
            .IsEqualTo(ModerationOperationOutcome.Known);
        var finding = ModerationOperation.Transition(ModerationOperationKind.AcceptFinding,
            seed.Subject, seed.Scope, Guid.CreateVersion7(), Guid.CreateVersion7(),
            proposal.DecisionId, 2, 1, Guid.CreateVersion7());
        await Assert.That((await Service(context, authority, privacyAuthority).ExecuteAsync(finding)).Outcome)
            .IsEqualTo(ModerationOperationOutcome.Denied);
        context.ModerationTrustDomainId = seed.Scope.TrustDomainId;
        var retained = await context.ModerationDecisions.AsNoTracking().SingleAsync();
        await Assert.That(retained.State).IsEqualTo(ModerationDecisionState.AwaitingFinding);
        await Assert.That(retained.FindingId).IsNull();
        await Assert.That(await context.ModerationOperationReceipts.AnyAsync(row => row.Id == finding.OperationId)).IsFalse();
        await Assert.That(await context.ModerationEffectIntents.AnyAsync(row => row.Id == finding.EffectId)).IsFalse();
    }

    [Test]
    public async Task Receipt_without_original_capture_cannot_replay_as_a_complete_writer_commit()
    {
        var seed = await SeedAsync();
        var operation = await ProposalAsync(seed);
        await using var context = BoundContext(seed.Scope);
        await using var privacyFixture = await ModerationNoticePrivacyFixture.CreateAsync();
        var privacyAuthority = privacyFixture.Authority(context, Clock);
        var authority = new RoleAuthority(context, seed);
        await Assert.That((await Service(context, authority, privacyAuthority).ExecuteAsync(operation)).Outcome)
            .IsEqualTo(ModerationOperationOutcome.Known);
        context.ModerationTrustDomainId = seed.Scope.TrustDomainId;
        await context.ModerationNoticeSources.Where(row => row.EffectId == operation.EffectId).ExecuteDeleteAsync();
        context.ModerationTrustDomainId = null;
        await Assert.That((await Service(context, authority, privacyAuthority).ExecuteAsync(operation)).Outcome)
            .IsEqualTo(ModerationOperationOutcome.Unavailable);
        context.ModerationTrustDomainId = seed.Scope.TrustDomainId;
        await Assert.That(await context.ModerationNoticeSources.AnyAsync()).IsFalse();
        await Assert.That((await context.ModerationEffectIntents.AsNoTracking().SingleAsync()).CompletedAt).IsNull();
    }

    [Test]
    public async Task Native_qualification_row_remains_locked_through_the_owner_transaction()
    {
        var seed = await SeedAsync();
        await using var owner = BoundContext(seed.Scope);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var privacyFixture = await ModerationNoticePrivacyFixture.CreateAsync();
        var privacyAuthority = privacyFixture.Authority(owner, Clock);
        var authority = new RoleAuthority(owner, seed, entered, release);
        var service = Service(owner, authority, privacyAuthority);
        var operation = service.ExecuteAsync(
            ModerationOperation.Evaluate(seed.Subject, seed.Scope, ModerationCapability.Publish));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await using var contender = BoundContext(seed.Scope);
            await using var transaction = await contender.Database.BeginTransactionAsync();
            var entity = contender.Model.FindEntityType(typeof(TenantUserRoleGrant))!;
            var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            var sql = contender.GetService<ISqlGenerationHelper>();
            var table = sql.DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
            var key = sql.DelimitIdentifier(entity.FindProperty(nameof(TenantUserRoleGrant.Id))!.GetColumnName(store)!);
            var blocked = false;
            try
            {
                await using var probe = contender.Database.GetDbConnection().CreateCommand();
                probe.Transaction = transaction.GetDbTransaction();
                probe.CommandText = $"SELECT {key} FROM {table} WHERE {key} = @grant FOR UPDATE NOWAIT";
                var grantId = probe.CreateParameter();
                grantId.ParameterName = "grant";
                grantId.Value = seed.GrantId;
                probe.Parameters.Add(grantId);
                await probe.ExecuteScalarAsync(CancellationToken.None);
            }
            catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.LockNotAvailable)
            {
                blocked = true;
            }

            await Assert.That(blocked).IsTrue();
        }
        finally
        {
            release.TrySetResult();
        }
        await Assert.That((await operation).Outcome).IsEqualTo(ModerationOperationOutcome.Known);

        await using var writer = BoundContext(seed.Scope);
        var repository = new TenantUserRoleGrantRepository(writer);
        var grant = (await repository.GetById(seed.GrantId))!;
        grant.RevokedAt = new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);
        await repository.Update(grant);
        await using var nextOwner = BoundContext(seed.Scope);
        var nextAuthority = new RoleAuthority(nextOwner, seed);
        var next = await Service(nextOwner, nextAuthority, privacyFixture.Authority(nextOwner, Clock)).ExecuteAsync(
            ModerationOperation.Evaluate(seed.Subject, seed.Scope, ModerationCapability.Publish));
        await Assert.That(next.Outcome).IsEqualTo(ModerationOperationOutcome.Denied);
    }

    [Test]
    public async Task Qualification_revocation_committed_first_cannot_be_bypassed_by_a_cached_grant()
    {
        var seed = await SeedAsync();
        await using var owner = BoundContext(seed.Scope);
        await using var privacyFixture = await ModerationNoticePrivacyFixture.CreateAsync();
        var privacyAuthority = privacyFixture.Authority(owner, Clock);
        var authority = new RoleAuthority(owner, seed);
        var operation = ModerationOperation.Evaluate(seed.Subject, seed.Scope, ModerationCapability.Publish);
        await Assert.That((await Service(owner, authority, privacyAuthority).ExecuteAsync(operation)).Outcome)
            .IsEqualTo(ModerationOperationOutcome.Known);
        await using (var writer = BoundContext(seed.Scope))
        {
            var repository = new TenantUserRoleGrantRepository(writer);
            var grant = (await repository.GetById(seed.GrantId))!;
            grant.RevokedAt = new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);
            await repository.Update(grant);
        }
        await Assert.That((await Service(owner, authority, privacyAuthority).ExecuteAsync(operation)).Outcome)
            .IsEqualTo(ModerationOperationOutcome.Denied);
    }

    private ModerationOperationService Service(
        ExploreDbContext context, RoleAuthority authority, IPrivacyIdentityFenceAuthority privacy) =>
        new(new ModerationStore(context, authority, Clock, privacy, ModerationLedgerPersistenceTests.Capture(context, privacy, authority, Clock)),
            authority, new ChangePointer(), Clock);

    private async Task<ModerationOperation> ProposalAsync(Seed seed)
    {
        var now = Clock.GetUtcNow();
        var moderationCase = ModerationCase.Open(Guid.CreateVersion7(), seed.Subject, seed.Scope, Guid.CreateVersion7(), now);
        await using var context = BoundContext(seed.Scope);
        context.ModerationTrustDomainId = seed.Scope.TrustDomainId;
        context.ModerationCases.Add(moderationCase);
        await context.SaveChangesAsync();
        return ModerationOperation.Propose(seed.Subject, seed.Scope, Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), 1, 1, new ModerationProposal(moderationCase.Id,
                ModerationRuleReference.Create(seed.Scope.Id, "capture-rule", 1),
                [ModerationCapability.Publish], now, now.AddDays(1), null, 1));
    }

    private ExploreDbContext BoundContext(AuthorityScope scope)
    {
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(scope.Id);
        return fixture.CreateTenantFilteredDbContext(tenant, PostgresTenantSessionInterceptor.Instance);
    }

    private async Task<Seed> SeedAsync()
    {
        await fixture.ResetAsync();
        var scope = AuthorityScope.Tenant(AuthorityScope.Instance(
            AuthorityScope.Solution(AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()),
            Guid.CreateVersion7()), Guid.CreateVersion7());
        var principalId = Guid.CreateVersion7();
        var user = new User
        {
            Id = principalId,
            ConcurrencyStamp = Guid.CreateVersion7(),
            Pii = new UserPii
            {
                UserId = principalId, Email = $"{principalId:N}@example.invalid", FirstName = "Test", LastName = "Reviewer"
            }
        };
        var tenant = new Tenant
        {
            Id = scope.Id, FullName = "Moderation race scope", Slug = $"moderation-{scope.Id:N}",
            TenantStatusId = (int)TenantStatusEnum.Active, TenantStatus = null!
        };
        var membership = new TenantUser
        {
            Id = Guid.CreateVersion7(), TenantId = tenant.Id, Tenant = tenant, UserId = user.Id, User = user,
            StatusId = (int)TenantUserStatusEnum.Active
        };
        var grant = new TenantUserRoleGrant
        {
            Id = Guid.CreateVersion7(), TenantId = tenant.Id, Tenant = tenant,
            TenantUserId = membership.Id, TenantUser = membership,
            RoleId = (int)RoleEnum.TenantAdmin, RoleScopeId = (int)RoleScopeEnum.Tenant, Role = null!
        };
        await using var context = fixture.CreateDbContext();
        context.AddRange(user, tenant, membership, grant);
        var service = new ServicePrincipal
        {
            Id = Guid.CreateVersion7(), Code = $"capture-{scope.Id:N}", DisplayName = "Capture integration test"
        };
        var host = ModerationHostBinding.Establish(Guid.CreateVersion7(),
            scope.TrustDomainId, scope.Parent!.Parent!.Id, scope.Parent.Id, service.Id);
        context.AddRange(service, host, ModerationQualificationEpoch.Initialize(Guid.CreateVersion7(), host, tenant));
        await context.SaveChangesAsync();
        return new Seed(scope, SubjectReference.Create(ModerationSubjectKind.Human, principalId, scope),
            grant.Id, membership.Id);
    }

    private sealed record Seed(AuthorityScope Scope, SubjectReference Subject, Guid GrantId, Guid MembershipId);

    private sealed class CaptureClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class RoleAuthority(
        ExploreDbContext context, Seed seed, TaskCompletionSource? entered = null,
        TaskCompletionSource? release = null) : IModerationAuthorityParticipant
    {
        public async Task<ModerationAuthorityResolution> AcquireAsync(
            IModerationSession session, ModerationOperation operation, CancellationToken token)
        {
            await RelationalEntityRowFence.AcquireGlobalAsync<Tenant>(context, seed.Scope.Id, token);
            await RelationalEntityRowFence.AcquireGlobalAsync<User>(context, seed.Subject.Id, token);
            await RelationalEntityRowFence.AcquireAsync<TenantUser>(context, seed.Scope.Id,
                entity => entity.Id, seed.MembershipId, token);
            await RelationalEntityRowFence.AcquireAsync<TenantUserRoleGrant>(context, seed.Scope.Id,
                entity => entity.Id, seed.GrantId, token);
            await RelationalEntityRowFence.AcquireLookupAsync<Role>(context, [(int)RoleEnum.TenantAdmin], token);
            var result = await ResolveAsync(session, operation, token);
            entered?.TrySetResult();
            if (release is not null) await release.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
            return result;
        }

        public async Task<ModerationAuthorityResolution> ResolveAsync(
            IModerationSession session, ModerationOperation operation, CancellationToken token)
        {
            var qualified = await context.TenantUserRoleGrants.AsNoTracking().AnyAsync(
                grant => grant.Id == seed.GrantId && grant.TenantId == seed.Scope.Id && grant.RevokedAt == null
                    && grant.RoleId == (int)RoleEnum.TenantAdmin && grant.TenantUser.UserId == seed.Subject.Id
                    && !grant.TenantUser.IsDeleted && grant.TenantUser.StatusId == (int)TenantUserStatusEnum.Active,
                token);
            return qualified
                ? ModerationAuthorityResolution.Known(new ModerationAuthorityGrant(
                    seed.Subject, operation.Subject, seed.Scope, operation.Kind, 1, 1, ModerationAuthorityState.Known))
                : ModerationAuthorityResolution.Denied();
        }

        public Task<ModerationAuthorityState> QualifyActivationAsync(
            IModerationSession session, ModerationDecision decision, ModerationAuthorityGrant grant,
            CancellationToken token) => Task.FromResult(ModerationAuthorityState.Unavailable);
    }

    private sealed class ChangePointer : IModerationEffectPlanner
    {
        public ModerationPlannedEffect Plan(
            ModerationOperation operation, ModerationDecision decision, ModerationSanction? sanction,
            ModerationSanction? supersededSanction) =>
            new(operation.EffectId, operation.OperationId, decision.Id, decision.Revision,
                sanction?.Id, sanction?.Revision, decision.Subject, decision.Scope, operation.Kind,
                supersededSanction?.Id, supersededSanction?.Revision);
    }
}
