using System.Data.Common;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Contracts.Services;
using Explore.Application.Models.InternalEvents;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using ISLAMU.Moderation.Application;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using NSubstitute;
using System.Text.Json;

namespace Event.Persistence.IntegrationTests.Moderation;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class ModerationLedgerPersistenceTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task Matching_target_identifier_cannot_pass_subject_qualified_case_access()
    {
        await fixture.ResetAsync();
        var scope = Scope();
        await using var context = BoundContext(scope);
        var subject = SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), scope);
        var ownedDecision = await SeedDecisionAsync(context, subject, scope);
        var now = new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
        var evidence = ModerationEvidenceReference.Create(
            ModerationCaseTarget.Create(ModerationCaseTargetKind.ParticipantIncident,
                subject.Id, scope, Guid.CreateVersion7()), scope,
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        var allegation = ModerationCase.OpenAllegation(Guid.CreateVersion7(), scope, evidence, now);
        context.Add(allegation);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        await using var privacyFixture = await ModerationNoticePrivacyFixture.CreateAsync();
        var privacyAuthority = privacyFixture.Authority(context, TimeProvider.System);
        var authority = new QualifiedAuthority(subject, scope);
        var store = new ModerationStore(context, authority, TimeProvider.System, privacyAuthority, Capture(context, privacyAuthority, authority));
        await store.ExecuteAsync(
            ModerationOperation.Evaluate(subject, scope, ModerationCapability.Publish),
            async (session, token) =>
            {
                await Assert.That(await session.FindCaseAsync(ownedDecision.CaseId, subject, scope, token)).IsNotNull();
                await Assert.That(await session.FindCaseAsync(allegation.Id, subject, scope, token)).IsNull();
                await Assert.That(await context.ModerationFindings.AnyAsync(token)).IsFalse();
                await Assert.That(await context.ModerationSanctions.AnyAsync(token)).IsFalse();
                return ModerationOperationResult.Unavailable();
            }, default);
    }

    [Test]
    public async Task Event_leaf_identity_is_required_for_case_and_decision_lookup()
    {
        await fixture.ResetAsync();
        var tenant = Scope();
        var eventScope = AuthorityScope.Event(tenant, Guid.CreateVersion7());
        var otherEvent = AuthorityScope.Event(tenant, Guid.CreateVersion7());
        await using var context = BoundContext(tenant);
        var subject = SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), tenant);
        var decision = await SeedDecisionAsync(context, subject, eventScope, tenant);
        await using var privacyFixture = await ModerationNoticePrivacyFixture.CreateAsync();
        var privacyAuthority = privacyFixture.Authority(context, TimeProvider.System);
        var authority = new QualifiedAuthority(subject, tenant);
        var store = new ModerationStore(context, authority, TimeProvider.System, privacyAuthority,
            Capture(context, privacyAuthority, authority));
        var inspected = false;

        await store.ExecuteAsync(ModerationOperation.Evaluate(subject, eventScope, ModerationCapability.Publish),
            async (session, token) =>
            {
                inspected = true;
                await Assert.That(await session.FindCaseAsync(decision.CaseId, subject, eventScope, token)).IsNotNull();
                await Assert.That(await session.FindDecisionAsync(decision.Id, subject, eventScope, token)).IsNotNull();
                await Assert.That(await session.FindCaseAsync(decision.CaseId, subject, otherEvent, token)).IsNull();
                await Assert.That(await session.FindDecisionAsync(decision.Id, subject, otherEvent, token)).IsNull();
                return ModerationOperationResult.Unavailable();
            }, default);
        await Assert.That(inspected).IsTrue();
    }

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
        await using var privacyFixture = await ModerationNoticePrivacyFixture.CreateAsync();
        var privacyAuthority = privacyFixture.Authority(context, TimeProvider.System);
        var authority = new QualifiedAuthority(subject, scope);
        var store = new ModerationStore(context, authority, TimeProvider.System, privacyAuthority, Capture(context, privacyAuthority, authority));
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
                var source = await context.ModerationNoticeSources.AsNoTracking().SingleAsync(token);
                var original = JsonSerializer.Deserialize<ModerationNoticeCaptureSnapshot>(source.SnapshotJson)!;
                await Assert.That(original.HasNoSubstantiveWork).IsTrue();
                await Assert.That(original.Decision.State).IsEqualTo(ModerationDecisionState.AwaitingFinding);
                await Assert.That(original.Decision.Revision).IsEqualTo(2);
                await Assert.That(await context.ModerationNotices.AnyAsync(token)).IsFalse();
                var handoff = await context.OutboxMessages.SingleAsync(
                    row => row.EventType == ModerationEffectOutboxMessageFactory.EventType, token);
                await Assert.That(handoff.Id).IsEqualTo(operation.EffectId);
                await Assert.That(handoff.AggregateId).IsEqualTo(operation.EffectId);
                await Assert.That(handoff.AggregateType).IsEqualTo(ModerationEffectOutboxMessageFactory.AggregateType);
                await Assert.That(handoff.Payload).IsNull();
                await Assert.That(handoff.Status).IsEqualTo(OutboxMessageStatus.Pending);
                await Assert.That((await context.ModerationEffectIntents.SingleAsync(token)).CompletedAt).IsNull();
                return ModerationOperationResult.Unavailable();
            }, default);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Inert_instance_capture_retains_actual_writer_tenancy_without_notice_projection(bool loseCommitAcknowledgement)
    {
        await fixture.ResetAsync();
        var writerTenant = Scope();
        var scope = writerTenant.Parent!;
        var acknowledgement = new CommitAcknowledgementFault();
        await using var context = BoundContext(writerTenant, acknowledgement);
        var subject = SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), scope);
        var decision = await SeedDecisionAsync(context, subject, scope, writerTenant);
        if (loseCommitAcknowledgement) acknowledgement.Arm();
        await using var privacyFixture = await ModerationNoticePrivacyFixture.CreateAsync();
        var privacyAuthority = privacyFixture.Authority(context, TimeProvider.System);
        var authority = new QualifiedAuthority(subject, scope);
        var store = new ModerationStore(context, authority, TimeProvider.System, privacyAuthority, Capture(context, privacyAuthority, authority));
        var service = new ModerationOperationService(store, authority, new ChangePointer(), TimeProvider.System);
        var operation = ModerationOperation.Transition(ModerationOperationKind.RecordResponse, subject, scope,
            Guid.CreateVersion7(), Guid.CreateVersion7(), decision.Id, 1, 1, Guid.Empty);
        var first = await service.ExecuteAsync(operation);
        var replay = await service.ExecuteAsync(operation);
        await Assert.That(first.Outcome).IsEqualTo(ModerationOperationOutcome.Known);
        await Assert.That(replay).IsEqualTo(first);
        await Assert.That(acknowledgement.Injected).IsEqualTo(loseCommitAcknowledgement);
        await store.ExecuteAsync(ModerationOperation.Evaluate(subject, scope, ModerationCapability.Publish),
            async (_, token) =>
            {
                var source = await context.ModerationNoticeSources.AsNoTracking().SingleAsync(token);
                var original = JsonSerializer.Deserialize<ModerationNoticeCaptureSnapshot>(source.SnapshotJson)!;
                await Assert.That(source.TenantId).IsEqualTo(writerTenant.Id);
                await Assert.That(original.WriterTenantId).IsEqualTo(writerTenant.Id);
                await Assert.That(original.Decision.Scope.Kind).IsEqualTo(ModerationScopeKind.Instance);
                await Assert.That(original.Decision.Scope.Id).IsEqualTo(scope.Id);
                await Assert.That(original.HasNoSubstantiveWork).IsTrue();
                await Assert.That(source.RetainUntil).IsNull();
                await Assert.That(await context.ModerationNotices.AnyAsync(token)).IsFalse();
                await Assert.That(await context.NotificationIntents.AnyAsync(token)).IsFalse();
                await Assert.That(await context.ModerationOperationReceipts.CountAsync(token)).IsEqualTo(1);
                await Assert.That(await context.ModerationEffectIntents.CountAsync(token)).IsEqualTo(1);
                return ModerationOperationResult.Unavailable();
            }, default);
    }

    [Test]
    [Arguments("payload")]
    [Arguments("hash")]
    [Arguments("writer-tenant")]
    public async Task Changed_original_capture_cannot_verify_a_committed_operation(string change)
    {
        await fixture.ResetAsync();
        var scope = Scope();
        await using var context = BoundContext(scope);
        var subject = SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), scope);
        var decision = await SeedDecisionAsync(context, subject, scope);
        await using var privacyFixture = await ModerationNoticePrivacyFixture.CreateAsync();
        var privacyAuthority = privacyFixture.Authority(context, TimeProvider.System);
        var authority = new QualifiedAuthority(subject, scope);
        var store = new ModerationStore(context, authority, TimeProvider.System, privacyAuthority, Capture(context, privacyAuthority, authority));
        var service = new ModerationOperationService(store, authority, new ChangePointer(), TimeProvider.System);
        var operation = ModerationOperation.Transition(ModerationOperationKind.RecordResponse, subject, scope,
            Guid.CreateVersion7(), Guid.CreateVersion7(), decision.Id, 1, 1, Guid.Empty);
        await Assert.That((await service.ExecuteAsync(operation)).Outcome).IsEqualTo(ModerationOperationOutcome.Known);
        await store.ExecuteAsync(ModerationOperation.Evaluate(subject, scope, ModerationCapability.Publish),
            async (session, token) =>
            {
                var source = await context.ModerationNoticeSources.AsNoTracking()
                    .SingleAsync(row => row.EffectId == operation.EffectId, token);
                if (change == "payload")
                    await context.ModerationNoticeSources.Where(row => row.EffectId == operation.EffectId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.SnapshotJson,
                            source.SnapshotJson + " "), token);
                else if (change == "writer-tenant")
                    await context.ModerationNoticeSources.Where(row => row.EffectId == operation.EffectId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.TenantId, (Guid?)null), token);
                else
                {
                    var changedHash = (source.SnapshotHash[0] == '0' ? "1" : "0") + source.SnapshotHash[1..];
                    await context.ModerationNoticeSources.Where(row => row.EffectId == operation.EffectId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.SnapshotHash, changedHash), token);
                }
                return ModerationOperationResult.Applied(decision.Id, null, 2);
            }, default);

        await Assert.That((await service.ExecuteAsync(operation)).Outcome).IsEqualTo(ModerationOperationOutcome.Unavailable);
        await store.ExecuteAsync(ModerationOperation.Evaluate(subject, scope, ModerationCapability.Publish),
            async (session, token) =>
            {
                var retained = (await session.FindDecisionAsync(decision.Id, subject, scope, token))!;
                await Assert.That(retained.Revision).IsEqualTo(2);
                await Assert.That(await context.ModerationOperationReceipts.CountAsync(token)).IsEqualTo(1);
                await Assert.That(await context.ModerationEffectIntents.CountAsync(token)).IsEqualTo(1);
                await Assert.That((await context.ModerationEffectIntents.SingleAsync(token)).CompletedAt).IsNull();
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
        await using var privacyFixture = await ModerationNoticePrivacyFixture.CreateAsync();
        var privacyAuthority = privacyFixture.Authority(context, TimeProvider.System);
        var authority = new QualifiedAuthority(subject, scope);
        var service = new ModerationOperationService(
            new ModerationStore(context, authority, TimeProvider.System, privacyAuthority, Capture(context, privacyAuthority, authority)), authority, new ChangePointer(), TimeProvider.System);
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
        await using var privacyFixture = await ModerationNoticePrivacyFixture.CreateAsync();
        var privacyAuthority = privacyFixture.Authority(context, TimeProvider.System);
        var authority = new QualifiedAuthority(subject, scope);
        var store = new ModerationStore(context, authority, TimeProvider.System, privacyAuthority, Capture(context, privacyAuthority, authority));
        var operation = ModerationOperation.Transition(ModerationOperationKind.RecordResponse, subject, scope,
            Guid.CreateVersion7(), Guid.CreateVersion7(), decision.Id, 1, 1, Guid.Empty);
        var result = await store.ExecuteAsync(operation, async (session, token) =>
        {
            var current = (await session.FindDecisionAsync(decision.Id, subject, scope, token))!;
            var grant = (await authority.ResolveAsync(session, operation, token)).Grant!;
            current.RecordResponse(1, TimeProvider.System.GetUtcNow());
            await session.SaveAsync(current, null, null, grant, new ChangePointer().Plan(operation, current, null, null), token);
            await context.SaveChangesAsync(token);
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
                await Assert.That(await context.ModerationNoticeSources.CountAsync(token)).IsEqualTo(0);
                await Assert.That(await context.OutboxMessages.CountAsync(
                    row => row.EventType == ModerationEffectOutboxMessageFactory.EventType, token)).IsEqualTo(0);
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
        await using var privacyFixture = await ModerationNoticePrivacyFixture.CreateAsync();
        var privacyAuthority = privacyFixture.Authority(context, TimeProvider.System);
        var authority = new QualifiedAuthority(subject, scope);
        var service = new ModerationOperationService(
            new ModerationStore(context, authority, TimeProvider.System, privacyAuthority, Capture(context, privacyAuthority, authority)), authority, new ChangePointer(), TimeProvider.System);

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
        await using var privacyFixture = await ModerationNoticePrivacyFixture.CreateAsync();
        var privacyAuthority = privacyFixture.Authority(context, TimeProvider.System);
        var authority = new QualifiedAuthority(subject, scope);
        var service = new ModerationOperationService(
            new ModerationStore(context, authority, TimeProvider.System, privacyAuthority, Capture(context, privacyAuthority, authority)), authority, new ChangePointer(), TimeProvider.System);

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
        await using var privacyFixture = await ModerationNoticePrivacyFixture.CreateAsync();
        var privacyAuthority = privacyFixture.Authority(context, TimeProvider.System);
        var authority = new QualifiedAuthority(subject, scope);
        var service = new ModerationOperationService(
            new ModerationStore(context, authority, TimeProvider.System, privacyAuthority, Capture(context, privacyAuthority, authority)), authority, new ChangePointer(), TimeProvider.System);
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
        await using var privacyFixture = await ModerationNoticePrivacyFixture.CreateAsync();
        var privacyAuthority = privacyFixture.Authority(context, TimeProvider.System);
        var authority = new QualifiedAuthority(subject, second);
        var service = new ModerationOperationService(
            new ModerationStore(context, authority, TimeProvider.System, privacyAuthority, Capture(context, privacyAuthority, authority)), authority, new ChangePointer(), TimeProvider.System);

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
        await using var privacyFixture = await ModerationNoticePrivacyFixture.CreateAsync();
        var privacyAuthority = privacyFixture.Authority(context, TimeProvider.System);
        var authority = new QualifiedAuthority(subject, second);
        var service = new ModerationOperationService(
            new ModerationStore(context, authority, TimeProvider.System, privacyAuthority, Capture(context, privacyAuthority, authority)), authority, new ChangePointer(), TimeProvider.System);

        var result = await service.ExecuteAsync(ModerationOperation.Evaluate(subject, second, ModerationCapability.Publish));

        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Unavailable);
    }

    private ExploreDbContext BoundContext(AuthorityScope scope, params IInterceptor[] interceptors)
    {
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(scope.Id);
        return fixture.CreateTenantFilteredDbContext(tenant, [PostgresTenantSessionInterceptor.Instance, .. interceptors]);
    }

    internal static ModerationNoticeCaptureRepository Capture(
        ExploreDbContext context, IPrivacyIdentityFenceAuthority privacy,
        IModerationAuthorityParticipant authority, TimeProvider? clock = null) =>
        new(context, privacy, null, Substitute.For<IModerationPolicyPublicationSelectionSource>(),
            new ModerationNoticeRecipientRepository(context),
            new ModerationNoticeNativeMaterializer(Substitute.For<INotificationOwnershipResolver>(),
                Substitute.For<INotificationPreferenceResolver>(), Substitute.For<IRecipientNotificationMaterializer>()),
            clock ?? TimeProvider.System, authority);

    private static async Task<ModerationDecision> SeedDecisionAsync(
        ExploreDbContext context, SubjectReference subject, AuthorityScope scope, AuthorityScope? writerTenant = null)
    {
        var now = new DateTimeOffset(2026, 10, 8, 0, 0, 0, TimeSpan.Zero);
        var allegation = ModerationCase.Open(Guid.CreateVersion7(), subject, scope, Guid.CreateVersion7(), now);
        var issuer = SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), scope);
        var decision = ModerationDecision.Propose(Guid.CreateVersion7(), allegation.Id, subject, issuer, scope, scope,
            ModerationRuleReference.Create(Guid.CreateVersion7(), "qualified-test", 1),
            [ModerationCapability.Publish], now, now.AddDays(1), null, 1, now);
        var tenantScope = writerTenant ?? scope;
        var tenant = new Tenant
        {
            Id = tenantScope.Id,
            FullName = "Capture test tenant",
            Slug = $"capture-{tenantScope.Id:N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        };
        var service = new ServicePrincipal
        {
            Id = Guid.CreateVersion7(),
            Code = $"capture-{scope.Id:N}",
            DisplayName = "Capture integration test"
        };
        var host = ModerationHostBinding.Establish(Guid.CreateVersion7(),
            tenantScope.TrustDomainId, tenantScope.Parent!.Parent!.Id, tenantScope.Parent.Id, service.Id);
        context.AddRange(tenant, service, host, ModerationQualificationEpoch.Initialize(Guid.CreateVersion7(), host, tenant),
            allegation, decision);
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
