using System.Data.Common;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using ISLAMU.Moderation.Application;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using NSubstitute;

namespace Event.Persistence.IntegrationTests.Moderation;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class ModerationLedgerPersistenceTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Identical_mutation_retry_returns_its_original_receipt_without_another_effect(bool loseCommitAcknowledgement)
    {
        await fixture.ResetAsync();
        var scope = Scope();
        var acknowledgement = new CommitAcknowledgementFault();
        await using var context = BoundContext(scope, acknowledgement);
        var subject = SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), scope);
        var decision = await SeedDecisionAsync(context, subject, scope);
        if (loseCommitAcknowledgement) acknowledgement.Arm();
        var authority = new QualifiedAuthority(subject, scope);
        var store = new ModerationStore(context, authority, TimeProvider.System);
        var service = new ModerationOperationService(store, authority, new ChangePointer(), TimeProvider.System);
        var operation = ModerationOperation.Transition(ModerationOperationKind.RecordResponse, subject, scope,
            Guid.CreateVersion7(), Guid.CreateVersion7(), decision.Id, 1, 1, Guid.Empty);
        var first = await service.ExecuteAsync(operation);
        var replay = await service.ExecuteAsync(operation);
        await Assert.That(first.Outcome).IsEqualTo(ModerationOperationOutcome.Known);
        await Assert.That(replay).IsEqualTo(first);
        await Assert.That(acknowledgement.Injected).IsEqualTo(loseCommitAcknowledgement);
        await store.ExecuteAsync(ModerationOperation.Evaluate(subject, scope, ModerationCapability.Publish),
            async (session, token) =>
            {
                var current = (await session.FindDecisionAsync(decision.Id, subject, scope, token))!;
                await Assert.That(current.Revision).IsEqualTo(2L);
                await Assert.That(await context.ModerationEffectIntents.CountAsync(token)).IsEqualTo(1);
                await Assert.That(await context.ModerationOperationReceipts.CountAsync(token)).IsEqualTo(1);
                return ModerationOperationResult.Unavailable();
            }, default);
    }

    [Test]
    public async Task Reusing_an_operation_identity_with_a_different_request_conflicts()
    {
        await fixture.ResetAsync();
        var scope = Scope();
        await using var context = BoundContext(scope);
        var subject = SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), scope);
        var decision = await SeedDecisionAsync(context, subject, scope);
        var authority = new QualifiedAuthority(subject, scope);
        var service = new ModerationOperationService(
            new ModerationStore(context, authority, TimeProvider.System), authority, new ChangePointer(), TimeProvider.System);
        var operationId = Guid.CreateVersion7();
        var first = ModerationOperation.Transition(ModerationOperationKind.RecordResponse, subject, scope,
            operationId, Guid.CreateVersion7(), decision.Id, 1, 1, Guid.Empty);
        await Assert.That((await service.ExecuteAsync(first)).Outcome).IsEqualTo(ModerationOperationOutcome.Known);
        var different = ModerationOperation.Transition(ModerationOperationKind.Withdraw, subject, scope,
            operationId, first.EffectId, decision.Id, 2, 1, Guid.Empty);
        await Assert.That((await service.ExecuteAsync(different)).Outcome).IsEqualTo(ModerationOperationOutcome.Conflict);
    }

    [Test]
    public async Task Non_known_result_rolls_back_staged_state_and_effect_without_a_receipt()
    {
        await fixture.ResetAsync();
        var scope = Scope();
        await using var context = BoundContext(scope);
        var subject = SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), scope);
        var decision = await SeedDecisionAsync(context, subject, scope);
        var authority = new QualifiedAuthority(subject, scope);
        var store = new ModerationStore(context, authority, TimeProvider.System);
        var operation = ModerationOperation.Transition(ModerationOperationKind.RecordResponse, subject, scope,
            Guid.CreateVersion7(), Guid.CreateVersion7(), decision.Id, 1, 1, Guid.Empty);
        var result = await store.ExecuteAsync(operation, async (session, token) =>
        {
            var current = (await session.FindDecisionAsync(decision.Id, subject, scope, token))!;
            var grant = (await authority.ResolveAsync(session, operation, token)).Grant!;
            current.RecordResponse(1, TimeProvider.System.GetUtcNow());
            await session.SaveAsync(current, null, null, grant, new ChangePointer().Plan(operation, current, null, null), token);
            return ModerationOperationResult.Unavailable();
        }, default);
        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Unavailable);
        await Assert.That(context.ChangeTracker.HasChanges()).IsFalse();
        await store.ExecuteAsync(ModerationOperation.Evaluate(subject, scope, ModerationCapability.Publish),
            async (session, token) =>
            {
                var current = (await session.FindDecisionAsync(decision.Id, subject, scope, token))!;
                await Assert.That(current.State).IsEqualTo(ModerationDecisionState.Proposed);
                await Assert.That(current.Revision).IsEqualTo(1L);
                await Assert.That(await context.ModerationEffectIntents.CountAsync(token)).IsEqualTo(0);
                await Assert.That(await context.ModerationOperationReceipts.CountAsync(token)).IsEqualTo(0);
                return ModerationOperationResult.Unavailable();
            }, default);
    }

    [Test]
    public async Task Bound_native_tenant_with_current_permission_can_read_an_empty_overlay()
    {
        await fixture.ResetAsync();
        var scope = Scope();
        await using var context = BoundContext(scope);
        var subject = SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), scope);
        var authority = new QualifiedAuthority(subject, scope);
        var service = new ModerationOperationService(
            new ModerationStore(context, authority, TimeProvider.System), authority, new ChangePointer(), TimeProvider.System);

        var result = await service.ExecuteAsync(ModerationOperation.Evaluate(subject, scope, ModerationCapability.Publish));

        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Known);
        await Assert.That(context.Database.CurrentTransaction is null).IsTrue();
    }

    [Test]
    public async Task Unbound_context_is_unavailable_not_an_unrestricted_global_query()
    {
        var scope = Scope();
        await using var context = fixture.CreateTenantFilteredDbContext();
        var subject = SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), scope);
        var authority = new QualifiedAuthority(subject, scope);
        var service = new ModerationOperationService(
            new ModerationStore(context, authority, TimeProvider.System), authority, new ChangePointer(), TimeProvider.System);

        var result = await service.ExecuteAsync(ModerationOperation.Evaluate(subject, scope, ModerationCapability.Publish));

        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Unavailable);
    }

    [Test]
    public async Task Positively_qualified_instance_scope_uses_no_synthetic_tenant()
    {
        await fixture.ResetAsync();
        var tenantScope = Scope();
        var scope = tenantScope.Parent!;
        await using var context = fixture.CreateTenantFilteredDbContext(null, PostgresTenantSessionInterceptor.Instance);
        var subject = SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), scope);
        var authority = new QualifiedAuthority(subject, scope);
        var service = new ModerationOperationService(
            new ModerationStore(context, authority, TimeProvider.System), authority, new ChangePointer(), TimeProvider.System);
        var result = await service.ExecuteAsync(
            ModerationOperation.Evaluate(subject, scope, ModerationCapability.Publish));
        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Known);
        await Assert.That(context.TenantFilterTenantId).IsNull();
    }

    [Test]
    public async Task Mutable_EF_tenant_cannot_override_the_open_provider_session_identity()
    {
        var first = Scope();
        var second = AuthorityScope.Tenant(first.Parent!, Guid.CreateVersion7());
        await using var context = BoundContext(first);
        await context.Database.OpenConnectionAsync();
        await context.Database.ExecuteSqlRawAsync(
            "SELECT set_config('app.current_tenant_id', {0}, false)", first.Id.ToString("D"));
        var switched = Substitute.For<ITenantContext>();
        switched.TenantId.Returns(second.Id);
        context.TenantContext = switched;
        var subject = SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), second);
        var authority = new QualifiedAuthority(subject, second);
        var service = new ModerationOperationService(
            new ModerationStore(context, authority, TimeProvider.System), authority, new ChangePointer(), TimeProvider.System);

        var result = await service.ExecuteAsync(ModerationOperation.Evaluate(subject, second, ModerationCapability.Publish));

        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Unavailable);
    }

    [Test]
    public async Task Foreign_requested_tenant_cannot_use_a_valid_local_authority_frame()
    {
        var first = Scope();
        var second = AuthorityScope.Tenant(first.Parent!, Guid.CreateVersion7());
        await using var context = BoundContext(first);
        var subject = SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), second);
        var authority = new QualifiedAuthority(subject, second);
        var service = new ModerationOperationService(
            new ModerationStore(context, authority, TimeProvider.System), authority, new ChangePointer(), TimeProvider.System);

        var result = await service.ExecuteAsync(ModerationOperation.Evaluate(subject, second, ModerationCapability.Publish));

        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Unavailable);
    }

    private ExploreDbContext BoundContext(AuthorityScope scope, params IInterceptor[] interceptors)
    {
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(scope.Id);
        return fixture.CreateTenantFilteredDbContext(tenant, [PostgresTenantSessionInterceptor.Instance, .. interceptors]);
    }

    private static async Task<ModerationDecision> SeedDecisionAsync(
        ExploreDbContext context, SubjectReference subject, AuthorityScope scope)
    {
        var now = new DateTimeOffset(2026, 10, 8, 0, 0, 0, TimeSpan.Zero);
        var allegation = ModerationCase.Open(Guid.CreateVersion7(), subject, scope, Guid.CreateVersion7(), now);
        var issuer = SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), scope);
        var decision = ModerationDecision.Propose(Guid.CreateVersion7(), allegation.Id, subject, issuer, scope, scope,
            ModerationRuleReference.Create(Guid.CreateVersion7(), "qualified-test", 1),
            [ModerationCapability.Publish], now, now.AddDays(1), null, 1, now);
        context.AddRange(allegation, decision);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        return decision;
    }

    private static AuthorityScope Scope() => AuthorityScope.Tenant(
        AuthorityScope.Instance(
            AuthorityScope.Solution(AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()),
            Guid.CreateVersion7()), Guid.CreateVersion7());

    private sealed class QualifiedAuthority(SubjectReference subject, AuthorityScope scope) : IModerationAuthorityParticipant
    {
        public Task<ModerationAuthorityResolution> AcquireAsync(
            IModerationSession session, ModerationOperation operation, CancellationToken cancellationToken) =>
            ResolveAsync(session, operation, cancellationToken);

        public Task<ModerationAuthorityResolution> ResolveAsync(
            IModerationSession session, ModerationOperation operation, CancellationToken cancellationToken) =>
            Task.FromResult(ModerationAuthorityResolution.Known(new ModerationAuthorityGrant(
                subject, subject, scope, operation.Kind, 1, 1, ModerationAuthorityState.Known)));

        public Task<ModerationAuthorityState> QualifyActivationAsync(
            IModerationSession session, ModerationDecision decision, ModerationAuthorityGrant grant,
            CancellationToken cancellationToken) => Task.FromResult(ModerationAuthorityState.Known);
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

    private sealed class CommitAcknowledgementFault : DbTransactionInterceptor
    {
        private int _armed;
        public bool Injected { get; private set; }
        public void Arm() => Interlocked.Exchange(ref _armed, 1);

        public override Task TransactionCommittedAsync(
            DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _armed, 0) == 1)
            {
                Injected = true;
                throw new NpgsqlException("Injected lost moderation commit acknowledgement.", new TimeoutException());
            }
            return Task.CompletedTask;
        }
    }
}
