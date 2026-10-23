using System.Collections.Immutable;
using System.Security.Cryptography;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;
using Microsoft.EntityFrameworkCore;
using DomainEvent = Explore.Domain.Event;
using SourceDatabase = Event.Persistence.IntegrationTests.Moderation.ModerationQualificationWriterTests.SourceDatabase;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed class ModerationContentNoticeCustodyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    [Test]
    [Arguments(RoleEnum.EventOwner)]
    [Arguments(RoleEnum.EventManager)]
    public async Task Original_Event_representative_is_approved_and_retained_without_a_human_finding(RoleEnum role)
    {
        var (database, intent) = await CreateAsync(role);
        await using var owner = database;
        var source = Approve(database, intent);
        var accepted = await database.ExecuteAsync(database.AdministratorId, source);
        var replay = await database.ExecuteAsync(database.AdministratorId, source);
        await Assert.That(accepted.IsSuccess).IsTrue();
        await Assert.That(replay.IsSuccess).IsTrue();
        await Assert.That(replay.Id).IsEqualTo(accepted.Id);
        await Assert.That(replay.QualificationRevision).IsEqualTo(4L);
        await using var reader = Read(database);
        var approval = await reader.ModerationNoticeDisclosureApprovals.SingleAsync();
        await Assert.That(approval.IsCurrentAt(intent, Now)).IsTrue();
        await Assert.That(approval.Intent.ContentRepresentatives.Single())
            .IsEqualTo(intent.ContentRepresentatives.Single());
        await Assert.That(approval.ApprovedByHumanId).IsEqualTo(database.AdministratorId);
        await Assert.That(approval.RetainUntil).IsEqualTo(intent.RetainUntil);
        var moderationCase = await reader.ModerationCases.SingleAsync();
        var decision = await reader.ModerationDecisions.SingleAsync();
        var caseSubject = moderationCase.Subject
            ?? throw new InvalidOperationException("The native Content case must retain its subject.");
        await Assert.That(caseSubject.Kind).IsEqualTo(ModerationSubjectKind.Content);
        await Assert.That(decision.Subject).IsEqualTo(caseSubject);
        await Assert.That(decision.Subject.Origin).IsEqualTo(database.Scope);
        await Assert.That(decision.Revision).IsEqualTo(1L);
        await Assert.That(await reader.ModerationFindings.CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.ModerationEffectIntents.CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.ModerationNotices.CountAsync()).IsEqualTo(0);
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(4L);
        await Assert.That(await reader.ModerationQualificationMutationReceipts
            .CountAsync(row => row.Id == source.OperationId)).IsEqualTo(1);
    }

    [Test]
    [Arguments("revoked")]
    [Arguments("expired")]
    [Arguments("elapsed-window")]
    [Arguments("pending")]
    [Arguments("future")]
    [Arguments("stale-version")]
    public async Task Noncurrent_original_assignment_cannot_approve_Content_custody(string change)
    {
        var (database, intent) = await CreateAsync();
        await using var owner = database;
        var proof = intent.ContentRepresentatives.Single();
        await using (var writer = database.Context())
        {
            var assignment = await writer.EventRoleAssignments.SingleAsync(row => row.Id == proof.AssignmentId);
            switch (change)
            {
                case "revoked":
                    assignment.Revoke(database.AdministratorId, Now.UtcDateTime);
                    break;
                case "expired":
                    assignment.UpdateValidityWindow(Now.AddHours(-1).UtcDateTime, Now.UtcDateTime, Now.UtcDateTime);
                    assignment.MarkExpired(Now.UtcDateTime);
                    break;
                case "elapsed-window":
                    database.EvaluationTime = Now.AddMinutes(5);
                    break;
                case "pending":
                    assignment.Status = EventRoleAssignmentStatus.Pending;
                    break;
                case "future":
                    assignment.UpdateValidityWindow(Now.AddMinutes(1).UtcDateTime,
                        Now.AddHours(1).UtcDateTime, Now.UtcDateTime);
                    break;
                case "stale-version":
                    assignment.UpdateValidityWindow(assignment.StartsAtUtc, Now.AddHours(2).UtcDateTime, Now.UtcDateTime);
                    break;
            }
            await writer.SaveChangesAsync();
            // Isolate current-status/window rejection from stale-version rejection.
            if (change != "stale-version")
                intent = intent with { ContentRepresentatives = [proof with { AssignmentRevision = assignment.Version }] };
        }
        await AssertRejectedAsync(database, intent);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Persisted_foreign_Event_or_tenant_assignment_cannot_represent_the_original_Content(bool foreignTenant)
    {
        var (database, intent) = await CreateAsync();
        await using var owner = database;
        await using var writer = database.Context();
        var tenant = await writer.Tenants.SingleAsync(row => row.Id == database.TenantId);
        if (foreignTenant)
        {
            var status = await writer.TenantStatuses.SingleAsync(row => row.IsActiveState);
            tenant = new Tenant
            {
                Id = Guid.CreateVersion7(), FullName = "Foreign custody tenant",
                Slug = $"custody-{Guid.CreateVersion7():N}", TenantStatusId = status.Id, TenantStatus = status
            };
            writer.Tenants.Add(tenant);
            writer.TenantContext = new FixtureTenant(tenant.Id);
            writer.TenantUsers.Add(new TenantUser
            {
                Id = Guid.CreateVersion7(), TenantId = tenant.Id, Tenant = tenant,
                UserId = database.SubjectId, User = null!, StatusId = (int)TenantUserStatusEnum.Active
            });
        }
        var actor = await writer.Actors.SingleAsync(row => row.UserId == database.SubjectId);
        var otherEvent = Event(database, tenant, actor);
        var otherAssignment = EventRoleAssignment.Create(tenant.Id, otherEvent.Id, database.SubjectId,
            (int)RoleEnum.EventOwner, EventRoleAssignmentStatus.Active,
            Now.AddHours(-1).UtcDateTime, Now.AddHours(1).UtcDateTime, database.AdministratorId);
        writer.AddRange(otherEvent, otherAssignment);
        await writer.SaveChangesAsync();
        intent = intent with
        {
            ContentRepresentatives = [new(otherAssignment.Id, otherAssignment.Version, database.SubjectId)]
        };
        await AssertRejectedAsync(database, intent);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Unrelated_native_administrator_or_reviewer_cannot_borrow_the_Event_representatives_assignment(bool reviewer)
    {
        var (database, intent) = await CreateAsync();
        await using var owner = database;
        if (reviewer)
            await Assert.That((await database.ExecuteAsync(database.AdministratorId, new AssignModerationReviewerSource
            {
                OperationId = Guid.CreateVersion7(), Target = database.Scope, ExpectedQualificationRevision = 3,
                AssignmentId = Guid.CreateVersion7(), CaseId = intent.CaseId, ExpectedCaseRevision = 1,
                ReviewerHumanId = database.ReviewerId
            })).IsSuccess).IsTrue();
        var proof = intent.ContentRepresentatives.Single();
        intent = intent with
        {
            ContentRepresentatives = [proof with { HumanId = reviewer ? database.ReviewerId : database.AdministratorId }]
        };
        await AssertRejectedAsync(database, intent, reviewer ? 4 : 3);
    }

    [Test]
    public async Task Human_source_rejects_an_otherwise_valid_Content_representative_projection()
    {
        var (database, intent) = await CreateAsync(humanSubject: true);
        await using var owner = database;
        await AssertRejectedAsync(database, intent);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Content_source_rejects_default_or_empty_original_custody(bool defaultArray)
    {
        var (database, intent) = await CreateAsync();
        await using var owner = database;
        intent = intent with
        {
            ContentRepresentatives = defaultArray ? default : ImmutableArray<ModerationNoticeContentRepresentative>.Empty
        };
        await AssertRejectedAsync(database, intent);
    }

    [Test]
    [Arguments("erasure")]
    [Arguments("deleted-human")]
    [Arguments("deleted-membership")]
    [Arguments("suspended-membership")]
    public async Task Original_representative_requires_live_human_membership_and_no_primary_erasure(string change)
    {
        var (database, intent) = await CreateAsync();
        await using var owner = database;
        await using (var writer = database.Context())
        {
            if (change == "erasure")
            {
                var erasure = PrivacyErasureIntent.Record(Guid.CreateVersion7(), 1, PrivacyErasureSubjectKind.User,
                    database.SubjectId, PrivacyErasureReasonCode.SubjectErasureRequest, 1, Now.UtcDateTime, Now.UtcDateTime);
                writer.PrivacyErasureSagas.Add(PrivacyErasureSaga.Start(erasure, 1,
                    RandomNumberGenerator.GetBytes(32), Now.AddHours(1).UtcDateTime, Now.UtcDateTime));
            }
            else if (change == "deleted-human")
                (await writer.Users.SingleAsync(row => row.Id == database.SubjectId)).IsDeleted = true;
            else
            {
                var membership = await writer.TenantUsers.SingleAsync(row => row.UserId == database.SubjectId);
                if (change == "deleted-membership") writer.TenantUsers.Remove(membership);
                else membership.StatusId = (int)TenantUserStatusEnum.Suspended;
            }
            await writer.SaveChangesAsync();
        }
        await AssertRejectedAsync(database, intent);
    }

    [Test]
    [Arguments(RoleEnum.EventOwner)]
    [Arguments(RoleEnum.EventManager)]
    public async Task Retained_representative_erasure_rejects_native_Content_approval_before_primary_saga(RoleEnum role)
    {
        var (database, intent) = await CreateAsync(role);
        await using var owner = database;
        await using (var context = database.Context())
        {
            var fact = await database.Privacy.Authority(context, new CustodyClock()).AppendAsync(
                PrivacyErasureRequest.Create(Guid.CreateVersion7(), PrivacyErasureSubjectKind.User,
                    database.SubjectId, PrivacyErasureReasonCode.AccountDeletion, 1));
            await Assert.That(fact.SubjectId).IsEqualTo(intent.ContentRepresentatives.Single().HumanId);
            await Assert.That(fact.AuthoritySequence).IsEqualTo(1L);
            await Assert.That(await context.PrivacyErasureSagas.AnyAsync()).IsFalse();
        }
        await AssertRejectedAsync(database, intent);
        await using var reader = Read(database);
        await Assert.That(await reader.PrivacyErasureSagas.AnyAsync()).IsFalse();
        var assignment = await reader.EventRoleAssignments.SingleAsync();
        await Assert.That(assignment.Status).IsEqualTo(EventRoleAssignmentStatus.Active);
        await Assert.That(assignment.Version).IsEqualTo(intent.ContentRepresentatives.Single().AssignmentRevision);
    }

    [Test]
    public async Task Retained_representative_erasure_rejects_approved_Content_replay_without_rewriting_custody()
    {
        var (database, intent) = await CreateAsync();
        await using var owner = database;
        var source = Approve(database, intent);
        var accepted = await database.ExecuteAsync(database.AdministratorId, source);
        await Assert.That(accepted.IsSuccess).IsTrue();
        await using (var context = database.Context())
        {
            await database.Privacy.Authority(context, new CustodyClock()).AppendAsync(
                PrivacyErasureRequest.Create(Guid.CreateVersion7(), PrivacyErasureSubjectKind.User,
                    database.SubjectId, PrivacyErasureReasonCode.AccountDeletion, 1));
            await Assert.That(await context.PrivacyErasureSagas.AnyAsync()).IsFalse();
        }
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, source)).IsSuccess).IsFalse();
        await using var reader = Read(database);
        var retained = await reader.ModerationNoticeDisclosureApprovals.SingleAsync();
        await Assert.That(retained.Id).IsEqualTo(accepted.Id);
        await Assert.That(retained.Revision).IsEqualTo(1L);
        await Assert.That(retained.Intent.ContentRepresentatives.Single()).IsEqualTo(intent.ContentRepresentatives.Single());
        await Assert.That(retained.RetainUntil).IsEqualTo(intent.RetainUntil);
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(4L);
        await Assert.That(await reader.ModerationQualificationMutationReceipts.CountAsync(row => row.Id == source.OperationId))
            .IsEqualTo(1);
        await Assert.That((await reader.ModerationDecisions.SingleAsync()).Revision).IsEqualTo(1L);
        await Assert.That(await reader.ModerationFindings.CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.ModerationEffectIntents.CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.ModerationNotices.CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.PrivacyErasureSagas.AnyAsync()).IsFalse();
    }

    [Test]
    public async Task Replacement_Event_manager_cannot_substitute_for_the_revoked_original_assignment()
    {
        var (database, intent) = await CreateAsync();
        await using var owner = database;
        var proof = intent.ContentRepresentatives.Single();
        await using (var writer = database.Context())
        {
            var original = await writer.EventRoleAssignments.SingleAsync(row => row.Id == proof.AssignmentId);
            original.Revoke(database.AdministratorId, Now.UtcDateTime);
            writer.EventRoleAssignments.Add(EventRoleAssignment.Create(database.TenantId, original.EventId,
                original.UserId, (int)RoleEnum.EventManager, EventRoleAssignmentStatus.Active,
                Now.AddHours(-1).UtcDateTime, Now.AddHours(1).UtcDateTime, database.AdministratorId));
            await writer.SaveChangesAsync();
            intent = intent with { ContentRepresentatives = [proof with { AssignmentRevision = original.Version }] };
        }
        await AssertRejectedAsync(database, intent);
    }

    [Test]
    public async Task Previously_approved_custody_replay_rejects_revoked_original_role_without_rewriting_retained_proof()
    {
        var (database, intent) = await CreateAsync();
        await using var owner = database;
        var source = Approve(database, intent);
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, source)).IsSuccess).IsTrue();
        await using (var writer = database.Context())
        {
            (await writer.EventRoleAssignments.SingleAsync()).Revoke(database.AdministratorId, Now.UtcDateTime);
            await writer.SaveChangesAsync();
        }
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, source)).IsSuccess).IsFalse();
        await using var reader = Read(database);
        var retained = await reader.ModerationNoticeDisclosureApprovals.SingleAsync();
        await Assert.That(retained.Intent.ContentRepresentatives.Single()).IsEqualTo(intent.ContentRepresentatives.Single());
        await Assert.That(retained.Revision).IsEqualTo(1L);
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(4L);
        await Assert.That(await reader.ModerationQualificationMutationReceipts
            .CountAsync(row => row.Id == source.OperationId)).IsEqualTo(1);
    }

    [Test]
    public async Task Original_assignment_expiry_at_final_flush_rolls_back_approval_epoch_and_receipt()
    {
        var (database, intent) = await CreateAsync();
        await using var owner = database;
        var flushed = false;
        database.QualificationSaved = () =>
        {
            flushed = true;
            database.EvaluationTime = Now.AddMinutes(5);
        };
        await AssertRejectedAsync(database, intent);
        await Assert.That(flushed).IsTrue();
        // The disclosure's ten-minute lease and governance's one-hour lease remain
        // live; only the explicitly selected original Event assignment has expired.
        await Assert.That(database.EvaluationTime < intent.ValidUntil).IsTrue();
    }

    private static async Task AssertRejectedAsync(
        SourceDatabase database, ModerationNoticeDisclosureIntent intent, long generation = 3)
    {
        var source = Approve(database, intent) with { ExpectedQualificationRevision = generation };
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, source)).IsSuccess).IsFalse();
        await using var reader = Read(database);
        await Assert.That(await reader.ModerationNoticeDisclosureApprovals.CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.ModerationQualificationMutationReceipts
            .AnyAsync(row => row.Id == source.OperationId)).IsFalse();
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(generation);
        await Assert.That((await reader.ModerationDecisions.SingleAsync()).Revision).IsEqualTo(1L);
        await Assert.That(await reader.ModerationFindings.CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.ModerationEffectIntents.CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.ModerationNotices.CountAsync()).IsEqualTo(0);
    }

    private static async Task<(SourceDatabase Database, ModerationNoticeDisclosureIntent Intent)> CreateAsync(
        RoleEnum role = RoleEnum.EventOwner, bool humanSubject = false)
    {
        var database = await SourceDatabase.CreateAsync();
        DomainEvent owningEvent;
        EventRoleAssignment assignment;
        await using (var writer = database.Context())
        {
            var tenant = await writer.Tenants.SingleAsync(row => row.Id == database.TenantId);
            owningEvent = Event(database, tenant);
            assignment = EventRoleAssignment.Create(tenant.Id, owningEvent.Id, database.SubjectId,
                (int)role, EventRoleAssignmentStatus.Active,
                Now.AddHours(-1).UtcDateTime, Now.AddMinutes(5).UtcDateTime, database.AdministratorId);
            writer.AddRange(owningEvent.Actor, owningEvent, assignment);
            await writer.SaveChangesAsync();
        }
        var opened = database.OpenCase() with
        {
            SubjectKind = humanSubject ? ModerationSubjectKind.Human : ModerationSubjectKind.Content,
            SubjectId = humanSubject ? database.SubjectId : owningEvent.Id
        };
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, opened)).IsSuccess).IsTrue();
        var request = database.Publication(2,
            humanSubject ? ModerationReferenceProfileId.CFG06 : ModerationReferenceProfileId.CFG09);
        request = request with
        {
            Proposed = request.Proposed with
            {
                Profiles = [request.Proposed.Profiles.Single() with { SubjectKinds = [opened.SubjectKind] }]
            }
        };
        var publication = await database.PublishAsync(database.AdministratorId, request);
        await Assert.That(publication.Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        var profile = request.Proposed.Profiles.Single();
        var subject = SubjectReference.Create(opened.SubjectKind, opened.SubjectId,
            humanSubject ? database.Instance : database.Scope);
        // Same accepted-decision boundary as ModerationNoticeDisclosureWriterTests:
        // only the decision is seeded; case, publication and disclosure use native writers.
        var decision = ModerationDecision.Propose(Guid.CreateVersion7(), opened.CaseId, subject,
            SubjectReference.Create(ModerationSubjectKind.Human, database.AdministratorId, database.Instance),
            database.Scope, database.Scope, profile.Rule, [ModerationCapability.Publish],
            Now, Now.AddHours(1), null, 1, Now);
        await using (var writer = Read(database))
        {
            writer.ModerationDecisions.Add(decision);
            await writer.SaveChangesAsync();
        }
        return (database, new ModerationNoticeDisclosureIntent(
            Guid.CreateVersion7(), Guid.CreateVersion7(), decision.Id, 2, opened.CaseId, 1,
            request.Proposed.PolicyId, 1, publication.Publication!.Id, profile.Id, Guid.CreateVersion7(), new string('a', 64),
            new(request.Proposed.Classes.Single().Meaning), new(request.Proposed.Reasons.Single().Meaning),
            [new("Approved original Event explanation.")], new("Protected response remains available."),
            new("Request independent review."), new("No automated finding."), new("Purpose-bound retention."),
            Now.AddMinutes(10), Now.AddYears(1))
        {
            ContentRepresentatives = [new(assignment.Id, assignment.Version, database.SubjectId)]
        });
    }

    private static DomainEvent Event(SourceDatabase database, Tenant tenant, Actor? actor = null)
    {
        var actorId = Guid.CreateVersion7();
        actor ??= new Actor
        {
            Id = actorId, ActorTypeId = (int)ActorTypeEnum.User, ActorType = null!,
            UserId = database.SubjectId, Pii = new ActorPii { ActorId = actorId, DisplayName = "Original Event representative" }
        };
        return new(EventStatusEnum.Draft)
        {
            Id = Guid.CreateVersion7(), TenantId = tenant.Id, Tenant = tenant, ActorId = actor.Id, Actor = actor,
            Title = "Original Content notice custody", SubmittedByUserId = database.SubjectId,
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            VisibilityTypeId = (int)VisibilityTypeEnum.Private, VisibilityType = null!,
            EventFormatId = (int)EventFormatEnum.Local, EventFormat = null!, EventStatus = null!
        };
    }

    private static ApproveModerationNoticeDisclosureSource Approve(
        SourceDatabase database, ModerationNoticeDisclosureIntent intent) => new()
    {
        OperationId = Guid.CreateVersion7(), Target = database.Scope, ExpectedQualificationRevision = 3, Intent = intent
    };

    private static ExploreDbContext Read(SourceDatabase database)
    {
        var context = database.Context();
        context.ModerationTrustDomainId = database.Scope.TrustDomainId;
        return context;
    }

    private sealed record FixtureTenant(Guid TenantId) : ITenantContext;

    private sealed class CustodyClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
