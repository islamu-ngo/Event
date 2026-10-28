using System.Collections.Immutable;
using System.Security.Claims;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Handlers.Commands;
using Explore.Application.Features.Moderation.Policy;
using Explore.Application.Features.Moderation.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Identity;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationQualificationWriterTests
{
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
            OperationId = Guid.CreateVersion7(),
            Target = database.Scope,
            ExpectedQualificationRevision = 2,
            EvidenceId = Guid.CreateVersion7(),
            CaseId = opened.CaseId,
            ExpectedCaseRevision = 1,
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
            OperationId = Guid.CreateVersion7(),
            Target = database.Scope,
            ExpectedQualificationRevision = 2,
            EvidenceId = Guid.CreateVersion7(),
            CaseId = opened.CaseId,
            ExpectedCaseRevision = 1,
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
        var adoption = database.Publication(1);
        await Assert.That((await database.PublishAsync(database.AdministratorId, adoption)).Outcome)
            .IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        var replacement = adoption with
        {
            OperationId = Guid.CreateVersion7(),
            ExpectedQualificationRevision = 2,
            ExpectedPublicationId = adoption.Proposed.PublicationId,
            Proposed = adoption.Proposed with { ExpectedRevision = 1, PublicationId = Guid.CreateVersion7() }
        };
        await Assert.That((await database.PublishAsync(database.AdministratorId, replacement)).Outcome)
            .IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        var changed = replacement with
        {
            OperationId = Guid.CreateVersion7(),
            ExpectedQualificationRevision = 3,
            ExpectedPublicationId = replacement.Proposed.PublicationId,
            Proposed = replacement.Proposed with
            {
                ExpectedRevision = 2,
                PublicationId = Guid.CreateVersion7(),
                Reasons = [replacement.Proposed.Reasons.Single() with { Meaning = "A silently changed adopted meaning." }]
            }
        };
        var rejected = await database.PublishAsync(database.AdministratorId, changed);
        await Assert.That(rejected.Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Invalid);
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        var selection = await reader.ModerationPolicySelections.SingleAsync();
        var publication = await reader.ModerationPolicyPublications.Include(row => row.Reasons)
            .SingleAsync(row => row.Id == selection.LatestPublicationId);
        var reason = publication.Reasons.Single();
        await Assert.That(selection.Revision).IsEqualTo(2L);
        await Assert.That(reason.Meaning).IsEqualTo(adoption.Proposed.Reasons.Single().Meaning);
        await Assert.That(selection.ActivePublicationId).IsNull();
        await Assert.That(await reader.ModerationPolicies.CountAsync()).IsEqualTo(0);
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(3L);
    }

    [Test]
    public async Task Rich_publication_replay_requalifies_actual_administrator_and_preserves_its_head()
    {
        await using var database = await SourceDatabase.CreateAsync();
        var request = database.Publication(1);
        var first = await database.PublishAsync(database.AdministratorId, request);
        var replay = await database.PublishAsync(database.AdministratorId, request);
        await Assert.That(first.Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        await Assert.That(replay.Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        await Assert.That(replay.Publication!.Id).IsEqualTo(first.Publication!.Id);
        await using (var writer = database.Context())
        {
            writer.PlatformUserRoles.Remove(await writer.PlatformUserRoles.SingleAsync(row => row.UserId == database.AdministratorId));
            await writer.SaveChangesAsync();
        }
        await Assert.That((await database.PublishAsync(database.AdministratorId, request)).Outcome)
            .IsEqualTo(ModerationPolicyPublicationOutcome.Denied);
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That(await reader.ModerationPolicyPublications.CountAsync()).IsEqualTo(1);
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(2L);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Reviewer_and_urgency_leases_use_their_independently_adopted_limits(bool urgency)
    {
        await using var database = await SourceDatabase.CreateAsync();
        var publication = database.Publication(1);
        await Assert.That((await database.PublishAsync(database.AdministratorId, publication)).Outcome)
            .IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        var opened = database.OpenCase() with { ExpectedQualificationRevision = 2 };
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, opened)).IsSuccess).IsTrue();
        // The selected authored profile lasts an hour. The two procedural leases are unrelated:
        // shortening one must not deny the other or relax the shortened one.
        database.ReviewerLifetime = database.ApprovedReviewerLifetime = TimeSpan.FromMinutes(urgency ? 10 : 40);
        database.UrgencyLifetime = database.ApprovedUrgencyLifetime = TimeSpan.FromMinutes(urgency ? 40 : 10);
        var assignmentId = Guid.CreateVersion7();
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, new AssignModerationReviewerSource
        {
            OperationId = Guid.CreateVersion7(),
            Target = database.Scope,
            ExpectedQualificationRevision = 3,
            AssignmentId = assignmentId,
            CaseId = opened.CaseId,
            ExpectedCaseRevision = 1,
            ReviewerHumanId = database.ReviewerId
        })).IsSuccess).IsTrue();
        ModerationQualificationMutation Lease(bool urgent, long generation) => urgent
            ? new QualifyModerationCaseUrgency
            {
                OperationId = Guid.CreateVersion7(),
                Target = database.Scope,
                ExpectedQualificationRevision = generation,
                CaseId = opened.CaseId,
                ExpectedCaseRevision = 1,
                EvidenceId = Guid.CreateVersion7(),
                Justification = "Case urgency requires its own adopted lease.",
                ValidUntil = Now.AddMinutes(30)
            }
            : new QualifyModerationReviewerController
            {
                OperationId = Guid.CreateVersion7(),
                Target = database.Scope,
                ExpectedQualificationRevision = generation,
                AssignmentId = assignmentId,
                ExpectedAssignmentRevision = 1,
                ControllerHumanId = database.ReviewerId,
                EvidenceId = Guid.CreateVersion7(),
                Justification = "Controller qualification requires its own adopted lease.",
                QualifiedUntil = Now.AddMinutes(30)
            };
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, Lease(!urgency, 4))).FailureCode)
            .IsEqualTo(FailureCodes.AdminRequired);
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, Lease(urgency, 4))).IsSuccess).IsTrue();
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That(await reader.ModerationQualificationEvidence.CountAsync()).IsEqualTo(1);
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(5L);
        await Assert.That(await reader.ModerationPolicies.CountAsync()).IsEqualTo(0);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task Missing_or_disagreed_procedural_approval_never_uses_authoring_limits(int missingKind)
    {
        await using var database = await SourceDatabase.CreateAsync();
        await Assert.That((await database.PublishAsync(database.AdministratorId, database.Publication(1))).Outcome)
            .IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        var opened = database.OpenCase() with { ExpectedQualificationRevision = 2 };
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, opened)).IsSuccess).IsTrue();
        if (missingKind == 2)
            database.ApprovedUrgencyLifetime = TimeSpan.FromMinutes(20);
        else if (missingKind == 0)
            database.GovernanceOwnerAvailable = false;
        else
            database.GovernanceAvailable = false;
        var rejected = await database.ExecuteAsync(database.AdministratorId, new QualifyModerationCaseUrgency
        {
            OperationId = Guid.CreateVersion7(),
            Target = database.Scope,
            ExpectedQualificationRevision = 3,
            CaseId = opened.CaseId,
            ExpectedCaseRevision = 1,
            EvidenceId = Guid.CreateVersion7(),
            Justification = "Authoring cannot replace independent procedural adoption.",
            ValidUntil = Now.AddMinutes(10)
        });
        await Assert.That(rejected.FailureCode).IsEqualTo("ModerationAuthorityUnavailable");
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That(await reader.ModerationQualificationEvidence.CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.ModerationQualificationMutationReceipts.CountAsync()).IsEqualTo(1);
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(3L);
    }

    [Test]
    public async Task Missing_procedural_source_rolls_back_its_partial_flush_and_never_returns_known()
    {
        await using var database = await SourceDatabase.CreateAsync();
        var opened = database.OpenCase();
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, opened)).IsSuccess).IsTrue();
        database.FlushBeforeUnavailable = true;
        var operationId = Guid.CreateVersion7();
        var rejected = await database.ExecuteAsync(database.AdministratorId, new QualifyModerationCaseUrgency
        {
            OperationId = operationId,
            Target = database.Scope,
            ExpectedQualificationRevision = 2,
            CaseId = opened.CaseId,
            ExpectedCaseRevision = 1,
            EvidenceId = Guid.CreateVersion7(),
            Justification = "The missing owner cannot commit a flushed prefix.",
            ValidUntil = Now.AddMinutes(10)
        });
        await Assert.That(rejected.FailureCode).IsEqualTo("ModerationAuthorityUnavailable");
        await Assert.That(rejected.Revision).IsNull();
        await Assert.That(database.PartialFlushObserved).IsTrue();
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That(await reader.ModerationQualificationEvidence.AnyAsync(row => row.Id == database.PartialEvidenceId)).IsFalse();
        await Assert.That(await reader.ModerationQualificationEvidence.CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.ModerationQualificationMutationReceipts.AnyAsync(row => row.Id == operationId)).IsFalse();
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(2L);
    }

    [Test]
    public async Task Procedural_deadline_expiry_after_source_flush_rolls_back_evidence_receipt_and_epoch()
    {
        await using var database = await SourceDatabase.CreateAsync();
        var opened = database.OpenCase();
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, opened)).IsSuccess).IsTrue();
        database.ExpireGovernanceAfterSave = true;
        var evidenceId = Guid.CreateVersion7();
        var operationId = Guid.CreateVersion7();
        var rejected = await database.ExecuteAsync(database.AdministratorId, new QualifyModerationCaseUrgency
        {
            OperationId = operationId,
            Target = database.Scope,
            ExpectedQualificationRevision = 2,
            CaseId = opened.CaseId,
            ExpectedCaseRevision = 1,
            EvidenceId = evidenceId,
            Justification = "Acquired policy validity must survive the committing flush.",
            ValidUntil = Now.AddMinutes(10)
        });
        await Assert.That(rejected.FailureCode).IsEqualTo("ModerationAuthorityUnavailable");
        await Assert.That(rejected.Revision).IsNull();
        await Assert.That(database.EvaluationTime).IsEqualTo(Now.AddHours(1));
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That(await reader.ModerationQualificationEvidence.AnyAsync(row => row.Id == evidenceId)).IsFalse();
        await Assert.That(await reader.ModerationQualificationMutationReceipts.AnyAsync(row => row.Id == operationId)).IsFalse();
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(2L);
    }

    [Test]
    public async Task Conflict_assessment_cannot_renew_the_original_controller_lease_under_a_shorter_policy()
    {
        await using var database = await SourceDatabase.CreateAsync();
        var opened = database.OpenCase();
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, opened)).IsSuccess).IsTrue();
        var assignmentId = Guid.CreateVersion7();
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, new AssignModerationReviewerSource
        {
            OperationId = Guid.CreateVersion7(),
            Target = database.Scope,
            ExpectedQualificationRevision = 2,
            AssignmentId = assignmentId,
            CaseId = opened.CaseId,
            ExpectedCaseRevision = 1,
            ReviewerHumanId = database.ReviewerId
        })).IsSuccess).IsTrue();
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, new QualifyModerationReviewerController
        {
            OperationId = Guid.CreateVersion7(),
            Target = database.Scope,
            ExpectedQualificationRevision = 3,
            AssignmentId = assignmentId,
            ExpectedAssignmentRevision = 1,
            ControllerHumanId = database.ReviewerId,
            EvidenceId = Guid.CreateVersion7(),
            Justification = "The original controller qualification lasts thirty minutes.",
            QualifiedUntil = Now.AddMinutes(30)
        })).IsSuccess).IsTrue();
        database.EvaluationTime = Now.AddMinutes(25);
        database.ReviewerLifetime = database.ApprovedReviewerLifetime = TimeSpan.FromMinutes(10);
        var rejected = await database.ExecuteAsync(database.AdministratorId, new AssessModerationReviewerConflict
        {
            OperationId = Guid.CreateVersion7(),
            Target = database.Scope,
            ExpectedQualificationRevision = 4,
            AssignmentId = assignmentId,
            ExpectedAssignmentRevision = 2,
            State = ModerationReviewerConflictState.Independent,
            EvidenceId = Guid.CreateVersion7(),
            Justification = "Only five minutes remain, but the original lease exceeds current policy."
        });
        await Assert.That(rejected.FailureCode).IsEqualTo(FailureCodes.AdminRequired);
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        var assignment = await reader.ModerationReviewerAssignments.SingleAsync();
        await Assert.That(assignment.Revision).IsEqualTo(2L);
        await Assert.That(assignment.ConflictState).IsEqualTo(ModerationReviewerConflictState.Unresolved);
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(4L);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Proposed_controller_cannot_certify_their_own_identity_or_replace_existing_qualification(bool assessed)
    {
        await using var database = await SourceDatabase.CreateAsync();
        await Assert.That((await database.PublishAsync(database.AdministratorId, database.Publication(1))).Outcome)
            .IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        var opened = database.OpenCase() with { ExpectedQualificationRevision = 2 };
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, opened)).IsSuccess).IsTrue();
        var assignmentId = Guid.CreateVersion7();
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, new AssignModerationReviewerSource
        {
            OperationId = Guid.CreateVersion7(),
            Target = database.Scope,
            ExpectedQualificationRevision = 3,
            AssignmentId = assignmentId,
            CaseId = opened.CaseId,
            ExpectedCaseRevision = 1,
            ReviewerHumanId = database.ReviewerId
        })).IsSuccess).IsTrue();
        var identityId = Guid.CreateVersion7();
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, new QualifyModerationReviewerController
        {
            OperationId = Guid.CreateVersion7(),
            Target = database.Scope,
            ExpectedQualificationRevision = 4,
            AssignmentId = assignmentId,
            ExpectedAssignmentRevision = 1,
            ControllerHumanId = database.ReviewerId,
            EvidenceId = identityId,
            Justification = "Independent qualification of the existing controller.",
            QualifiedUntil = Now.AddMinutes(30)
        })).IsSuccess).IsTrue();
        if (assessed)
            await Assert.That((await database.ExecuteAsync(database.AdministratorId, new AssessModerationReviewerConflict
            {
                OperationId = Guid.CreateVersion7(),
                Target = database.Scope,
                ExpectedQualificationRevision = 5,
                AssignmentId = assignmentId,
                ExpectedAssignmentRevision = 2,
                State = ModerationReviewerConflictState.Independent,
                EvidenceId = Guid.CreateVersion7(),
                Justification = "Independent conflict assessment of the existing controller."
            })).IsSuccess).IsTrue();
        var operationId = Guid.CreateVersion7();
        var evidenceId = Guid.CreateVersion7();
        var denied = await database.ExecuteAsync(database.AdministratorId, new QualifyModerationReviewerController
        {
            OperationId = operationId,
            Target = database.Scope,
            ExpectedQualificationRevision = assessed ? 6 : 5,
            AssignmentId = assignmentId,
            ExpectedAssignmentRevision = assessed ? 3 : 2,
            ControllerHumanId = database.AdministratorId,
            EvidenceId = evidenceId,
            Justification = "The proposed controller attempts to certify their own identity.",
            QualifiedUntil = Now.AddMinutes(30)
        });
        await Assert.That(denied.FailureCode).IsEqualTo(FailureCodes.AdminRequired);
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        var assignment = await reader.ModerationReviewerAssignments.SingleAsync();
        await Assert.That(assignment.Revision).IsEqualTo(assessed ? 3L : 2L);
        await Assert.That(assignment.ControllerHumanId).IsEqualTo((Guid?)database.ReviewerId);
        await Assert.That(assignment.IdentityEvidenceId).IsEqualTo((Guid?)identityId);
        await Assert.That(assignment.IsQualifiedAt(Now)).IsEqualTo(assessed);
        await Assert.That(await reader.ModerationQualificationEvidence.AnyAsync(row => row.Id == evidenceId)).IsFalse();
        await Assert.That(await reader.ModerationQualificationMutationReceipts.AnyAsync(row => row.Id == operationId)).IsFalse();
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(assessed ? 6L : 5L);
    }

    [Test]
    public async Task Reviewer_cannot_attest_their_own_controller_even_with_native_admin_role()
    {
        await using var database = await SourceDatabase.CreateAsync();
        await Assert.That((await database.PublishAsync(database.AdministratorId, database.Publication(1))).Outcome)
            .IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        var opened = database.OpenCase() with { ExpectedQualificationRevision = 2 };
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, opened)).IsSuccess).IsTrue();
        var assignmentId = Guid.CreateVersion7();
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, new AssignModerationReviewerSource
        {
            OperationId = Guid.CreateVersion7(),
            Target = database.Scope,
            ExpectedQualificationRevision = 3,
            AssignmentId = assignmentId,
            CaseId = opened.CaseId,
            ExpectedCaseRevision = 1,
            ReviewerHumanId = database.ReviewerId
        })).IsSuccess).IsTrue();
        var denied = await database.ExecuteAsync(database.ReviewerId, new QualifyModerationReviewerController
        {
            OperationId = Guid.CreateVersion7(),
            Target = database.Scope,
            ExpectedQualificationRevision = 4,
            AssignmentId = assignmentId,
            ExpectedAssignmentRevision = 1,
            ControllerHumanId = database.ReviewerId,
            EvidenceId = Guid.CreateVersion7(),
            Justification = "Attempted self-qualification.",
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
    [Arguments(false, 0, 0, ModerationReferenceProfileId.CFG06)]
    [Arguments(true, 0, 0, ModerationReferenceProfileId.CFG06)]
    [Arguments(true, 1, 0, ModerationReferenceProfileId.CFG06)]
    [Arguments(true, 2, 0, ModerationReferenceProfileId.CFG06)]
    [Arguments(false, 0, 1, ModerationReferenceProfileId.CFG06)]
    [Arguments(false, 0, 2, ModerationReferenceProfileId.CFG06)]
    [Arguments(false, 0, 3, ModerationReferenceProfileId.CFG06)]
    [Arguments(false, 0, 4, ModerationReferenceProfileId.CFG06)]
    [Arguments(true, 0, 5, ModerationReferenceProfileId.CFG06)]
    [Arguments(false, 0, 6, ModerationReferenceProfileId.CFG06)]
    [Arguments(false, 0, 7, ModerationReferenceProfileId.CFG06)]
    [Arguments(false, 0, 8, ModerationReferenceProfileId.CFG06)]
    [Arguments(false, 0, 9, ModerationReferenceProfileId.CFG06)]
    [Arguments(false, 0, 0, ModerationReferenceProfileId.CFG05)]
    public async Task Qualified_native_finding_requires_current_evidence_and_preserves_reviewer_and_audit(
        bool urgent, int withdrawnQualification, int publicationChange, ModerationReferenceProfileId referenceProfile)
    {
        await using var database = await SourceDatabase.CreateAsync();
        var adoption = database.Publication(1, referenceProfile);
        await Assert.That((await database.PublishAsync(database.AdministratorId, adoption)).Outcome)
            .IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        var opened = database.OpenCase() with { ExpectedQualificationRevision = 2 };
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, opened)).IsSuccess).IsTrue();
        var responseId = Guid.CreateVersion7();
        var evidenceOperationId = Guid.CreateVersion7();
        ModerationQualificationMutation evidence = urgent ? new QualifyModerationCaseUrgency
        {
            OperationId = evidenceOperationId,
            Target = database.Scope,
            ExpectedQualificationRevision = 3,
            EvidenceId = responseId,
            CaseId = opened.CaseId,
            ExpectedCaseRevision = 1,
            Justification = "An independent native assessor qualifies the bounded urgent case.",
            ValidUntil = Now.AddMinutes(30)
        } : new RecordModerationCaseResponse
        {
            OperationId = evidenceOperationId,
            Target = database.Scope,
            ExpectedQualificationRevision = 3,
            EvidenceId = responseId,
            CaseId = opened.CaseId,
            ExpectedCaseRevision = 1,
            Statement = "The actual subject responds before the final finding."
        };
        await Assert.That((await database.ExecuteAsync(
            urgent ? database.UrgencyAssessorId : database.SubjectId, evidence)).IsSuccess).IsTrue();
        var assignmentId = Guid.CreateVersion7();
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, new AssignModerationReviewerSource
        {
            OperationId = Guid.CreateVersion7(),
            Target = database.Scope,
            ExpectedQualificationRevision = 4,
            AssignmentId = assignmentId,
            CaseId = opened.CaseId,
            ExpectedCaseRevision = 1,
            ReviewerHumanId = database.ReviewerId
        })).IsSuccess).IsTrue();
        var controllerEvidenceId = Guid.CreateVersion7();
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, new QualifyModerationReviewerController
        {
            OperationId = Guid.CreateVersion7(),
            Target = database.Scope,
            ExpectedQualificationRevision = 5,
            AssignmentId = assignmentId,
            ExpectedAssignmentRevision = 1,
            ControllerHumanId = database.ReviewerId,
            EvidenceId = controllerEvidenceId,
            Justification = "The independent native assessor records canonical control.",
            QualifiedUntil = Now.AddMinutes(30)
        })).IsSuccess).IsTrue();
        var conflictEvidenceId = Guid.CreateVersion7();
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, new AssessModerationReviewerConflict
        {
            OperationId = Guid.CreateVersion7(),
            Target = database.Scope,
            ExpectedQualificationRevision = 6,
            AssignmentId = assignmentId,
            ExpectedAssignmentRevision = 2,
            State = ModerationReviewerConflictState.Independent,
            EvidenceId = conflictEvidenceId,
            Justification = "The native assessor records the current case-specific independence."
        })).IsSuccess).IsTrue();
        if (withdrawnQualification != 0)
        {
            await using var source = database.Context();
            source.ModerationTrustDomainId = database.Scope.TrustDomainId;
            if (withdrawnQualification == 1)
                source.ModerationQualificationMutationReceipts.Remove(
                    await source.ModerationQualificationMutationReceipts.SingleAsync(row => row.Id == evidenceOperationId));
            else
                source.PlatformUserRoles.Remove(
                    await source.PlatformUserRoles.SingleAsync(row => row.UserId == database.UrgencyAssessorId));
            await source.SaveChangesAsync();
        }
        var findingGeneration = 7L;
        if (publicationChange == 1)
        {
            var replacement = adoption with
            {
                OperationId = Guid.CreateVersion7(),
                ExpectedQualificationRevision = 7,
                ExpectedPublicationId = adoption.Proposed.PublicationId,
                Proposed = adoption.Proposed with { ExpectedRevision = 1, PublicationId = Guid.CreateVersion7() }
            };
            await Assert.That((await database.PublishAsync(database.AdministratorId, replacement)).Outcome)
                .IsEqualTo(ModerationPolicyPublicationOutcome.Known);
            findingGeneration = 8;
        }
        else if (publicationChange == 3)
        {
            await using var source = database.Context();
            source.ModerationTrustDomainId = database.Scope.TrustDomainId;
            source.Set<ModerationPolicyPublicationPlan>().Remove(
                await source.Set<ModerationPolicyPublicationPlan>().SingleAsync(row =>
                    row.PublicationId == adoption.Proposed.PublicationId && row.Strategy == 0));
            await source.SaveChangesAsync();
        }
        else if (publicationChange == 4)
            database.ReviewerLifetime = database.ApprovedReviewerLifetime = TimeSpan.FromMinutes(10);
        else if (publicationChange == 5)
            database.UrgencyLifetime = database.ApprovedUrgencyLifetime = TimeSpan.FromMinutes(10);
        var findingId = Guid.CreateVersion7();
        var rule = adoption.Proposed.Profiles.Single();
        var finalization = new FinalizeModerationFindingSource
        {
            OperationId = Guid.CreateVersion7(),
            Target = database.Scope,
            ExpectedQualificationRevision = findingGeneration,
            FindingId = findingId,
            CaseId = opened.CaseId,
            ExpectedCaseRevision = 1,
            PolicyId = adoption.Proposed.PolicyId,
            ExpectedPolicyRevision = 1,
            Rule = publicationChange == 2 ? rule.Reason : rule.Rule,
            Reason = publicationChange switch
            {
                6 => rule.Rule,
                7 => ModerationRuleReference.Create(rule.Reason.OwnerId, rule.Reason.Code, rule.Reason.Revision + 1),
                8 => ModerationRuleReference.Create(database.Instance.Id, rule.Reason.Code, rule.Reason.Revision),
                9 => ModerationRuleReference.Create(rule.Reason.OwnerId, "different-reason", rule.Reason.Revision),
                _ => rule.Reason
            },
            EvidenceId = responseId,
            Justification = "The qualified native reviewer finalizes the adopted rule finding."
        };
        if (withdrawnQualification == 0 && publicationChange == 0)
        {
            var selfAdjudication = await database.ExecuteAsync(database.SubjectId,
                finalization with { OperationId = Guid.CreateVersion7() });
            await Assert.That(selfAdjudication.IsSuccess).IsFalse();
            var foreignTenant = await database.ExecuteAsync(database.ReviewerId, finalization with
            {
                OperationId = Guid.CreateVersion7(),
                Target = AuthorityScope.Tenant(database.Instance, Guid.CreateVersion7())
            });
            await Assert.That(foreignTenant.IsSuccess).IsFalse();
            await using var beforeFinding = database.Context();
            beforeFinding.ModerationTrustDomainId = database.Scope.TrustDomainId;
            await Assert.That(await beforeFinding.ModerationFindings.CountAsync()).IsEqualTo(0);
            await Assert.That((await beforeFinding.ModerationQualificationEpochs.SingleAsync()).Revision)
                .IsEqualTo(findingGeneration);
        }
        var finalized = await database.ExecuteAsync(database.ReviewerId, finalization);
        if (withdrawnQualification != 0 || publicationChange != 0)
        {
            await Assert.That(finalized.IsSuccess).IsFalse();
            await Assert.That(finalized.FailureCode).IsEqualTo(publicationChange == 1
                ? FailureCodes.ConcurrencyConflict
                : withdrawnQualification == 1 || publicationChange == 3
                    ? "ModerationAuthorityUnavailable" : FailureCodes.AdminRequired);
            await using var unavailable = database.Context();
            unavailable.ModerationTrustDomainId = database.Scope.TrustDomainId;
            await Assert.That(await unavailable.ModerationFindings.CountAsync()).IsEqualTo(0);
            await Assert.That((await unavailable.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(findingGeneration);
            await Assert.That(await unavailable.ModerationQualificationMutationReceipts
                .AnyAsync(row => row.Id == evidenceOperationId)).IsEqualTo(withdrawnQualification != 1);
            return;
        }
        await Assert.That(finalized.IsSuccess).IsTrue();
        var replayed = await database.ExecuteAsync(database.ReviewerId, finalization);
        await Assert.That(replayed.IsSuccess).IsTrue();
        await Assert.That(replayed.Id).IsEqualTo(finalized.Id);
        await Assert.That(replayed.Revision).IsEqualTo(finalized.Revision);
        await Assert.That(replayed.QualificationRevision).IsEqualTo(finalized.QualificationRevision);
        var changedReplay = await database.ExecuteAsync(database.ReviewerId,
            finalization with { Justification = "A changed explanation is not the original operation." });
        await Assert.That(changedReplay.IsSuccess).IsFalse();
        await Assert.That(changedReplay.FailureCode).IsEqualTo(FailureCodes.ConcurrencyConflict);
        var replacementReason = ModerationRuleReference.Create(
            rule.Reason.OwnerId, rule.Reason.Code, rule.Reason.Revision + 1);
        var replacementProfile = rule with
        {
            Id = Guid.CreateVersion7(),
            Rule = ModerationRuleReference.Create(rule.Rule.OwnerId, rule.Rule.Code, rule.Rule.Revision + 1),
            Reason = replacementReason
        };
        var replacementPublication = await database.PublishAsync(database.AdministratorId, adoption with
        {
            OperationId = Guid.CreateVersion7(),
            ExpectedQualificationRevision = 8,
            ExpectedPublicationId = adoption.Proposed.PublicationId,
            Proposed = adoption.Proposed with
            {
                ExpectedRevision = 1,
                PublicationId = Guid.CreateVersion7(),
                Reasons = adoption.Proposed.Reasons.Add(adoption.Proposed.Reasons.Single() with
                {
                    Reference = replacementReason,
                    Meaning = "Revised reason applies only to new findings."
                }),
                Profiles = adoption.Proposed.Profiles.Add(replacementProfile),
                SupportedProfiles = [replacementProfile.Rule]
            }
        });
        await Assert.That(replacementPublication.Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        var replacementHistory = ModerationPolicyPublicationProjection.RestoreHistory(replacementPublication.Publication!);
        await Assert.That(replacementHistory!.Profiles.Single(row => row.Definition.Rule == replacementProfile.Rule)
            .Definition.Reason).IsEqualTo(replacementReason);
        var reversalId = Guid.CreateVersion7();
        var reversal = new ReverseModerationFindingSource
        {
            OperationId = Guid.CreateVersion7(),
            Target = database.Scope,
            ExpectedQualificationRevision = 9,
            FindingId = findingId,
            ExpectedFindingRevision = 1,
            ReversalReferenceId = reversalId,
            Justification = "The native correction preserves the adjudication and its response."
        };
        var selfReversal = await database.ExecuteAsync(database.SubjectId,
            reversal with { OperationId = Guid.CreateVersion7() });
        await Assert.That(selfReversal.IsSuccess).IsFalse();
        var reversed = await database.ExecuteAsync(database.AdministratorId, reversal);
        await Assert.That(reversed.IsSuccess).IsTrue();
        var reversalReplay = await database.ExecuteAsync(database.AdministratorId, reversal);
        await Assert.That(reversalReplay.IsSuccess).IsTrue();
        await Assert.That(reversalReplay.Revision).IsEqualTo(reversed.Revision);
        await Assert.That(reversalReplay.QualificationRevision).IsEqualTo(reversed.QualificationRevision);
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        var finding = await reader.ModerationFindings.SingleAsync();
        finding.ValidatePersistedState();
        await Assert.That(finding.State).IsEqualTo(ModerationFindingState.Reversed);
        await Assert.That(finding.Revision).IsEqualTo(2L);
        await Assert.That(finding.AdjudicatorId).IsEqualTo(database.ReviewerId);
        await Assert.That(finding.Rule).IsEqualTo(rule.Rule);
        await Assert.That(finding.Reason).IsEqualTo(rule.Reason);
        await Assert.That(finding.Reason == finding.Rule).IsFalse();
        await Assert.That(finding.Reason == replacementReason).IsFalse();
        await Assert.That(finding.Rule == rule.Reason).IsFalse();
        await Assert.That(finding.EvidenceReferenceId).IsEqualTo(responseId);
        await Assert.That(finding.EvidencePurpose).IsEqualTo(urgent
            ? ModerationFindingEvidencePurpose.QualifiedUrgency : ModerationFindingEvidencePurpose.Response);
        await Assert.That(finding.UrgencyQualificationId).IsEqualTo(urgent ? (Guid?)evidenceOperationId : null);
        await Assert.That(finding.ReversalReferenceId).IsEqualTo((Guid?)reversalId);
        await Assert.That(finding.ReversedById).IsEqualTo((Guid?)database.AdministratorId);
        await Assert.That(await reader.ModerationQualificationEvidence.CountAsync()).IsEqualTo(3);
        await Assert.That(await reader.ModerationQualificationMutationReceipts.CountAsync()).IsEqualTo(7);
        await Assert.That(await reader.ModerationPolicies.CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.ModerationDecisions.CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.ModerationSanctions.CountAsync()).IsEqualTo(0);
        var declared = await reader.Set<ModerationPolicyPublicationEffect>()
            .SingleAsync(row => row.PublicationId == adoption.Proposed.PublicationId);
        await Assert.That(declared.Meaning).IsEqualTo(rule.Effects.Single());
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(10L);
    }

    internal sealed partial class SourceDatabase
    {
        public async Task<ModerationQualificationResponse> ExecuteAsync(
            Guid actor, ModerationQualificationMutation mutation, bool machineCaller = false, IInterceptor? interceptor = null)
        {
            await using var context = Context(interceptor);
            var identity = Identity(context, actor, machineCaller);
            var clock = new SourceClock(this);
            var publications = new ModerationPolicyPublicationRepository(context, identity.Admin, identity.Machine, Inventory, null, clock);
            var governance = GovernanceOwnerAvailable ? new ControlledGovernance(context, this) : null;
            var activation = new ModerationNativeActivationQualification(
                context, Inventory, null, governance, clock);
            var repository = new ModerationQualificationRepository(context, identity.Admin, identity.Machine, publications, activation, clock,
                      Privacy.Authority(context, clock),
                       ConsumerRuntimeSources, new Explore.Infrastructure.Services.Moderation.ModerationMeasurementReceiptVerifier(),
                       deploymentInventory: Inventory);
            var response = mutation switch
            {
                FinalizeModerationFindingSource finding => await new FinalizeModerationFindingCommandHandler(repository)
                    .ExecuteAsync(new()
                    {
                        OperationId = finding.OperationId,
                        Target = finding.Target,
                        ExpectedQualificationRevision = finding.ExpectedQualificationRevision,
                        FindingId = finding.FindingId,
                        CaseId = finding.CaseId,
                        ExpectedCaseRevision = finding.ExpectedCaseRevision,
                        PolicyId = finding.PolicyId,
                        ExpectedPolicyRevision = finding.ExpectedPolicyRevision,
                        Rule = finding.Rule,
                        Reason = finding.Reason,
                        EvidenceId = finding.EvidenceId,
                        Justification = finding.Justification
                    }),
                ReverseModerationFindingSource finding => await new ReverseModerationFindingCommandHandler(repository)
                    .ExecuteAsync(new()
                    {
                        OperationId = finding.OperationId,
                        Target = finding.Target,
                        ExpectedQualificationRevision = finding.ExpectedQualificationRevision,
                        FindingId = finding.FindingId,
                        ExpectedFindingRevision = finding.ExpectedFindingRevision,
                        ReversalReferenceId = finding.ReversalReferenceId,
                        Justification = finding.Justification
                    }),
                _ => await new ApplyModerationQualificationCommandHandler(repository).ExecuteAsync(new(mutation))
            };
            if (context.ModerationTrustDomainId is not null || context.ChangeTracker.HasChanges())
                throw new InvalidOperationException("The source writer did not clear its acquisition boundary.");
            return response;
        }

        public ModerationAuthorityParticipant Participant(ExploreDbContext context, Guid caller, TimeProvider clock,
            IModerationMeasurementReceiptVerifier? measurementVerifier = null, bool useMeasurementVerifier = true)
        {
            var identity = Identity(context, caller, false);
            var publications = new ModerationPolicyPublicationRepository(
                context, identity.Admin, identity.Machine, Inventory, null, clock);
            var governance = new ControlledGovernance(context, this);
            var activation = new ModerationNativeActivationQualification(context, Inventory, null, governance, clock);
            var keys = new ExternalApiKeyAuthenticationRepository(new SecurityTestSupport.AdministrativeAuthorityTestFactory(context));
            var bindings = new ModerationMachineSubjectBindingRepository(context, identity.Admin, identity.Machine,
                keys, new ModerationNativeAuthorityParticipantTests.UnconfiguredKeys(), clock);
            return new(context, identity.Admin, identity.Machine, bindings, keys, publications, Inventory, activation, clock,
                ConsumerRuntimeSources, useMeasurementVerifier
                    ? measurementVerifier ?? new Explore.Infrastructure.Services.Moderation.ModerationMeasurementReceiptVerifier()
                    : null);
        }

    }

    private sealed class SourceClock(SourceDatabase database) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => database.EvaluationTime;
    }

    // Controlled external governance supplies procedural evidence only. It does not supply
    // deployment/conformance observations and is not evidence of a production governance owner.
    private sealed class ControlledGovernance(ExploreDbContext context, SourceDatabase database)
        : IModerationActivationSourceOwner, IModerationQualificationPolicySourceOwner
    {
        public async Task<ModerationQualificationPolicyCurrentSource?> AcquireQualificationPolicyAsync(
            Guid hostBindingId, Guid transactionId, Guid caseId, AuthorityScope scope,
            DateTimeOffset observedAt, CancellationToken cancellationToken)
        {
            if (caseId != database.CaseId || scope != database.CaseScope || hostBindingId != database.HostId)
                return null;
            if (database.FlushBeforeUnavailable)
            {
                context.ModerationQualificationEvidence.Add(ModerationQualificationEvidence.RecordResponse(
                    database.PartialEvidenceId, database.CaseId, database.Scope.TrustDomainId, database.TenantId,
                    database.SubjectId, database.SubjectId, "A flushed prefix must roll back with missing governance.", Now));
                await context.SaveChangesAsync(cancellationToken);
                database.PartialFlushObserved = true;
                return null;
            }
            return database.GovernanceAvailable ? new(transactionId, database.HostId, database.CaseId, database.CaseScope,
                database.ProceduralSourceId, 1, "controlled-external-governance", database.ReviewerLifetime,
                database.UrgencyLifetime, observedAt, Now.AddHours(1)) : null;
        }

        public Task<ModerationQualificationPolicyApproval?> ReadQualificationPolicyApprovalAsync(
            Guid hostBindingId, Guid transactionId, Guid caseId, AuthorityScope scope,
            DateTimeOffset observedAt, CancellationToken cancellationToken) =>
            Task.FromResult<ModerationQualificationPolicyApproval?>(database.GovernanceAvailable
                ? new(transactionId, database.HostId, database.CaseId, database.CaseScope,
                    database.ProceduralApprovalId, 1, database.ProceduralSourceId, 1,
                    "controlled-external-governance", database.ApprovedReviewerLifetime,
                    database.ApprovedUrgencyLifetime, Now, Now.AddHours(1), observedAt) : null);

        public Task<ModerationActivationProfileObservation?> AcquireProfileAsync(
            IModerationConsumerEvidenceSession evidenceSession,
            Guid hostBindingId, Guid transactionId, ModerationPolicyCompiledDraft policy,
            ModerationPolicyCompiledProfile profile, ModerationDecision decision,
            DateTimeOffset observedAt, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Finding qualification must not fabricate deployment evidence.");

        public Task<ModerationCapabilityRegistration?> ReadConformanceAsync(
            IModerationConsumerEvidenceSession evidenceSession,
            Guid transactionId, ModerationActivationBinding binding, string consumerId,
            DateTimeOffset observedAt, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Finding qualification does not establish measured conformance.");

        public Task<ModerationCapabilitySourceObservation?> AcquireConsumerAsync(
            IModerationConsumerEvidenceSession evidenceSession,
            Guid transactionId, ModerationActivationBinding binding, string consumerId,
            DateTimeOffset observedAt, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Finding qualification must not acquire deployment consumers.");

        public Task<ModerationPrerequisiteObservation?> AcquirePrerequisiteAsync(
            IModerationConsumerEvidenceSession evidenceSession,
            Guid transactionId, ModerationActivationBinding binding, ModerationActivationPrerequisite prerequisite,
            DateTimeOffset observedAt, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Finding qualification must not invent activation prerequisites.");
    }

}
