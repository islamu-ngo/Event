using System.Data;
using System.Security.Claims;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Infrastructure.Identity;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SourceDatabase = Event.Persistence.IntegrationTests.Moderation.ModerationQualificationWriterTests.SourceDatabase;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed class ModerationQualificationPolicyWriterTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Publication_and_explicit_adoption_are_distinct_atomic_native_writes()
    {
        await using var database = await CreateCase();
        var publish = Publish(database);
        await Assert.That((await Execute(database, database.AdministratorId, publish)).Outcome)
            .IsEqualTo(ModerationQualificationWriteOutcome.Known);
        await using (var reader = Read(database))
        {
            await Assert.That(await reader.ModerationQualificationPolicySources.CountAsync()).IsEqualTo(1);
            await Assert.That(await reader.ModerationQualificationPolicyAdoptions.CountAsync()).IsEqualTo(0);
        }
        var unadopted = await Observe(database);
        await Assert.That(unadopted.Current!.SourceId).IsEqualTo(publish.SourceId);
        await Assert.That(unadopted.Approval).IsNull();
        await Assert.That(unadopted.Joined).IsNull();
        var adopt = Adopt(database, publish.SourceId);
        await Assert.That((await Execute(database, database.AdministratorId, adopt)).Outcome)
            .IsEqualTo(ModerationQualificationWriteOutcome.Known);
        var observed = await Observe(database);
        await Assert.That(observed.Current!.SourceRevision).IsEqualTo(1L);
        await Assert.That(observed.Approval!.ApprovalId).IsEqualTo(adopt.AdoptionId);
        await Assert.That(observed.Approval.ApprovedSourceId).IsEqualTo(publish.SourceId);
        await Assert.That(observed.Approval.ApprovedWriterGeneration).IsEqualTo(observed.Current.WriterGeneration);
        await Assert.That(observed.Joined!.ValidUntil).IsEqualTo(Now.AddHours(2));
        await Assert.That(observed.Joined.Current.MaximumReviewerLifetime).IsEqualTo(TimeSpan.FromMinutes(30));
        await Assert.That(observed.Joined.Current.MaximumUrgencyLifetime).IsEqualTo(TimeSpan.FromMinutes(5));
        await using var settled = Read(database);
        var source = await settled.ModerationQualificationPolicySources.SingleAsync();
        var adoption = await settled.ModerationQualificationPolicyAdoptions.SingleAsync();
        await Assert.That(adoption.MatchesCurrent(source, 1, Now)).IsTrue();
        await Assert.That(adoption.AdoptedByHumanId).IsEqualTo(database.AdministratorId);
        await Assert.That(source.PublishedByHumanId).IsEqualTo(database.AdministratorId);
        await Assert.That((await settled.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(4L);
        await Assert.That(await settled.ModerationQualificationMutationReceipts.CountAsync()).IsEqualTo(3);
        await Assert.That(await settled.ModerationDecisions.CountAsync()).IsEqualTo(0);
        await Assert.That(await settled.ModerationSanctions.CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task Exact_publication_retry_preserves_receipt_and_rejects_changed_request_or_actor()
    {
        await using var database = await CreateCase();
        var publish = Publish(database);
        var first = await Execute(database, database.AdministratorId, publish);
        var retry = await Execute(database, database.AdministratorId, publish);
        await Assert.That(retry.Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        await Assert.That(retry.Receipt!.ResultRevision).IsEqualTo(first.Receipt!.ResultRevision);
        await Assert.That((await Execute(database, database.AdministratorId,
            publish with { MaximumReviewerLifetime = TimeSpan.FromMinutes(15) })).Outcome)
            .IsEqualTo(ModerationQualificationWriteOutcome.Conflict);
        await Assert.That((await Execute(database, database.UrgencyAssessorId, publish)).Outcome)
            .IsEqualTo(ModerationQualificationWriteOutcome.Conflict);
        await using var reader = Read(database);
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(3L);
    }

    [Test]
    public async Task Adoption_must_name_current_source_revision_and_writer_generation()
    {
        await using var database = await CreateCase();
        var publish = Publish(database);
        await Execute(database, database.AdministratorId, publish);
        var adoption = Adopt(database, publish.SourceId);
        await Assert.That((await Execute(database, database.AdministratorId,
            adoption with { ExpectedSourceRevision = 2 })).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Conflict);
        await Assert.That((await Execute(database, database.AdministratorId,
            adoption with { ExpectedWriterGeneration = 2 })).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Conflict);
        await using var reader = Read(database);
        await Assert.That(await reader.ModerationQualificationPolicyAdoptions.CountAsync()).IsEqualTo(0);
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(3L);
    }

    [Test]
    public async Task Replaced_source_invalidates_old_approval_and_requires_new_explicit_adoption()
    {
        await using var database = await CreateCase();
        var publish = Publish(database);
        await Execute(database, database.AdministratorId, publish);
        await Execute(database, database.AdministratorId, Adopt(database, publish.SourceId));
        var replace = publish with
        {
            OperationId = Guid.CreateVersion7(), ExpectedQualificationRevision = 4, ExpectedSourceRevision = 1,
            MaximumUrgencyLifetime = TimeSpan.FromMinutes(2)
        };
        await Assert.That((await Execute(database, database.AdministratorId, replace)).Outcome)
            .IsEqualTo(ModerationQualificationWriteOutcome.Known);
        await using (var reader = Read(database))
        {
            var source = await reader.ModerationQualificationPolicySources.SingleAsync();
            var old = await reader.ModerationQualificationPolicyAdoptions.SingleAsync();
            await Assert.That(old.MatchesCurrent(source, 1, Now)).IsFalse();
            await Assert.That(old.MaximumUrgencyLifetime).IsEqualTo(TimeSpan.FromMinutes(5));
        }
        var unadopted = await Observe(database);
        await Assert.That(unadopted.Current!.SourceRevision).IsEqualTo(2L);
        await Assert.That(unadopted.Approval).IsNull();
        await Assert.That(unadopted.Joined).IsNull();
        await Assert.That((await Execute(database, database.AdministratorId, Urgency(database, 5))).Outcome)
            .IsEqualTo(ModerationQualificationWriteOutcome.Unavailable);
        await Assert.That((await Execute(database, database.AdministratorId, Adopt(database, publish.SourceId) with
        {
            ExpectedQualificationRevision = 5, ExpectedSourceRevision = 2, ExpectedWriterGeneration = 5
        })).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        var adopted = await Observe(database);
        await Assert.That(adopted.Joined!.Approval.ApprovedSourceRevision).IsEqualTo(2L);
        await Assert.That(adopted.Joined.Current.MaximumUrgencyLifetime).IsEqualTo(TimeSpan.FromMinutes(2));
        await Assert.That((await Execute(database, database.AdministratorId, Urgency(database, 6))).Outcome)
            .IsEqualTo(ModerationQualificationWriteOutcome.Denied);
        await Assert.That((await Execute(database, database.AdministratorId, Urgency(database, 6) with
        {
            ValidUntil = Now.AddMinutes(2)
        })).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
    }

    [Test]
    public async Task Revocation_is_exact_retryable_and_does_not_reenable_a_generation()
    {
        await using var database = await CreateCase();
        var publish = Publish(database);
        await Execute(database, database.AdministratorId, publish);
        var adopt = Adopt(database, publish.SourceId);
        await Execute(database, database.AdministratorId, adopt);
        var revoke = new RevokeModerationQualificationPolicyAdoption
        {
            OperationId = Guid.CreateVersion7(), Target = database.Scope, ExpectedQualificationRevision = 4,
            CaseId = database.CaseId, ExpectedCaseRevision = 1, SourceId = publish.SourceId,
            ExpectedSourceRevision = 1, AdoptionId = adopt.AdoptionId, ExpectedAdoptionRevision = 1
        };
        await Assert.That((await Execute(database, database.AdministratorId, revoke)).Outcome)
            .IsEqualTo(ModerationQualificationWriteOutcome.Known);
        await Assert.That((await Execute(database, database.AdministratorId, revoke)).Outcome)
            .IsEqualTo(ModerationQualificationWriteOutcome.Known);
        await Assert.That((await Execute(database, database.AdministratorId, Adopt(database, publish.SourceId) with
        {
            ExpectedQualificationRevision = 5
        })).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Conflict);
        var revoked = await Observe(database);
        await Assert.That(revoked.Current).IsNotNull();
        await Assert.That(revoked.Approval).IsNull();
        await Assert.That(revoked.Joined).IsNull();
        await Assert.That((await Execute(database, database.AdministratorId, Urgency(database, 5))).Outcome)
            .IsEqualTo(ModerationQualificationWriteOutcome.Unavailable);
        await using var reader = Read(database);
        var source = await reader.ModerationQualificationPolicySources.SingleAsync();
        var row = await reader.ModerationQualificationPolicyAdoptions.SingleAsync();
        await Assert.That(row.RevokedByHumanId).IsEqualTo((Guid?)database.AdministratorId);
        await Assert.That(row.MatchesCurrent(source, 1, Now)).IsFalse();
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(5L);
    }

    [Test]
    public async Task Claimed_admin_without_native_grant_cannot_publish_or_adopt()
    {
        await using var database = await CreateCase();
        var publish = Publish(database);
        await Assert.That((await Execute(database, database.SubjectId, publish)).Outcome)
            .IsEqualTo(ModerationQualificationWriteOutcome.Denied);
        await Execute(database, database.AdministratorId, publish);
        await Assert.That((await Execute(database, database.SubjectId, Adopt(database, publish.SourceId))).Outcome)
            .IsEqualTo(ModerationQualificationWriteOutcome.Denied);
        await using var reader = Read(database);
        await Assert.That(await reader.ModerationQualificationPolicyAdoptions.CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task Revoked_publisher_cannot_supply_adoption_or_replay_proof()
    {
        await using var database = await CreateCase();
        var publish = Publish(database);
        await Execute(database, database.AdministratorId, publish);
        await using (var context = database.Context())
        {
            context.PlatformUserRoles.Remove(await context.PlatformUserRoles
                .SingleAsync(row => row.UserId == database.AdministratorId));
            await context.SaveChangesAsync();
        }
        await Assert.That((await Execute(database, database.AdministratorId, publish)).Outcome)
            .IsEqualTo(ModerationQualificationWriteOutcome.Denied);
        await Assert.That((await Execute(database, database.UrgencyAssessorId, Adopt(database, publish.SourceId))).Outcome)
            .IsEqualTo(ModerationQualificationWriteOutcome.Denied);
    }

    [Test]
    public async Task Wrong_case_revision_or_epoch_cannot_leave_partial_policy_rows()
    {
        await using var database = await CreateCase();
        var publish = Publish(database);
        await Assert.That((await Execute(database, database.AdministratorId,
            publish with { ExpectedCaseRevision = 2 })).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Conflict);
        await Assert.That((await Execute(database, database.AdministratorId,
            publish with { ExpectedQualificationRevision = 1 })).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Conflict);
        await using var reader = Read(database);
        await Assert.That(await reader.ModerationQualificationPolicySources.CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.ModerationQualificationMutationReceipts.CountAsync()).IsEqualTo(1);
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(2L);
    }

    [Test]
    public async Task Adoption_cannot_certify_a_source_without_its_native_publication_receipt()
    {
        await using var database = await CreateCase();
        var publish = Publish(database);
        await Execute(database, database.AdministratorId, publish);
        await using (var writer = Read(database))
        {
            writer.ModerationQualificationMutationReceipts.Remove(
                await writer.ModerationQualificationMutationReceipts.SingleAsync(row => row.Id == publish.OperationId));
            await writer.SaveChangesAsync();
        }
        await Assert.That((await Execute(database, database.AdministratorId, Adopt(database, publish.SourceId))).Outcome)
            .IsEqualTo(ModerationQualificationWriteOutcome.Unavailable);
        await using var reader = Read(database);
        await Assert.That(await reader.ModerationQualificationPolicyAdoptions.CountAsync()).IsEqualTo(0);
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(3L);
    }

    [Test]
    public async Task Policy_source_cannot_be_adopted_for_another_actual_case()
    {
        await using var database = await CreateCase();
        var publish = Publish(database);
        await Execute(database, database.AdministratorId, publish);
        var originalCase = database.CaseId;
        var second = database.OpenCase() with { ExpectedQualificationRevision = 3 };
        await Execute(database, database.AdministratorId, second);
        await Assert.That((await Execute(database, database.AdministratorId, Adopt(database, publish.SourceId) with
        {
            ExpectedQualificationRevision = 4
        })).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Denied);
        await Assert.That((await Execute(database, database.AdministratorId, Adopt(database, publish.SourceId) with
        {
            CaseId = originalCase, ExpectedQualificationRevision = 4,
            Target = ISLAMU.Moderation.Domain.AuthorityScope.Tenant(database.Instance, Guid.CreateVersion7())
        })).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Denied);
        await using var reader = Read(database);
        await Assert.That(await reader.ModerationQualificationPolicyAdoptions.CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task Exact_adoption_retry_does_not_accept_changed_expiry_or_another_native_administrator()
    {
        await using var database = await CreateCase();
        var publish = Publish(database);
        await Execute(database, database.AdministratorId, publish);
        var adopt = Adopt(database, publish.SourceId);
        var first = await Execute(database, database.AdministratorId, adopt);
        var retry = await Execute(database, database.AdministratorId, adopt);
        await Assert.That(retry.Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        await Assert.That(retry.Receipt!.QualificationRevision).IsEqualTo(first.Receipt!.QualificationRevision);
        await Assert.That((await Execute(database, database.AdministratorId,
            adopt with { ValidUntil = Now.AddMinutes(30) })).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Conflict);
        await Assert.That((await Execute(database, database.UrgencyAssessorId, adopt)).Outcome)
            .IsEqualTo(ModerationQualificationWriteOutcome.Conflict);
        await using var reader = Read(database);
        await Assert.That(await reader.ModerationQualificationPolicyAdoptions.CountAsync()).IsEqualTo(1);
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(4L);
    }

    [Test]
    public async Task Expiry_reached_after_save_rolls_back_policy_epoch_and_receipt()
    {
        await using var database = await CreateCase();
        database.ExpireGovernanceAfterSave = true;
        await Assert.That((await Execute(database, database.AdministratorId,
            Publish(database) with { ValidUntil = Now.AddHours(1) })).Outcome)
            .IsEqualTo(ModerationQualificationWriteOutcome.Unavailable);
        await using var reader = Read(database);
        await Assert.That(await reader.ModerationQualificationPolicySources.CountAsync()).IsEqualTo(0);
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(2L);
        await Assert.That(await reader.ModerationQualificationMutationReceipts.CountAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task Adopted_native_limits_enable_reviewer_and_urgency_writes_without_deployment_owner()
    {
        await using var database = await CreateCase();
        var publish = Publish(database);
        await Assert.That((await Execute(database, database.AdministratorId, publish)).Outcome)
            .IsEqualTo(ModerationQualificationWriteOutcome.Known);
        await Assert.That((await Execute(database, database.AdministratorId, Adopt(database, publish.SourceId))).Outcome)
            .IsEqualTo(ModerationQualificationWriteOutcome.Known);
        var assignmentId = Guid.CreateVersion7();
        await Assert.That((await Execute(database, database.AdministratorId, new AssignModerationReviewerSource
        {
            OperationId = Guid.CreateVersion7(), Target = database.Scope, ExpectedQualificationRevision = 4,
            AssignmentId = assignmentId, CaseId = database.CaseId, ExpectedCaseRevision = 1,
            ReviewerHumanId = database.ReviewerId
        })).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        var qualify = new QualifyModerationReviewerController
        {
            OperationId = Guid.CreateVersion7(), Target = database.Scope, ExpectedQualificationRevision = 5,
            AssignmentId = assignmentId, ExpectedAssignmentRevision = 1, ControllerHumanId = database.UrgencyAssessorId,
            EvidenceId = Guid.CreateVersion7(), Justification = "Native independently assessed canonical controller.",
            QualifiedUntil = Now.AddMinutes(30).AddTicks(1)
        };
        await Assert.That((await Execute(database, database.AdministratorId, qualify)).Outcome)
            .IsEqualTo(ModerationQualificationWriteOutcome.Denied);
        await Assert.That((await Execute(database, database.AdministratorId, qualify with
        {
            OperationId = Guid.CreateVersion7(), EvidenceId = Guid.CreateVersion7(), QualifiedUntil = Now.AddMinutes(30)
        })).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        await Assert.That((await Execute(database, database.AdministratorId, Urgency(database, 6) with
        {
            ValidUntil = Now.AddMinutes(5).AddTicks(1)
        })).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Denied);
        await Assert.That((await Execute(database, database.AdministratorId, Urgency(database, 6))).Outcome)
            .IsEqualTo(ModerationQualificationWriteOutcome.Known);
        await using var reader = Read(database);
        await Assert.That((await reader.ModerationReviewerAssignments.SingleAsync()).QualifiedUntil)
            .IsEqualTo((DateTimeOffset?)Now.AddMinutes(30));
        await Assert.That((await reader.ModerationQualificationEvidence.SingleAsync(
            row => row.Purpose == ModerationQualificationEvidencePurpose.Urgency)).ValidUntil)
            .IsEqualTo((DateTimeOffset?)Now.AddMinutes(5));
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(7L);
        await Assert.That(await reader.ModerationDecisions.CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.ModerationSanctions.CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task Publication_without_adoption_cannot_supply_urgency_governance()
    {
        await using var database = await CreateCase();
        await Execute(database, database.AdministratorId, Publish(database));
        var observed = await Observe(database);
        await Assert.That(observed.Current).IsNotNull();
        await Assert.That(observed.Approval).IsNull();
        await Assert.That(observed.Joined).IsNull();
        await Assert.That((await Execute(database, database.AdministratorId, Urgency(database, 3))).Outcome)
            .IsEqualTo(ModerationQualificationWriteOutcome.Unavailable);
        await using var reader = Read(database);
        await Assert.That(await reader.ModerationQualificationEvidence.CountAsync()).IsEqualTo(0);
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(3L);
    }

    [Test]
    public async Task Adoption_expiry_is_half_open_through_actual_reader_and_native_writer()
    {
        await using var database = await CreateCase();
        var publish = Publish(database);
        await Execute(database, database.AdministratorId, publish);
        var adopt = Adopt(database, publish.SourceId);
        await Execute(database, database.AdministratorId, adopt);
        database.EvaluationTime = Now.AddHours(2).AddTicks(-1);
        await Assert.That((await Observe(database)).Joined).IsNotNull();
        database.EvaluationTime = Now.AddHours(2);
        var expired = await Observe(database);
        await Assert.That(expired.Current).IsNotNull();
        await Assert.That(expired.Approval).IsNull();
        await Assert.That(expired.Joined).IsNull();
        await Assert.That((await Execute(database, database.AdministratorId, Urgency(database, 4) with
        {
            ValidUntil = database.EvaluationTime.AddMinutes(5)
        })).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Unavailable);
        await Assert.That((await Execute(database, database.AdministratorId, new RevokeModerationQualificationPolicyAdoption
        {
            OperationId = Guid.CreateVersion7(), Target = database.Scope, ExpectedQualificationRevision = 4,
            SourceId = publish.SourceId, ExpectedSourceRevision = 1, AdoptionId = adopt.AdoptionId,
            ExpectedAdoptionRevision = 1, CaseId = database.CaseId, ExpectedCaseRevision = 1
        })).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        await using var reader = Read(database);
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(5L);
        await Assert.That(await reader.ModerationQualificationEvidence.CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task Source_expiry_is_half_open_through_actual_reader()
    {
        await using var database = await CreateCase();
        var publish = Publish(database) with { ValidUntil = Now.AddHours(1) };
        await Execute(database, database.AdministratorId, publish);
        await Execute(database, database.AdministratorId, Adopt(database, publish.SourceId) with
        {
            ValidUntil = Now.AddHours(1)
        });
        database.EvaluationTime = Now.AddHours(1).AddTicks(-1);
        await Assert.That((await Observe(database)).Joined).IsNotNull();
        database.EvaluationTime = Now.AddHours(1);
        var expired = await Observe(database);
        await Assert.That(expired.Current).IsNull();
        await Assert.That(expired.Approval).IsNull();
        await Assert.That(expired.Joined).IsNull();
        await Assert.That((await Execute(database, database.AdministratorId, publish with
        {
            OperationId = Guid.CreateVersion7(), ExpectedQualificationRevision = 4, ExpectedSourceRevision = 1,
            ValidUntil = database.EvaluationTime.AddHours(1)
        })).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        var replaced = await Observe(database);
        await Assert.That(replaced.Current!.SourceRevision).IsEqualTo(2L);
        await Assert.That(replaced.Approval).IsNull();
        await Assert.That(replaced.Joined).IsNull();
    }

    private static async Task<SourceDatabase> CreateCase()
    {
        var database = await SourceDatabase.CreateAsync();
        var result = await Execute(database, database.AdministratorId, database.OpenCase());
        if (result.Outcome != ModerationQualificationWriteOutcome.Known)
            throw new InvalidOperationException("Real native case authority did not install.");
        return database;
    }

    private static PublishModerationQualificationPolicySource Publish(SourceDatabase database) => new()
    {
        OperationId = Guid.CreateVersion7(), Target = database.Scope, ExpectedQualificationRevision = 2,
        SourceId = Guid.CreateVersion7(), ExpectedSourceRevision = 0,
        CaseId = database.CaseId, ExpectedCaseRevision = 1,
        MaximumReviewerLifetime = TimeSpan.FromMinutes(30), MaximumUrgencyLifetime = TimeSpan.FromMinutes(5),
        ValidUntil = Now.AddHours(3)
    };

    private static AdoptModerationQualificationPolicySource Adopt(SourceDatabase database, Guid sourceId) => new()
    {
        OperationId = Guid.CreateVersion7(), Target = database.Scope, ExpectedQualificationRevision = 3,
        SourceId = sourceId, ExpectedSourceRevision = 1, ExpectedWriterGeneration = 3,
        CaseId = database.CaseId, ExpectedCaseRevision = 1, AdoptionId = Guid.CreateVersion7(),
        ValidUntil = Now.AddHours(2)
    };

    private static QualifyModerationCaseUrgency Urgency(SourceDatabase database, long generation) => new()
    {
        OperationId = Guid.CreateVersion7(), Target = database.Scope, ExpectedQualificationRevision = generation,
        CaseId = database.CaseId, ExpectedCaseRevision = 1, EvidenceId = Guid.CreateVersion7(),
        Justification = "Current case-specific urgency independently assessed by a native administrator.",
        ValidUntil = Now.AddMinutes(5)
    };

    private static async Task<(
        ModerationQualificationPolicyCurrentSource? Current,
        ModerationQualificationPolicyApproval? Approval,
        ModerationQualificationPolicyObservation? Joined)> Observe(SourceDatabase database)
    {
        await using var context = Read(database);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var clock = new PolicyClock(database);
        var owner = new ModerationQualificationPolicySourceOwner(context, clock);
        var now = clock.GetUtcNow();
        var current = await owner.AcquireQualificationPolicyAsync(
            database.HostId, transaction.TransactionId, database.CaseId, database.Scope, now, CancellationToken.None);
        var approval = await owner.ReadQualificationPolicyApprovalAsync(
            database.HostId, transaction.TransactionId, database.CaseId, database.Scope, now, CancellationToken.None);
        var allegation = await context.ModerationCases.AsNoTracking().SingleAsync(row => row.Id == database.CaseId);
        var qualification = new ModerationNativeActivationQualification(context, database.Inventory, null, owner, clock);
        var joined = await qualification.AcquireQualificationPolicyAsync(
            database.HostId, allegation, now, CancellationToken.None);
        await transaction.CommitAsync();
        return (current, approval, joined);
    }

    private static ExploreDbContext Read(SourceDatabase database)
    {
        var context = database.Context();
        context.ModerationTrustDomainId = database.Scope.TrustDomainId;
        return context;
    }

    private static async Task<ModerationQualificationWriteResult> Execute(
        SourceDatabase database, Guid actor, ModerationQualificationMutation mutation)
    {
        await using var context = database.Context();
        var http = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, actor.ToString()), new Claim(ClaimTypes.Role, "platform.admin")],
                    "policy-witness"))
            }
        };
        var admin = new AdminContext(http, new PlatformUserRoleRepository(context),
            new TenantUserRoleGrantRepository(context), new OrganizationMemberRepository(context),
            new GroupMemberRepository(context), new UserExternalLoginRepository(context), NullLogger<AdminContext>.Instance);
        var machine = new MachinePrincipalAccessor(http);
        var clock = new PolicyClock(database);
        var publications = new ModerationPolicyPublicationRepository(
            context, admin, machine, database.Inventory, null, clock);
        var activation = new ModerationNativeActivationQualification(
            context, database.Inventory, null, new ModerationQualificationPolicySourceOwner(context, clock), clock);
        return await new ModerationQualificationRepository(context, admin, machine, publications, activation, clock, database.Privacy.Authority(context, clock))
            .ExecuteAsync(mutation);
    }

    private sealed class PolicyClock(SourceDatabase database) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => database.EvaluationTime;
    }
}
