using System.Security.Claims;
using System.Collections.Immutable;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.Moderation.Policy;
using Explore.Application.Services;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Identity;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using ISLAMU.Moderation.Application;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace Event.Persistence.IntegrationTests.Moderation;

/// <summary>Authored source regressions; no result here certifies native consumer conformance.</summary>
public sealed partial class ModerationNativeAuthorityParticipantTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Adopted_inactive_definition_allows_a_native_admin_proposal_without_enabling_effects()
    {
        await using var database = await NativeDatabase.CreateAsync();
        var proposal = ModerationOperation.Propose(database.Subject, database.Scope,
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 1, 1,
            new(database.Case.Id, database.Rule.Rule, [ModerationCapability.Publish],
                Now, Now.AddHours(1), null, 1));
        var result = await database.ExecuteAsync(database.IssuerId, proposal);
        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Known);
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That((await reader.ModerationPolicyPublications.SingleAsync()).PublishedActive).IsFalse();
        await Assert.That((await reader.ModerationPolicySelections.SingleAsync()).ActivePublicationId).IsNull();
        await Assert.That(await reader.ModerationSanctions.CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task Native_qualification_change_during_final_flush_rolls_back_the_complete_writer_capture()
    {
        await using var database = await NativeDatabase.CreateAsync();
        var proposal = ModerationOperation.Propose(database.Subject, database.Scope,
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 1, 1,
            new(database.Case.Id, database.Rule.Rule, [ModerationCapability.Publish],
                Now, Now.AddHours(1), null, 1));
        var invalidation = new AdvanceQualificationAfterFlush(proposal.OperationId, database.HostId, database.TenantId);
        await using (var context = database.Context(invalidation))
        {
            var clock = new FixedClock();
            var privacyAuthority = database.Privacy.Authority(context, clock);
            var participant = database.Participant(context, database.IssuerId, clock: clock);
            var service = new ModerationOperationService(
                new ModerationStore(context, participant, clock, privacyAuthority, ModerationLedgerPersistenceTests.Capture(context, privacyAuthority, participant, clock)),
                participant, new ModerationEffectPlanner(), clock);
            await Assert.That((await service.ExecuteAsync(proposal)).Outcome)
                .IsEqualTo(ModerationOperationOutcome.Unavailable);
            await Assert.That(invalidation.Injected).IsTrue();
        }
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That(await reader.ModerationDecisions.AnyAsync(row => row.Id == proposal.DecisionId)).IsFalse();
        await Assert.That(await reader.ModerationOperationReceipts.AnyAsync(row => row.Id == proposal.OperationId)).IsFalse();
        await Assert.That(await reader.ModerationEffectIntents.AnyAsync(row => row.Id == proposal.EffectId)).IsFalse();
        await Assert.That(await reader.ModerationNoticeSources.AnyAsync(row => row.EffectId == proposal.EffectId)).IsFalse();
        await Assert.That(await reader.OutboxMessages.AnyAsync(row => row.Id == proposal.EffectId)).IsFalse();
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync(
            row => row.HostBindingId == database.HostId && row.TenantId == database.TenantId)).Revision).IsEqualTo(1);
    }

    [Test]
    public async Task Closing_the_case_during_final_flush_cannot_commit_a_new_proposal()
    {
        await using var database = await NativeDatabase.CreateAsync();
        var proposal = ModerationOperation.Propose(database.Subject, database.Scope,
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 1, 1,
            new(database.Case.Id, database.Rule.Rule, [ModerationCapability.Publish],
                Now, Now.AddHours(1), null, 1));
        var closure = new CloseCaseAfterFlush(proposal.OperationId, database.Case.Id);
        await using (var context = database.Context(closure))
        {
            var clock = new FixedClock();
            var privacyAuthority = database.Privacy.Authority(context, clock);
            var participant = database.Participant(context, database.IssuerId, clock: clock);
            var service = new ModerationOperationService(
                new ModerationStore(context, participant, clock, privacyAuthority, ModerationLedgerPersistenceTests.Capture(context, privacyAuthority, participant, clock)),
                participant, new ModerationEffectPlanner(), clock);
            await Assert.That((await service.ExecuteAsync(proposal)).Outcome)
                .IsEqualTo(ModerationOperationOutcome.Unavailable);
            await Assert.That(closure.Injected).IsTrue();
        }
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That(await reader.ModerationDecisions.AnyAsync(row => row.Id == proposal.DecisionId)).IsFalse();
        await Assert.That(await reader.ModerationOperationReceipts.AnyAsync(row => row.Id == proposal.OperationId)).IsFalse();
        await Assert.That(await reader.ModerationEffectIntents.AnyAsync(row => row.Id == proposal.EffectId)).IsFalse();
        await Assert.That(await reader.ModerationNoticeSources.AnyAsync(row => row.EffectId == proposal.EffectId)).IsFalse();
        await Assert.That(await reader.OutboxMessages.AnyAsync(row => row.Id == proposal.EffectId)).IsFalse();
        var allegation = await reader.ModerationCases.SingleAsync(row => row.Id == database.Case.Id);
        await Assert.That(allegation.State).IsEqualTo(ModerationCaseState.Open);
        await Assert.That(allegation.Revision).IsEqualTo(1);
    }

    [Test]
    [Arguments(ModerationOperationKind.Approve, false, false)]
    [Arguments(ModerationOperationKind.Approve, false, true)]
    [Arguments(ModerationOperationKind.AcceptFinding, false, false)]
    [Arguments(ModerationOperationKind.AcceptFinding, false, true)]
    [Arguments(ModerationOperationKind.AcceptFinding, true, false)]
    [Arguments(ModerationOperationKind.AcceptFinding, true, true)]
    public async Task Native_permission_is_requalified_at_the_exact_reviewer_evidence_deadline(
        ModerationOperationKind kind, bool urgent, bool atDeadline)
    {
        await using var database = await NativeDatabase.CreateAsync();
        var finding = await database.PrepareFindingAsync(urgent, acceptFinding: kind == ModerationOperationKind.Approve);
        if (kind == ModerationOperationKind.AcceptFinding && !urgent)
        {
            await using var writer = database.Context();
            writer.ModerationTrustDomainId = database.Scope.TrustDomainId;
            var decision = await writer.ModerationDecisions.SingleAsync(row => row.Id == database.Decision.Id);
            decision.RecordResponse(1, Now);
            await writer.SaveChangesAsync();
        }
        var operation = database.Transition(kind,
            revision: kind == ModerationOperationKind.Approve ? 3 : urgent ? 1 : 2,
            reference: kind == ModerationOperationKind.AcceptFinding ? finding : Guid.Empty);
        await using (var context = database.Context())
        {
            var clock = new FixedClock();
            var privacyAuthority = database.Privacy.Authority(context, clock);
            var participant = database.Participant(context, database.ReviewerId, clock: clock);
            var store = new ModerationStore(context, participant, clock, privacyAuthority,
                ModerationLedgerPersistenceTests.Capture(context, privacyAuthority, participant, clock));
            var result = await store.ExecuteAsync(operation, async (session, token) =>
            {
                var initial = await participant.ResolveAsync(session, operation, token);
                await Assert.That(initial.Outcome).IsEqualTo(ModerationOperationOutcome.Known);
                clock.UtcNow = Now.AddMinutes(30).AddTicks(atDeadline ? 0 : -1);
                var current = await participant.ResolveAsync(session, operation, token);
                await Assert.That(current.Outcome).IsEqualTo(atDeadline
                    ? ModerationOperationOutcome.Unavailable : ModerationOperationOutcome.Known);
                if (!atDeadline)
                    await Assert.That(ReferenceEquals(current.Grant, initial.Grant)).IsTrue();
                return ModerationOperationResult.Unavailable();
            }, default);
            await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Unavailable);
        }
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That(await reader.ModerationOperationReceipts.AnyAsync()).IsFalse();
        await Assert.That(await reader.ModerationEffectIntents.AnyAsync()).IsFalse();
        await Assert.That(await reader.ModerationNoticeSources.AnyAsync()).IsFalse();
        await Assert.That(await reader.OutboxMessages.AnyAsync()).IsFalse();
        var retained = await reader.ModerationDecisions.SingleAsync(row => row.Id == operation.DecisionId);
        await Assert.That(retained.Revision).IsEqualTo(operation.ExpectedRevision);
    }

    [Test]
    public async Task A_native_owner_review_profile_cannot_be_proposed_as_a_blanket_publishing_denial()
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationReferenceProfileId.CFG05);
        var proposal = ModerationOperation.Propose(database.Subject, database.Scope,
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 1, 1,
            new(database.Case.Id, database.Rule.Rule, [ModerationCapability.Publish],
                Now, Now.AddHours(1), null, 1));
        var result = await database.ExecuteAsync(database.IssuerId, proposal);
        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Denied);
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That(await reader.ModerationDecisions.CountAsync()).IsEqualTo(1);
        await Assert.That(await reader.ModerationOperationReceipts.CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task Claimed_admin_cannot_reject_without_a_current_native_role()
    {
        await using var database = await NativeDatabase.CreateAsync();
        var result = await database.ExecuteAsync(database.ReviewerId, database.Transition(ModerationOperationKind.Reject));
        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Denied);
    }

    [Test]
    public async Task Multiple_actual_role_rows_do_not_turn_a_lookup_fence_into_a_composite_key()
    {
        await using var database = await NativeDatabase.CreateAsync();
        await using (var writer = database.Context())
        {
            var role = new Role
            {
                Id = checked(await writer.Roles.MaxAsync(row => row.Id) + 1),
                MasterCode = "platform.native-authority-witness", FullName = "Additional native role",
                RoleScopeId = (int)RoleScopeEnum.Platform
            };
            writer.PlatformUserRoles.Add(new PlatformUserRole
            {
                Id = Guid.CreateVersion7(), UserId = database.IssuerId, User = null!,
                RoleId = role.Id, Role = role
            });
            await writer.SaveChangesAsync();
        }
        var result = await database.ExecuteAsync(database.IssuerId, database.Transition(ModerationOperationKind.Reject));
        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Known);
    }

    [Test]
    public async Task Reviewer_evidence_lifetime_comes_from_current_procedural_policy_not_profile_timing()
    {
        await using var database = await NativeDatabase.CreateAsync();
        await database.PrepareFindingAsync();
        database.NativeSources.CurrentMaximumReviewerLifetime = TimeSpan.FromMinutes(15);
        database.NativeSources.ApprovedMaximumReviewerLifetime = TimeSpan.FromMinutes(15);
        var result = await database.ExecuteAsync(database.ReviewerId,
            database.Transition(ModerationOperationKind.Approve, revision: 3));
        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Denied);
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That(await reader.ModerationOperationReceipts.CountAsync()).IsEqualTo(0);
        await Assert.That((await reader.ModerationDecisions.SingleAsync()).State)
            .IsEqualTo(ModerationDecisionState.AwaitingApproval);
    }

    [Test]
    public async Task Urgency_evidence_has_its_own_independently_approved_lifetime_ceiling()
    {
        await using var database = await NativeDatabase.CreateAsync();
        var finding = await database.PrepareFindingAsync(urgent: true, acceptFinding: false);
        database.NativeSources.CurrentMaximumUrgencyLifetime = TimeSpan.FromMinutes(15);
        database.NativeSources.ApprovedMaximumUrgencyLifetime = TimeSpan.FromMinutes(15);
        var result = await database.ExecuteAsync(database.ReviewerId,
            database.Transition(ModerationOperationKind.AcceptFinding, reference: finding));
        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Denied);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task Only_the_actual_subject_can_record_the_native_response(bool callerIsSubject)
    {
        await using var database = await NativeDatabase.CreateAsync();
        await database.AddResponseAsync();
        var result = await database.ExecuteAsync(callerIsSubject ? database.Subject.Id : database.IssuerId,
            database.Transition(ModerationOperationKind.RecordResponse));
        await Assert.That(result.Outcome).IsEqualTo(callerIsSubject
            ? ModerationOperationOutcome.Known : ModerationOperationOutcome.Denied);
    }

    [Test]
    public async Task A_response_transition_without_actual_case_evidence_is_denied()
    {
        await using var database = await NativeDatabase.CreateAsync();
        var result = await database.ExecuteAsync(database.Subject.Id, database.Transition(ModerationOperationKind.RecordResponse));
        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Denied);
    }

    [Test]
    public async Task Issuer_withdrawal_does_not_require_retaining_the_administrative_role()
    {
        await using var database = await NativeDatabase.CreateAsync();
        await using (var writer = database.Context())
        {
            writer.PlatformUserRoles.Remove(await writer.PlatformUserRoles.SingleAsync(row => row.UserId == database.IssuerId));
            await writer.SaveChangesAsync();
        }
        var result = await database.ExecuteAsync(database.IssuerId, database.Transition(ModerationOperationKind.Withdraw));
        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Known);
    }

    [Test]
    public async Task Another_current_administrator_cannot_withdraw_the_issuers_proposal()
    {
        await using var database = await NativeDatabase.CreateAsync();
        var result = await database.ExecuteAsync(database.AssessorId, database.Transition(ModerationOperationKind.Withdraw));
        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Denied);
    }

    [Test]
    public async Task An_identical_receipt_is_not_replayed_after_native_authority_is_removed()
    {
        await using var database = await NativeDatabase.CreateAsync();
        var operation = database.Transition(ModerationOperationKind.Reject);
        await Assert.That((await database.ExecuteAsync(database.IssuerId, operation)).Outcome)
            .IsEqualTo(ModerationOperationOutcome.Known);
        await using (var writer = database.Context())
        {
            writer.PlatformUserRoles.Remove(await writer.PlatformUserRoles.SingleAsync(row => row.UserId == database.IssuerId));
            await writer.SaveChangesAsync();
        }
        await Assert.That((await database.ExecuteAsync(database.IssuerId, operation)).Outcome)
            .IsEqualTo(ModerationOperationOutcome.Denied);
    }

    [Test]
    public async Task Installed_host_origin_cannot_be_replaced_with_the_request_tenant()
    {
        await using var database = await NativeDatabase.CreateAsync();
        var forged = SubjectReference.Create(ModerationSubjectKind.Human, database.Subject.Id, database.Scope);
        var result = await database.ExecuteAsync(database.Subject.Id,
            ModerationOperation.Evaluate(forged, database.Scope, ModerationCapability.ProtectedStatus));
        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Denied);
    }

    [Test]
    public async Task Another_instance_in_the_same_trust_domain_is_not_native_authority()
    {
        await using var database = await NativeDatabase.CreateAsync();
        var other = AuthorityScope.Instance(database.Instance.Parent!, Guid.CreateVersion7());
        var operation = ModerationOperation.Transition(ModerationOperationKind.Reject, database.Subject, other,
            Guid.CreateVersion7(), Guid.CreateVersion7(), database.Decision.Id, 1, 1, Guid.Empty);
        await Assert.That((await database.ExecuteAsync(database.IssuerId, operation)).Outcome)
            .IsEqualTo(ModerationOperationOutcome.Denied);
    }

    [Test]
    public async Task Resolved_grant_is_bound_to_the_same_operation_object_and_transaction()
    {
        await using var database = await NativeDatabase.CreateAsync();
        await using var context = database.Context();
        var privacyAuthority = database.Privacy.Authority(context, new FixedClock());
        var participant = database.Participant(context, database.Subject.Id);
        var store = new ModerationStore(context, participant, new FixedClock(), privacyAuthority,
            ModerationLedgerPersistenceTests.Capture(context, privacyAuthority, participant, new FixedClock()));
        var operation = ModerationOperation.Evaluate(database.Subject, database.Scope, ModerationCapability.Publish);
        IModerationSession? retained = null;
        await store.ExecuteAsync(operation, async (session, token) =>
        {
            retained = session;
            var first = await participant.ResolveAsync(session, operation, token);
            var second = await participant.ResolveAsync(session, operation, token);
            var substituted = await participant.ResolveAsync(session,
                ModerationOperation.Evaluate(database.Subject, database.Scope, ModerationCapability.Publish), token);
            await Assert.That(first.Outcome).IsEqualTo(ModerationOperationOutcome.Known);
            await Assert.That(ReferenceEquals(first.Grant, second.Grant)).IsTrue();
            await Assert.That(substituted.Outcome).IsEqualTo(ModerationOperationOutcome.Unavailable);
            return ModerationOperationResult.Unavailable();
        }, default);
        await Assert.That(retained is not null).IsTrue();
        await Assert.That((await participant.ResolveAsync(retained!, operation, default)).Outcome)
            .IsEqualTo(ModerationOperationOutcome.Unavailable);
    }

    [Test]
    public async Task Retiring_policy_support_does_not_retire_a_current_sanction()
    {
        await using var database = await NativeDatabase.CreateAsync();
        var sanction = await database.ApproveAsync();
        await using (var writer = database.Context())
        {
            writer.ModerationTrustDomainId = database.Scope.TrustDomainId;
            writer.ModerationSanctions.Add(sanction);
            await writer.SaveChangesAsync();
        }
        await database.PublishNextAsync(retire: true);
        var result = await database.ExecuteAsync(database.Subject.Id,
            ModerationOperation.Evaluate(database.Subject, database.Scope, ModerationCapability.Publish));
        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Denied);
    }

    [Test]
    public async Task A_current_assessor_revocation_invalidates_an_otherwise_qualified_approval()
    {
        await using var database = await NativeDatabase.CreateAsync();
        await database.PrepareFindingAsync();
        await using (var writer = database.Context())
        {
            writer.PlatformUserRoles.Remove(await writer.PlatformUserRoles.SingleAsync(row => row.UserId == database.AssessorId));
            await writer.SaveChangesAsync();
        }
        var operation = database.Transition(ModerationOperationKind.Approve, revision: 3);
        await Assert.That((await database.ExecuteAsync(database.ReviewerId, operation)).Outcome)
            .IsEqualTo(ModerationOperationOutcome.Denied);
    }

    [Test]
    [Arguments("owner")]
    [Arguments("code")]
    [Arguments("revision")]
    public async Task Retained_finding_reason_must_match_the_independently_selected_profile(string change)
    {
        await using var database = await NativeDatabase.CreateAsync();
        var findingId = await database.PrepareFindingAsync();
        await using (var writer = database.Context())
        {
            writer.ModerationTrustDomainId = database.Scope.TrustDomainId;
            var finding = writer.ModerationFindings.Where(row => row.Id == findingId);
            if (change == "owner")
            {
                var ownerId = Guid.CreateVersion7();
                await finding.ExecuteUpdateAsync(update => update.SetProperty(row => row.ReasonOwnerId, ownerId));
            }
            else if (change == "code")
                await finding.ExecuteUpdateAsync(update => update.SetProperty(row => row.ReasonCode, database.Rule.Rule.Code));
            else
                await finding.ExecuteUpdateAsync(update => update.SetProperty(row => row.ReasonRevision, database.Rule.Reason.Revision + 1));
        }
        var operation = database.Transition(ModerationOperationKind.Approve, revision: 3);
        await Assert.That((await database.ExecuteAsync(database.ReviewerId, operation)).Outcome)
            .IsEqualTo(ModerationOperationOutcome.Denied);
        await using var observer = database.Context();
        observer.ModerationTrustDomainId = database.Scope.TrustDomainId;
        var decision = await observer.ModerationDecisions.AsNoTracking().SingleAsync(row => row.Id == database.Decision.Id);
        await Assert.That(decision.Revision).IsEqualTo(3);
        await Assert.That(decision.Approvals.Count).IsEqualTo(0);
        await Assert.That(await observer.ModerationOperationReceipts.CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task Independent_reviewer_authority_cannot_replace_missing_original_disclosure()
    {
        await using var database = await NativeDatabase.CreateAsync();
        await database.PrepareFindingAsync();
        var operation = database.Transition(ModerationOperationKind.Approve, revision: 3);
        var first = await database.ExecuteAsync(database.ReviewerId, operation);
        var replay = await database.ExecuteAsync(database.ReviewerId, operation);
        await Assert.That(first.Outcome).IsEqualTo(ModerationOperationOutcome.Unavailable);
        await Assert.That(replay).IsEqualTo(first);
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        var decision = await reader.ModerationDecisions.AsNoTracking().SingleAsync();
        await Assert.That(decision.State).IsEqualTo(ModerationDecisionState.AwaitingApproval);
        await Assert.That(decision.Revision).IsEqualTo(3);
        await Assert.That(decision.Approvals.Count).IsEqualTo(0);
        await Assert.That(await reader.ModerationOperationReceipts.AnyAsync()).IsFalse();
        await Assert.That(await reader.ModerationNoticeSources.AnyAsync()).IsFalse();
        await Assert.That(await reader.ModerationEffectIntents.AnyAsync()).IsFalse();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task Urgent_finding_requires_its_separate_current_assessor_without_fabricating_response(int changed)
    {
        await using var database = await NativeDatabase.CreateAsync();
        var findingId = await database.PrepareFindingAsync(urgent: true, acceptFinding: false);
        if (changed != 0)
        {
            await using var writer = database.Context();
            writer.ModerationTrustDomainId = database.Scope.TrustDomainId;
            if (changed == 1)
                writer.PlatformUserRoles.Remove(await writer.PlatformUserRoles.SingleAsync(
                    row => row.UserId == database.UrgencyAssessorId));
            else
                writer.ModerationQualificationMutationReceipts.Remove(
                    await writer.ModerationQualificationMutationReceipts.SingleAsync());
            await writer.SaveChangesAsync();
        }
        var result = await database.ExecuteAsync(database.ReviewerId,
            database.Transition(ModerationOperationKind.AcceptFinding, reference: findingId));
        await Assert.That(result.Outcome).IsEqualTo(changed == 0
            ? ModerationOperationOutcome.Unavailable : ModerationOperationOutcome.Denied);
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        var decision = await reader.ModerationDecisions.SingleAsync();
        await Assert.That(decision.State).IsEqualTo(ModerationDecisionState.Proposed);
        await Assert.That(decision.Revision).IsEqualTo(1L);
        await Assert.That(await reader.ModerationQualificationEvidence.CountAsync(
            row => row.Purpose == ModerationQualificationEvidencePurpose.Response)).IsEqualTo(0);
        await Assert.That(await reader.ModerationEffectIntents.CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.ModerationNoticeSources.AnyAsync()).IsFalse();
    }

    [Test]
    public async Task Native_administrator_cannot_impersonate_the_maintenance_service()
    {
        await using var database = await NativeDatabase.CreateAsync();
        var sanction = await database.ApproveAsync();
        await using (var writer = database.Context())
        {
            writer.ModerationTrustDomainId = database.Scope.TrustDomainId;
            writer.ModerationSanctions.Add(sanction);
            await writer.SaveChangesAsync();
        }
        var operation = ModerationOperation.Reconcile(database.Subject, database.Scope,
            Guid.CreateVersion7(), Guid.CreateVersion7(), database.Decision.Id,
            database.Decision.Revision, 1, sanction.Id, sanction.Revision);
        await Assert.That((await database.ExecuteAsync(database.IssuerId, operation)).Outcome)
            .IsEqualTo(ModerationOperationOutcome.Denied);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task Only_the_installed_current_service_can_reconcile_an_expired_contribution(bool installedMaintenance)
    {
        await using var database = await NativeDatabase.CreateAsync();
        var sanction = await database.ApproveAsync();
        await using (var writer = database.Context())
        {
            writer.ModerationTrustDomainId = database.Scope.TrustDomainId;
            writer.ModerationSanctions.Add(sanction);
            await writer.SaveChangesAsync();
        }
        var principal = await database.AddMachineAsync(installedMaintenance);
        var operation = ModerationOperation.Reconcile(database.Subject, database.Scope,
            Guid.CreateVersion7(), Guid.CreateVersion7(), database.Decision.Id,
            database.Decision.Revision, 1, sanction.Id, sanction.Revision);
        await using var context = database.Context();
        var clock = new FixedClock(Now.AddHours(2));
        var privacyAuthority = database.Privacy.Authority(context, clock);
        var participant = database.Participant(context, database.IssuerId, principal, clock);
        var result = await new ModerationOperationService(
            new ModerationStore(context, participant, clock, privacyAuthority, ModerationLedgerPersistenceTests.Capture(context, privacyAuthority, participant, clock)),
            participant, new ModerationEffectPlanner(), clock)
            .ExecuteAsync(operation);
        await Assert.That(result.Outcome).IsEqualTo(installedMaintenance
            ? ModerationOperationOutcome.Unavailable : ModerationOperationOutcome.Denied);
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That((await reader.ModerationSanctions.SingleAsync()).Revision).IsEqualTo(sanction.Revision);
        await Assert.That(await reader.ModerationOperationReceipts.AnyAsync()).IsFalse();
    }

    [Test]
    public async Task A_reversed_finding_cannot_receive_an_independent_approval()
    {
        await using var database = await NativeDatabase.CreateAsync();
        await database.PrepareFindingAsync();
        await using (var writer = database.Context())
        {
            writer.ModerationTrustDomainId = database.Scope.TrustDomainId;
            var finding = await writer.ModerationFindings.SingleAsync();
            finding.Reverse(Guid.CreateVersion7(),
                SubjectReference.Create(ModerationSubjectKind.Human, database.AssessorId, database.Instance),
                "The final finding is reversed.", 1, Now);
            await writer.SaveChangesAsync();
        }
        await Assert.That((await database.ExecuteAsync(database.ReviewerId,
            database.Transition(ModerationOperationKind.Approve, revision: 3))).Outcome)
            .IsEqualTo(ModerationOperationOutcome.Denied);
    }

    [Test]
    public async Task A_publication_flag_cannot_replace_active_selection_and_current_native_owners()
    {
        await using var database = await NativeDatabase.CreateAsync();
        await database.PrepareFindingAsync();
        await using (var writer = database.Context())
        {
            writer.ModerationTrustDomainId = database.Scope.TrustDomainId;
            var decision = await writer.ModerationDecisions.SingleAsync();
            decision.Approve(SubjectReference.Create(ModerationSubjectKind.Human, database.ReviewerId, database.Instance),
                database.Scope, 3, Now);
            await writer.SaveChangesAsync();
            // Deliberately corrupt a flag and its declaration digest, without manufacturing
            // an active pointer, independently current sources or measured conformance.
            var declared = database.PublicationRequest.Proposed with { RequestedState = ModerationPolicyRequestedState.Active };
            await writer.ModerationPolicyPublications.ExecuteUpdateAsync(update =>
                update.SetProperty(row => row.PublishedActive, true)
                    .SetProperty(row => row.DeclaredStateDigest, ModerationPolicyPublicationProjection.ProposedDigest(declared)));
        }
        var operation = ModerationOperation.Transition(ModerationOperationKind.Activate,
            database.Subject, database.Scope, Guid.CreateVersion7(), Guid.CreateVersion7(),
            database.Decision.Id, 4, 1, Guid.CreateVersion7());
        var result = await database.ExecuteAsync(database.IssuerId, operation);
        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Unavailable);
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That(await reader.ModerationSanctions.CountAsync()).IsEqualTo(0);
        await Assert.That((await reader.ModerationDecisions.SingleAsync()).State).IsEqualTo(ModerationDecisionState.Approved);
    }

    [Test]
    public async Task Two_qualified_accounts_controlled_by_one_human_cannot_complete_native_quorum()
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationReferenceProfileId.CFG11);
        await database.PrepareFindingAsync();
        await database.QualifyReviewerAsync(database.UrgencyAssessorId, database.UrgencyAssessorId);
        await using (var writer = database.Context())
        {
            writer.ModerationTrustDomainId = database.Scope.TrustDomainId;
            var decision = await writer.ModerationDecisions.SingleAsync();
            decision.Approve(
                SubjectReference.Create(ModerationSubjectKind.Human, database.ReviewerId, database.Instance),
                database.Scope, 3, Now);
            await writer.SaveChangesAsync();
        }
        var operation = database.Transition(ModerationOperationKind.Approve, revision: 4);
        await database.AssertNativeAuthorityAsync(database.UrgencyAssessorId, operation, ModerationOperationOutcome.Known);

        await database.QualifyReviewerAsync(database.UrgencyAssessorId, database.ReviewerId);
        await database.AssertNativeAuthorityAsync(database.UrgencyAssessorId, operation, ModerationOperationOutcome.Denied);
    }

    [Test]
    public async Task A_shared_verified_mailbox_cannot_supply_missing_controller_proof_or_independent_native_quorum()
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationReferenceProfileId.CFG11);
        await database.PrepareFindingAsync();
        await using (var writer = database.Context())
        {
            var users = await writer.Users.Include(user => user.Pii).Where(user =>
                user.Id == database.ReviewerId || user.Id == database.UrgencyAssessorId).ToListAsync();
            await Assert.That(users.Count).IsEqualTo(2);
            foreach (var user in users)
            {
                user.Email = "shared-review@example.invalid";
                user.EmailVerified = true;
            }
            writer.ModerationTrustDomainId = database.Scope.TrustDomainId;
            var decision = await writer.ModerationDecisions.SingleAsync();
            decision.Approve(
                SubjectReference.Create(ModerationSubjectKind.Human, database.ReviewerId, database.Instance),
                database.Scope, 3, Now);
            await writer.SaveChangesAsync();
        }
        var operation = database.Transition(ModerationOperationKind.Approve, revision: 4);
        await database.AssertNativeAuthorityAsync(database.UrgencyAssessorId, operation, ModerationOperationOutcome.Denied);

        await database.QualifyReviewerAsync(database.UrgencyAssessorId, database.UrgencyAssessorId);
        await database.AssertNativeAuthorityAsync(database.UrgencyAssessorId, operation, ModerationOperationOutcome.Known);

        await database.QualifyReviewerAsync(database.UrgencyAssessorId, database.ReviewerId);
        await database.AssertNativeAuthorityAsync(database.UrgencyAssessorId, operation, ModerationOperationOutcome.Denied);
    }

    [Test]
    public async Task An_issuer_controlled_adjudicator_alias_cannot_establish_a_native_final_finding()
    {
        await using var database = await NativeDatabase.CreateAsync();
        var finding = await database.PrepareFindingAsync(acceptFinding: false);
        var operation = database.Transition(ModerationOperationKind.AcceptFinding, reference: finding);
        await database.AssertNativeAuthorityAsync(database.ReviewerId, operation, ModerationOperationOutcome.Known);

        await database.QualifyReviewerAsync(database.ReviewerId, database.IssuerId);
        await database.AssertNativeAuthorityAsync(database.ReviewerId, operation, ModerationOperationOutcome.Denied);
    }

    [Test]
    public async Task A_current_conflicted_reviewer_cannot_establish_a_native_final_finding()
    {
        await using var database = await NativeDatabase.CreateAsync();
        var finding = await database.PrepareFindingAsync(acceptFinding: false);
        var operation = database.Transition(ModerationOperationKind.AcceptFinding, reference: finding);
        await database.AssertNativeAuthorityAsync(database.ReviewerId, operation, ModerationOperationOutcome.Known);

        await database.QualifyReviewerAsync(database.ReviewerId, database.ReviewerId,
            ModerationReviewerConflictState.Conflicted);
        await database.AssertNativeAuthorityAsync(database.ReviewerId, operation, ModerationOperationOutcome.Denied);
    }

    [Test]
    public async Task A_copied_occurrence_reference_cannot_substitute_for_the_owned_native_final_finding()
    {
        await using var database = await NativeDatabase.CreateAsync();
        var finding = await database.PrepareFindingAsync(acceptFinding: false);
        await database.AssertNativeAuthorityAsync(database.ReviewerId,
            database.Transition(ModerationOperationKind.AcceptFinding, reference: finding), ModerationOperationOutcome.Known);

        await database.AssertNativeAuthorityAsync(database.ReviewerId,
            database.Transition(ModerationOperationKind.AcceptFinding, reference: database.Case.OccurrenceId),
            ModerationOperationOutcome.Denied);
    }

    [Test]
    public async Task A_delegated_admin_key_cannot_contribute_its_owners_human_approval()
    {
        await using var database = await NativeDatabase.CreateAsync();
        await database.PrepareFindingAsync();
        await database.QualifyReviewerAsync(database.UrgencyAssessorId, database.UrgencyAssessorId);
        var operation = database.Transition(ModerationOperationKind.Approve, revision: 3);
        await database.AssertNativeAuthorityAsync(database.UrgencyAssessorId, operation, ModerationOperationOutcome.Known);

        var principal = await database.AddMachineAsync(maintenance: false, ownerId: database.UrgencyAssessorId);
        await database.AssertNativeAuthorityAsync(database.UrgencyAssessorId, operation, ModerationOperationOutcome.Denied,
            principal);
    }

    [Test]
    public async Task A_finding_with_forged_subject_origin_cannot_establish_native_guilt()
    {
        await using var database = await NativeDatabase.CreateAsync();
        var finding = await database.PrepareFindingAsync(acceptFinding: false);
        var operation = database.Transition(ModerationOperationKind.AcceptFinding, reference: finding);
        await database.AssertNativeAuthorityAsync(database.ReviewerId, operation, ModerationOperationOutcome.Known);

        await using (var writer = database.Context())
        {
            writer.ModerationTrustDomainId = database.Scope.TrustDomainId;
            await writer.ModerationFindings.Where(row => row.Id == finding).ExecuteUpdateAsync(update =>
                update.SetProperty(row => row.SubjectOriginKind, ModerationScopeKind.Tenant)
                    .SetProperty(row => row.SubjectOriginTenantId, database.TenantId));
        }
        await database.AssertNativeAuthorityAsync(database.ReviewerId, operation, ModerationOperationOutcome.Denied);
    }

    [Test]
    [Arguments(ModerationOperationKind.Reject, false)]
    [Arguments(ModerationOperationKind.Reject, true)]
    [Arguments(ModerationOperationKind.Withdraw, false)]
    [Arguments(ModerationOperationKind.Withdraw, true)]
    public async Task HistoricalTerminalAuthorityDoesNotRequireDepartedAdjudicatorOrSubject(
        ModerationOperationKind kind, bool erasedSubject)
    {
        await using var database = await NativeDatabase.CreateAsync();
        await database.PrepareFindingAsync();
        await using (var departure = database.Context())
        {
            await departure.Users.Where(row => row.Id == database.ReviewerId
                || erasedSubject && row.Id == database.Subject.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.IsDeleted, true));
        }
        var operation = database.Transition(kind, revision: database.Decision.Revision);
        await database.AssertNativeAuthorityAsync(database.IssuerId, operation, ModerationOperationOutcome.Known);
        await database.AssertNativeAuthorityAsync(database.ReviewerId, operation, ModerationOperationOutcome.Denied);
    }

    private sealed class NativeDatabase(SqliteConnection connection) : IAsyncDisposable, ITenantContext
    {
        public ModerationNoticePrivacyFixture Privacy { get; private set; } = null!;
        private readonly DbContextOptions<ExploreDbContext> _options =
            TestDbContextOptions.Create<ExploreDbContext>().UseSqlite(connection)
                .UseSnakeCaseNamingConvention().AddInterceptors(SqliteNamedLockTransactionInterceptor.Instance).Options;
        public Guid TenantId { get; } = Guid.CreateVersion7();
        public Guid IssuerId { get; } = Guid.CreateVersion7();
        public Guid AssessorId { get; } = Guid.CreateVersion7();
        public Guid UrgencyAssessorId { get; } = Guid.CreateVersion7();
        public Guid ReviewerId { get; } = Guid.CreateVersion7();
        public Guid HostId { get; } = Guid.CreateVersion7();
        public Guid MaintenanceId { get; } = Guid.CreateVersion7();
        public AuthorityScope Instance { get; } = AuthorityScope.Instance(
            AuthorityScope.Solution(AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()), Guid.CreateVersion7());
        public AuthorityScope Scope => AuthorityScope.Tenant(Instance, TenantId);
        public SubjectReference Subject { get; private set; } = null!;
        public ModerationCase Case { get; private set; } = null!;
        public ModerationDecision Decision { get; private set; } = null!;
        public ModerationPolicyProfileDefinition Rule { get; private set; } = null!;
        public ModerationPolicyPublicationRequest PublicationRequest { get; private set; } = null!;
        public ModerationCapabilityRegistry Inventory { get; } =
            ModerationCapabilityRegistry.Parse(ModerationCapabilityRegistry.ReadInventory());
        public ProceduralPolicyWithoutDeployment NativeSources { get; private set; } = null!;
        public ExploreDbContext Context(params IInterceptor[] interceptors) =>
            new(new DbContextOptionsBuilder<ExploreDbContext>(_options).AddInterceptors(interceptors).Options)
                { TenantContext = this };

        public static async Task<NativeDatabase> CreateAsync(
            ModerationReferenceProfileId referenceId = ModerationReferenceProfileId.CFG06)
        {
            var connection = await SqliteTestDatabaseFactory.CreateOpenIsolatedConnectionAsync();
            await LocalIdentitySqliteTemplate.CopySeededApplicationToAsync(connection, CancellationToken.None);
            var database = new NativeDatabase(connection);
            database.Privacy = await ModerationNoticePrivacyFixture.CreateAsync();
            database.Subject = SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), database.Instance);
            await using var context = database.Context();
            var status = await context.TenantStatuses.SingleAsync(row => row.IsActiveState);
            var tenant = new Tenant
            {
                Id = database.TenantId, FullName = "Native lifecycle authority", Slug = $"native-{database.TenantId:N}",
                TenantStatusId = status.Id, TenantStatus = status
            };
            context.Tenants.Add(tenant);
            var role = await context.Roles.SingleAsync(row =>
                row.RoleScopeId == (int)RoleScopeEnum.Platform && row.MasterCode == "platform.admin");
            foreach (var id in new[] { database.IssuerId, database.AssessorId, database.UrgencyAssessorId,
                database.ReviewerId, database.Subject.Id })
            {
                var user = new User
                {
                    Id = id, Pii = new UserPii
                    {
                        UserId = id, Email = $"{id:N}@example.invalid", FirstName = "Native", LastName = "Authority"
                    }
                };
                context.Users.Add(user);
                context.TenantUsers.Add(new TenantUser
                {
                    Id = Guid.CreateVersion7(), TenantId = tenant.Id, Tenant = tenant, UserId = id, User = user,
                    StatusId = (int)TenantUserStatusEnum.Active
                });
                if (id == database.IssuerId || id == database.AssessorId || id == database.UrgencyAssessorId)
                    context.PlatformUserRoles.Add(new PlatformUserRole
                    {
                        Id = Guid.CreateVersion7(), UserId = id, User = user, RoleId = role.Id, Role = role
                    });
            }
            var maintenance = new ServicePrincipal
            {
                Id = database.MaintenanceId, Code = $"native-maintenance-{database.TenantId:N}", DisplayName = "Native maintenance"
            };
            var host = ModerationHostBinding.Establish(database.HostId, database.Instance.TrustDomainId,
                database.Instance.Parent!.Id, database.Instance.Id, maintenance.Id);
            var epoch = ModerationQualificationEpoch.Initialize(Guid.CreateVersion7(), host, tenant);
            context.AddRange(maintenance, host, epoch);
            database.Case = ModerationCase.Open(Guid.CreateVersion7(), database.Subject, database.Scope, Guid.CreateVersion7(), Now);
            var reference = ModerationReferenceCatalogue.GetProfile(referenceId);
            var response = reference.Responses[0];
            ModerationRuleReference Rule(string code) => ModerationRuleReference.Create(database.Scope.Id, code, 1);
            database.Rule = new(Guid.CreateVersion7(), Rule("native-lifecycle"), Rule("native-lifecycle-reason"),
                reference.Id, reference.Revision, response.Grade, [ModerationScopeKind.Tenant],
                [ModerationSubjectKind.Human], reference.Effects.ToImmutableArray(), reference.Bundle,
                reference.ProtectionPurpose, [], new(response.Timing.Kind, TimeSpan.FromHours(1)),
                response.RequiredApprovals, 6, ModerationReferenceRecognition.Never);
            var proposed = new ModerationPolicyProposedState(Guid.CreateVersion7(), 0, Guid.CreateVersion7(), database.Scope,
                ModerationReferenceCatalogue.Revision, database.Inventory.Revision, database.Inventory.Fingerprint,
                ModerationPolicyRequestedState.Draft, [ModerationNativeMode.Embedded],
                [new(Rule("native-lifecycle-class"), "Native lifecycle authority witness.", reference.Ground, reference.Basis)],
                [new(database.Rule.Reason, "Native lifecycle finding.", Rule("native-lifecycle-class"))],
                [database.Rule], [database.Rule.Rule]);
            database.PublicationRequest = new(Guid.CreateVersion7(), host.Id, epoch.Id, 1,
                ModerationPolicyPublicationProjection.WriterGeneration, null, proposed);
            database.Decision = ModerationDecision.Propose(Guid.CreateVersion7(), database.Case.Id, database.Subject,
                SubjectReference.Create(ModerationSubjectKind.Human, database.IssuerId, database.Instance),
                database.Instance, database.Scope, database.Rule.Rule,
                [referenceId == ModerationReferenceProfileId.CFG11 ? ModerationCapability.Manage : ModerationCapability.Publish],
                Now, Now.AddHours(1), null, response.RequiredApprovals, Now);
            context.ModerationTrustDomainId = database.Instance.TrustDomainId;
            context.AddRange(database.Case, database.Decision);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            var (admin, machine) = database.Identity(context, database.IssuerId);
            var published = await new ModerationPolicyPublicationRepository(
                context, admin, machine, database.Inventory, null, new FixedClock()).PublishAsync(database.PublicationRequest);
            if (published.Outcome != ModerationPolicyPublicationOutcome.Known)
                throw new InvalidOperationException("Native lifecycle draft publication failed.");
            database.NativeSources = new(database);
            return database;
        }

        public async Task PublishNextAsync(bool retire = false)
        {
            await using var context = Context();
            var epoch = await context.ModerationQualificationEpochs.AsNoTracking().SingleAsync();
            var previous = PublicationRequest;
            var next = previous with
            {
                OperationId = Guid.CreateVersion7(), ExpectedQualificationRevision = epoch.Revision,
                ExpectedPublicationId = previous.Proposed.PublicationId,
                Proposed = previous.Proposed with
                {
                    PublicationId = Guid.CreateVersion7(), ExpectedRevision = previous.Proposed.ExpectedRevision + 1,
                    SupportedProfiles = retire ? [] : previous.Proposed.SupportedProfiles
                }
            };
            var (admin, machine) = Identity(context, IssuerId);
            var result = await new ModerationPolicyPublicationRepository(
                context, admin, machine, Inventory, null, new FixedClock()).PublishAsync(next);
            await Assert.That(result.Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Known);
            PublicationRequest = next;
        }

        public ModerationOperation Transition(ModerationOperationKind kind, long revision = 1, Guid reference = default) =>
            ModerationOperation.Transition(kind, Subject, Scope, Guid.CreateVersion7(), Guid.CreateVersion7(),
                Decision.Id, revision, 1, reference);

        public async Task<ModerationOperationResult> ExecuteAsync(Guid actor, ModerationOperation operation)
        {
            await using var context = Context();
            var privacyAuthority = Privacy.Authority(context, new FixedClock());
            var participant = Participant(context, actor);
            return await new ModerationOperationService(new ModerationStore(context, participant, new FixedClock(), privacyAuthority,
                    ModerationLedgerPersistenceTests.Capture(context, privacyAuthority, participant, new FixedClock())),
                participant, new ModerationEffectPlanner(), new FixedClock()).ExecuteAsync(operation);
        }

        public async Task AssertNativeAuthorityAsync(
            Guid actor, ModerationOperation operation, ModerationOperationOutcome expected,
            ApiKeyPrincipalContext? principal = null)
        {
            ModerationDecision before;
            await using (var reader = Context())
            {
                reader.ModerationTrustDomainId = Scope.TrustDomainId;
                before = await reader.ModerationDecisions.AsNoTracking().SingleAsync();
            }
            await using (var context = Context())
            {
                var clock = new FixedClock();
                var privacyAuthority = Privacy.Authority(context, clock);
                var participant = Participant(context, actor, principal, clock);
                var resolved = false;
                // The real store acquires authority inside its serializable transaction before
                // this callback. Deliberately abort after Resolve: no service mutation or notice
                // capture can mask the acquisition gate being tested.
                var result = await new ModerationStore(context, participant, clock, privacyAuthority,
                    ModerationLedgerPersistenceTests.Capture(context, privacyAuthority, participant, clock)).ExecuteAsync(operation, async (session, token) =>
                {
                    var authority = await participant.ResolveAsync(session, operation, token);
                    await Assert.That(authority.Outcome).IsEqualTo(ModerationOperationOutcome.Known);
                    await Assert.That(authority.Grant!.Principal.Kind).IsEqualTo(ModerationSubjectKind.Human);
                    await Assert.That(authority.Grant.Principal.Id).IsEqualTo(actor);
                    await Assert.That(authority.Grant.Scope.Contains(operation.Target)).IsTrue();
                    resolved = true;
                    return ModerationOperationResult.Unavailable();
                }, default);
                await Assert.That(resolved).IsEqualTo(expected == ModerationOperationOutcome.Known);
                await Assert.That(result.Outcome).IsEqualTo(expected == ModerationOperationOutcome.Known
                    ? ModerationOperationOutcome.Unavailable : expected);
            }
            await using var observer = Context();
            observer.ModerationTrustDomainId = Scope.TrustDomainId;
            var after = await observer.ModerationDecisions.AsNoTracking().SingleAsync();
            await Assert.That(after.Revision).IsEqualTo(before.Revision);
            await Assert.That(after.State).IsEqualTo(before.State);
            await Assert.That(after.FindingId).IsEqualTo(before.FindingId);
            await Assert.That(after.Approvals.Count).IsEqualTo(before.Approvals.Count);
            await Assert.That(after.Approval1Id).IsEqualTo(before.Approval1Id);
            await Assert.That(after.Approval2Id).IsEqualTo(before.Approval2Id);
            await Assert.That((await observer.ModerationCases.SingleAsync()).Revision).IsEqualTo(Case.Revision);
            await Assert.That(await observer.ModerationOperationReceipts.AnyAsync()).IsFalse();
            await Assert.That(await observer.ModerationSanctions.AnyAsync()).IsFalse();
            await Assert.That(await observer.ModerationEffectIntents.AnyAsync()).IsFalse();
            await Assert.That(await observer.ModerationNoticeSources.AnyAsync()).IsFalse();
        }

        public ModerationAuthorityParticipant Participant(
            ExploreDbContext context, Guid actor, ApiKeyPrincipalContext? principal = null, TimeProvider? clock = null)
        {
            clock ??= new FixedClock();
            var (admin, machine) = Identity(context, actor);
            if (principal is not null) machine.SetPrincipal(principal);
            var keys = new ExternalApiKeyRepository(context);
            var bindings = new ModerationMachineSubjectBindingRepository(
                context, admin, machine, keys, new UnconfiguredKeys(), clock);
            var activation = new ModerationNativeActivationQualification(context, Inventory, NativeSources, NativeSources, clock);
            var publications = new ModerationPolicyPublicationRepository(context, admin, machine, Inventory, null, clock);
            return new(context, admin, machine, bindings, keys, publications, Inventory, activation, clock);
        }

        private (AdminContext Admin, MachinePrincipalAccessor Machine) Identity(ExploreDbContext context, Guid actor)
        {
            var http = new HttpContextAccessor
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, actor.ToString()), new Claim(ClaimTypes.Role, "platform.admin")],
                        "native-lifecycle"))
                }
            };
            var admin = new AdminContext(http, new PlatformUserRoleRepository(context),
                new TenantUserRoleGrantRepository(context), new OrganizationMemberRepository(context),
                new GroupMemberRepository(context), new UserExternalLoginRepository(context), NullLogger<AdminContext>.Instance);
            return (admin, new MachinePrincipalAccessor(http));
        }

        public async Task<ApiKeyPrincipalContext> AddMachineAsync(bool maintenance, Guid? ownerId = null)
        {
            await using var context = Context();
            var owner = ownerId ?? IssuerId;
            var keyId = Guid.CreateVersion7().ToString("N");
            var serviceId = maintenance ? MaintenanceId : Guid.CreateVersion7();
            if (!maintenance)
                context.ServicePrincipals.Add(new ServicePrincipal
                {
                    Id = serviceId, Code = $"unprivileged-{serviceId:N}", DisplayName = "Ordinary service"
                });
            context.ExternalApiKeys.Add(new ExternalApiKey
            {
                Id = Guid.CreateVersion7(), KeyId = keyId, Name = "Native maintenance credential",
                SecretHash = ApiKeyHashing.ComputeHash(ApiKeyHashing.CreateSecret()), Scopes = "events:read",
                OwnerType = ExternalApiKeyOwnerType.InstanceAdmin, OwnerId = owner,
                ExternalApiKeyStatusId = (int)ExternalApiKeyStatusEnum.Active, ExternalApiKeyStatus = null!,
                ExternalApiKeyCreditPeriodId = (int)ExternalApiKeyCreditPeriodEnum.None, ExternalApiKeyCreditPeriod = null!
            });
            context.ModerationMachineSubjectBindings.Add(ModerationMachineSubjectBinding.Establish(
                Guid.CreateVersion7(), HostId, ModerationMachineCredentialSource.Persisted, keyId,
                null, ExternalApiKeyOwnerType.InstanceAdmin, owner, serviceId));
            await context.SaveChangesAsync();
            return new ApiKeyPrincipalContext(keyId, null, ExternalApiKeyOwnerType.InstanceAdmin, owner, [])
            {
                CredentialSource = ApiKeyCredentialSource.Persisted
            };
        }

        public async Task<Guid> AddResponseAsync()
        {
            await using var context = Context();
            var evidence = ModerationQualificationEvidence.RecordResponse(
                Guid.CreateVersion7(), Case.Id, Scope.TrustDomainId, TenantId, Subject.Id, Subject.Id,
                "The subject contests this occurrence.", Now);
            context.ModerationQualificationEvidence.Add(evidence);
            await context.SaveChangesAsync();
            return evidence.Id;
        }

        public async Task QualifyReviewerAsync(
            Guid humanId, Guid controllerId,
            ModerationReviewerConflictState state = ModerationReviewerConflictState.Independent)
        {
            await using var context = Context();
            context.ModerationTrustDomainId = Scope.TrustDomainId;
            var reviewer = await context.ModerationReviewerAssignments.SingleOrDefaultAsync(
                row => row.CaseId == Case.Id && row.ReviewerHumanId == humanId);
            if (reviewer is null)
            {
                reviewer = ModerationReviewerAssignment.Assign(Guid.CreateVersion7(), Case, 1, humanId, IssuerId, Now);
                context.ModerationReviewerAssignments.Add(reviewer);
            }
            var identity = ModerationQualificationEvidence.QualifyController(Guid.CreateVersion7(), Case.Id,
                Scope.TrustDomainId, TenantId, humanId, controllerId, AssessorId,
                "Independently qualified controller.", Now.AddMinutes(30), Now);
            var conflict = ModerationQualificationEvidence.AssessConflict(Guid.CreateVersion7(), Case.Id,
                Scope.TrustDomainId, TenantId, humanId, controllerId, AssessorId,
                (int)state, "Current reviewer conflict assessment.",
                Now.AddMinutes(30), Now);
            reviewer.QualifyController(reviewer.Revision, controllerId, identity.Id, AssessorId, Now.AddMinutes(30), Now);
            reviewer.RecordConflictAssessment(reviewer.Revision, state, conflict.Id, AssessorId, Now);
            context.AddRange(identity, conflict);
            await context.SaveChangesAsync();
        }

        public async Task<Guid> PrepareFindingAsync(bool urgent = false, bool acceptFinding = true)
        {
            var responseId = urgent ? Guid.CreateVersion7() : await AddResponseAsync();
            await QualifyReviewerAsync(ReviewerId, ReviewerId);
            await using var context = Context();
            context.ModerationTrustDomainId = Scope.TrustDomainId;
            var qualificationId = Guid.CreateVersion7();
            if (urgent)
            {
                var urgency = ModerationQualificationEvidence.QualifyUrgency(responseId, Case.Id,
                    Scope.TrustDomainId, TenantId, Subject.Id, UrgencyAssessorId,
                    "Separately qualified urgency.", Now.AddMinutes(30), Now);
                context.AddRange(urgency, ModerationQualificationMutationReceipt.Complete(qualificationId,
                    new string('A', 64), HostId, Scope.TrustDomainId, TenantId,
                    ModerationQualificationSourceKind.Evidence, urgency.Id, UrgencyAssessorId, 1, 1, Now));
            }
            var finding = ModerationFinding.Finalize(Guid.CreateVersion7(), Case, 1,
                Case.Scope, null, null, Rule.Rule, Rule.Rule,
                Rule.Reason, Rule.Reason,
                SubjectReference.Create(ModerationSubjectKind.Human, ReviewerId, Instance),
                urgent ? ModerationFindingEvidenceReference.QualifiedUrgency(responseId, qualificationId, Case.Id, 1, Now)
                    : ModerationFindingEvidenceReference.Response(responseId, Case.Id, 1, Now),
                "Independent final finding.", Now);
            Decision = await context.ModerationDecisions.SingleAsync(row => row.Id == Decision.Id);
            if (acceptFinding)
            {
                if (urgent)
                    Decision.AcceptUrgentFinding(finding, 1, Now);
                else
                {
                    Decision.RecordResponse(1, Now);
                    Decision.AcceptFinding(finding.Id, 2, Now);
                }
            }
            context.Add(finding);
            await context.SaveChangesAsync();
            return finding.Id;
        }

        public async Task<ModerationSanction> ApproveAsync()
        {
            await PrepareFindingAsync();
            await using var context = Context();
            context.ModerationTrustDomainId = Scope.TrustDomainId;
            Decision = await context.ModerationDecisions.SingleAsync(row => row.Id == Decision.Id);
            Decision.Approve(SubjectReference.Create(ModerationSubjectKind.Human, ReviewerId, Instance), Scope, 3, Now);
            var sanction = Decision.Activate(Guid.CreateVersion7(), 4, Now);
            await context.SaveChangesAsync();
            return sanction;
        }

        public async ValueTask DisposeAsync()
        {
            await Privacy.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    // Controlled procedural governance is separate from this harness's absent measured deployment.
    // Neither its observations nor these authored cases certify a production source or consumer.
    private sealed class ProceduralPolicyWithoutDeployment(NativeDatabase database)
        : IModerationActivationSourceOwner, IModerationQualificationPolicySourceOwner
    {
        private readonly Guid sourceId = Guid.CreateVersion7();
        private readonly Guid approvalId = Guid.CreateVersion7();
        public TimeSpan CurrentMaximumReviewerLifetime { get; set; } = TimeSpan.FromHours(1);
        public TimeSpan ApprovedMaximumReviewerLifetime { get; set; } = TimeSpan.FromHours(1);
        public TimeSpan CurrentMaximumUrgencyLifetime { get; set; } = TimeSpan.FromHours(1);
        public TimeSpan ApprovedMaximumUrgencyLifetime { get; set; } = TimeSpan.FromHours(1);
        public Task<ModerationActivationProfileObservation?> AcquireProfileAsync(
            IModerationConsumerEvidenceSession evidenceSession,
            Guid hostBindingId, Guid transactionId, ModerationPolicyCompiledDraft policy,
            ModerationPolicyCompiledProfile profile, ModerationDecision decision,
            DateTimeOffset observedAt, CancellationToken cancellationToken) =>
            Task.FromResult<ModerationActivationProfileObservation?>(null);
        public Task<ModerationQualificationPolicyCurrentSource?> AcquireQualificationPolicyAsync(
            Guid hostBindingId, Guid transactionId, Guid caseId, AuthorityScope scope,
            DateTimeOffset observedAt, CancellationToken cancellationToken) =>
            Task.FromResult<ModerationQualificationPolicyCurrentSource?>(new(
                transactionId, database.HostId, database.Case.Id, database.Scope, sourceId, 1,
                "fixture-procedural-policy", CurrentMaximumReviewerLifetime, CurrentMaximumUrgencyLifetime,
                observedAt, Now.AddHours(1)));
        public Task<ModerationQualificationPolicyApproval?> ReadQualificationPolicyApprovalAsync(
            Guid hostBindingId, Guid transactionId, Guid caseId, AuthorityScope scope,
            DateTimeOffset observedAt, CancellationToken cancellationToken) =>
            Task.FromResult<ModerationQualificationPolicyApproval?>(new(
                transactionId, database.HostId, database.Case.Id, database.Scope, approvalId, 1,
                sourceId, 1, "fixture-procedural-policy", ApprovedMaximumReviewerLifetime, ApprovedMaximumUrgencyLifetime,
                Now, Now.AddHours(1), observedAt));
        public Task<ModerationCapabilityRegistration?> ReadConformanceAsync(
            IModerationConsumerEvidenceSession evidenceSession,
            Guid transactionId, ModerationActivationBinding binding, string consumerId,
            DateTimeOffset observedAt, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("An absent deployment must stop before conformance.");
        public Task<ModerationCapabilitySourceObservation?> AcquireConsumerAsync(
            IModerationConsumerEvidenceSession evidenceSession,
            Guid transactionId, ModerationActivationBinding binding, string consumerId,
            DateTimeOffset observedAt, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("An absent deployment must stop before consumers.");
        public Task<ModerationPrerequisiteObservation?> AcquirePrerequisiteAsync(
            IModerationConsumerEvidenceSession evidenceSession,
            Guid transactionId, ModerationActivationBinding binding, ModerationActivationPrerequisite prerequisite,
            DateTimeOffset observedAt, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("An absent deployment must stop before prerequisites.");
    }

    private sealed class AdvanceQualificationAfterFlush(Guid operationId, Guid hostId, Guid tenantId) : SaveChangesInterceptor
    {
        public bool Injected { get; private set; }

        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (!Injected && eventData.Context is ExploreDbContext context
                && context.ModerationOperationReceipts.Local.Any(row => row.Id == operationId))
            {
                Injected = await context.ModerationQualificationEpochs
                    .Where(row => row.HostBindingId == hostId && row.TenantId == tenantId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.Revision, row => row.Revision + 1),
                        cancellationToken) == 1;
            }
            return result;
        }
    }

    private sealed class CloseCaseAfterFlush(Guid operationId, Guid caseId) : SaveChangesInterceptor
    {
        public bool Injected { get; private set; }

        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (!Injected && eventData.Context is ExploreDbContext context
                && context.ModerationOperationReceipts.Local.Any(row => row.Id == operationId))
            {
                Injected = true;
                var allegation = await context.ModerationCases.SingleAsync(row => row.Id == caseId, cancellationToken);
                allegation.Close(allegation.Revision, Now);
                await context.SaveChangesAsync(cancellationToken);
            }
            return result;
        }
    }

    private sealed class FixedClock(DateTimeOffset? value = null) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = value ?? Now;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
