using Explore.Application.Contracts.Infrastructure;
using Explore.Persistence;
using ISLAMU.Moderation.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using SourceDatabase = Event.Persistence.IntegrationTests.Moderation.ModerationQualificationWriterTests.SourceDatabase;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed class ModerationScheduleRiskReviewPersistenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Scalar_hydration_preserves_original_and_rebound_coordinates_without_extending_the_sanction(bool rebound)
    {
        await using var database = await ReviewDatabase.Create();
        var original = database.Review.OriginalCoordinates;
        if (rebound)
        {
            var current = ModerationScheduleRiskCoordinates.Create(original.EventScope, original.SessionId,
                Guid.NewGuid(), original.ParticipantId, original.Participant, Guid.NewGuid(),
                original.PolicyId, original.PolicyRevision + 1, Guid.CreateVersion7(),
                ModerationRuleReference.Create(original.Policy.OwnerId, original.Policy.Code, original.Policy.Revision + 1));
            database.Review.Rebind(current, Now.AddHours(4), 1, Now);
            await database.Context.SaveChangesAsync();
        }
        var expected = database.Review.Coordinates;
        await using var reader = database.Owner.Context();
        reader.ModerationTrustDomainId = database.Owner.Scope.TrustDomainId;
        var retained = await reader.Set<ModerationScheduleRiskReview>().AsNoTracking().SingleAsync();
        retained.ValidatePersistedState();
        await Assert.That(retained.OriginalCoordinates).IsEqualTo(original);
        await Assert.That(retained.Coordinates).IsEqualTo(expected);
        await Assert.That(retained.Owner).IsEqualTo(database.Review.Owner);
        await Assert.That(retained.EscalationOwner).IsEqualTo(database.Review.EscalationOwner);
        await Assert.That(retained.SourceScope).IsEqualTo(database.Sanction.Scope);
        await Assert.That(retained.SourceRule).IsEqualTo(database.Sanction.Rule);
        await Assert.That(retained.OriginalSanctionEndsAt).IsEqualTo(Now.AddHours(1));
        await Assert.That(retained.OriginalDueAt).IsEqualTo(Now.AddHours(3));
        var sanction = await reader.ModerationSanctions.AsNoTracking().SingleAsync();
        await Assert.That(sanction.EndsAt).IsEqualTo(Now.AddHours(1));
        await Assert.That(sanction.Revision).IsEqualTo(1L);
    }

    [Test]
    public async Task Stale_review_transition_cannot_replace_a_committed_withdrawal()
    {
        await using var database = await ReviewDatabase.Create();
        await using var staleContext = database.Owner.Context();
        staleContext.ModerationTrustDomainId = database.Owner.Scope.TrustDomainId;
        var stale = await staleContext.Set<ModerationScheduleRiskReview>().SingleAsync();
        database.Review.Withdraw(Guid.CreateVersion7(), database.Review.Owner, 1, Now);
        await database.Context.SaveChangesAsync();
        stale.Reconcile(1, stale.DueAt);
        await Assert.That(() => staleContext.SaveChangesAsync()).Throws<DbUpdateConcurrencyException>();
        await using var reader = database.Owner.Context();
        reader.ModerationTrustDomainId = database.Owner.Scope.TrustDomainId;
        var retained = await reader.Set<ModerationScheduleRiskReview>().AsNoTracking().SingleAsync();
        retained.ValidatePersistedState();
        await Assert.That(retained.State).IsEqualTo(ModerationScheduleRiskReviewState.Withdrawn);
        await Assert.That(retained.WithdrawnBy).IsEqualTo(database.Review.Owner);
        await Assert.That(retained.WithdrawalIntentId).IsEqualTo(database.Review.WithdrawalIntentId);
        await Assert.That(retained.Revision).IsEqualTo(2L);
    }

    [Test]
    public async Task Duplicate_original_work_cannot_create_a_second_review_identity()
    {
        await using var database = await ReviewDatabase.Create();
        var duplicate = ModerationScheduleRiskReview.Open(Guid.CreateVersion7(), database.Sanction, 1,
            database.Review.OriginalCoordinates, database.Review.Owner, database.Review.EscalationOwner,
            database.Review.OriginalDueAt, Now);
        database.Context.Add(duplicate);
        await Assert.That(() => database.Context.SaveChangesAsync()).Throws<DbUpdateException>();
        await using var reader = database.Owner.Context();
        reader.ModerationTrustDomainId = database.Owner.Scope.TrustDomainId;
        await Assert.That(await reader.Set<ModerationScheduleRiskReview>().CountAsync()).IsEqualTo(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Unbound_or_foreign_trust_context_cannot_read_review_work(bool foreign)
    {
        await using var database = await ReviewDatabase.Create();
        await using var reader = database.Owner.Context();
        reader.ModerationTrustDomainId = foreign ? Guid.CreateVersion7() : null;
        await Assert.That(await reader.Set<ModerationScheduleRiskReview>().AnyAsync()).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Foreign_tenant_with_current_trust_cannot_borrow_review_work_even_under_general_bypass(bool bypass)
    {
        await using var database = await ReviewDatabase.Create();
        await using var reader = database.Owner.Context();
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(Guid.CreateVersion7());
        reader.TenantContext = tenant;
        reader.ModerationTrustDomainId = database.Owner.Scope.TrustDomainId;
        if (bypass) reader.EnableTenantFilterBypass("Native row-isolation invariant.");
        await Assert.That(await reader.ModerationScheduleRiskReviews.AnyAsync()).IsFalse();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    [Arguments(6)]
    [Arguments(7)]
    public async Task Malformed_stored_ancestry_deadline_or_receipt_is_rejected_by_the_native_model(int corruption)
    {
        await using var database = await ReviewDatabase.Create();
        var assignment = corruption switch
        {
            0 => "state = 2",
            1 => "source_sanction_revision = 0",
            2 => "original_sanction_ends_at = original_sanction_starts_at",
            3 => "due_at = created_at",
            4 => "original_policy_revision = 0",
            5 => "event_scope_kind = 4",
            6 => "updated_at = due_at",
            _ => "policy_id = '00000000-0000-0000-0000-000000000000'"
        };
        await Assert.That(() => database.Context.Database.ExecuteSqlRawAsync(
            $"UPDATE moderation_schedule_risk_reviews SET {assignment} WHERE id = {{0}}", database.Review.Id))
            .Throws<SqliteException>();
        await using var reader = database.Owner.Context();
        reader.ModerationTrustDomainId = database.Owner.Scope.TrustDomainId;
        var retained = await reader.ModerationScheduleRiskReviews.AsNoTracking().SingleAsync();
        retained.ValidatePersistedState();
        await Assert.That(retained.Revision).IsEqualTo(1L);
        await Assert.That(retained.State).IsEqualTo(ModerationScheduleRiskReviewState.Pending);
    }

    [Test]
    public async Task Accepted_review_hydrates_exact_finding_and_independent_assignment_without_a_new_sanction()
    {
        await using var database = await ReviewDatabase.Create();
        var (finding, assignment) = await StoreRiskFindingAsync(database);
        var intentId = Guid.CreateVersion7();
        await Assert.That(database.Review.Accept(intentId, finding, assignment, assignment.Revision,
            database.Review.Coordinates, 1, Now)).IsTrue();
        await database.Context.SaveChangesAsync();
        await using var reader = database.Owner.Context();
        reader.ModerationTrustDomainId = database.Owner.Scope.TrustDomainId;
        var retained = await reader.ModerationScheduleRiskReviews.AsNoTracking().SingleAsync();
        retained.ValidatePersistedState();
        await Assert.That(retained.State).IsEqualTo(ModerationScheduleRiskReviewState.Accepted);
        await Assert.That(retained.AcceptedIntentId).IsEqualTo(intentId);
        await Assert.That(retained.AcceptedFindingId).IsEqualTo(finding.Id);
        await Assert.That(retained.AcceptedFindingRevision).IsEqualTo(finding.Revision);
        await Assert.That(retained.AcceptedReviewerAssignmentId).IsEqualTo(assignment.Id);
        await Assert.That(retained.AcceptedReviewerAssignmentRevision).IsEqualTo(assignment.Revision);
        await Assert.That(retained.Revision).IsEqualTo(2L);
        await Assert.That(retained.WithdrawalIntentId).IsNull();
        await Assert.That(retained.WithdrawnBy).IsNull();
        await Assert.That(retained.Accept(intentId, finding, assignment, assignment.Revision,
            retained.Coordinates, 2, Now)).IsFalse();
        await Assert.That(retained.Reconcile(2, retained.DueAt)).IsFalse();
        await Assert.That(await reader.ModerationSanctions.CountAsync()).IsEqualTo(1);
        await Assert.That((await reader.ModerationSanctions.SingleAsync()).EndsAt).IsEqualTo(Now.AddHours(1));
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Accepted_receipt_cannot_borrow_a_missing_finding_or_assignment(bool reviewer)
    {
        await using var database = await ReviewDatabase.Create();
        var (finding, assignment) = await StoreRiskFindingAsync(database);
        database.Review.Accept(Guid.CreateVersion7(), finding, assignment, assignment.Revision,
            database.Review.Coordinates, 1, Now);
        await database.Context.SaveChangesAsync();
        var column = reviewer ? "accepted_reviewer_assignment_id" : "accepted_finding_id";
        await Assert.That(() => database.Context.Database.ExecuteSqlRawAsync(
            $"UPDATE moderation_schedule_risk_reviews SET {column} = {{0}} WHERE id = {{1}}",
            Guid.CreateVersion7(), database.Review.Id)).Throws<SqliteException>();
        await using var reader = database.Owner.Context();
        reader.ModerationTrustDomainId = database.Owner.Scope.TrustDomainId;
        var retained = await reader.ModerationScheduleRiskReviews.AsNoTracking().SingleAsync();
        await Assert.That(retained.AcceptedFindingId).IsEqualTo(finding.Id);
        await Assert.That(retained.AcceptedReviewerAssignmentId).IsEqualTo(assignment.Id);
    }

    [Test]
    public async Task Later_assignment_withdrawal_retains_history_but_cannot_replay_current_review_authority()
    {
        await using var database = await ReviewDatabase.Create();
        var (finding, assignment) = await StoreRiskFindingAsync(database);
        var intent = Guid.CreateVersion7();
        database.Review.Accept(intent, finding, assignment, assignment.Revision, database.Review.Coordinates, 1, Now);
        await database.Context.SaveChangesAsync();
        var originalAssignmentRevision = assignment.Revision;
        assignment.Withdraw(assignment.Revision, database.Owner.AdministratorId, Now.AddMinutes(1));
        await database.Context.SaveChangesAsync();
        await using var reader = database.Owner.Context();
        reader.ModerationTrustDomainId = database.Owner.Scope.TrustDomainId;
        var retained = await reader.ModerationScheduleRiskReviews.AsNoTracking().SingleAsync();
        retained.ValidatePersistedState();
        var current = await reader.ModerationReviewerAssignments.AsNoTracking().SingleAsync(row => row.Id == assignment.Id);
        await Assert.That(retained.AcceptedReviewerAssignmentRevision).IsEqualTo(originalAssignmentRevision);
        await Assert.That(current.Revision).IsEqualTo(originalAssignmentRevision + 1);
        await Assert.That(current.IsQualifiedAt(Now.AddMinutes(1))).IsFalse();
        await Assert.That(() => retained.Accept(intent, finding, current, current.Revision,
            retained.Coordinates, retained.Revision, Now.AddMinutes(1))).Throws<InvalidOperationException>();
        await Assert.That(retained.Revision).IsEqualTo(2L);
        await Assert.That(retained.State).IsEqualTo(ModerationScheduleRiskReviewState.Accepted);
    }

    private static async Task<(ModerationFinding Finding, ModerationReviewerAssignment Assignment)> StoreRiskFindingAsync(
        ReviewDatabase database)
    {
        var owner = database.Owner;
        var adjudicator = SubjectReference.Create(ModerationSubjectKind.Human, owner.AdministratorId, owner.Instance);
        var caseRow = ModerationCase.Open(Guid.CreateVersion7(), database.Review.Subject, owner.Scope, Guid.CreateVersion7(), Now);
        var rule = database.Review.Coordinates.Policy;
        var finding = ModerationFinding.Finalize(Guid.CreateVersion7(), caseRow, 1, owner.Scope, null, null,
            rule, rule, rule, rule, adjudicator,
            ModerationFindingEvidenceReference.Response(Guid.CreateVersion7(), caseRow.Id, 1, Now),
            "Structural acceptance fixture; current native risk permission remains separate.", Now);
        var assignment = ModerationReviewerAssignment.Assign(Guid.CreateVersion7(), caseRow, 1,
            adjudicator.Id, owner.ReviewerId, Now);
        assignment.QualifyController(1, adjudicator.Id, Guid.CreateVersion7(), owner.ReviewerId, Now.AddMinutes(30), Now);
        assignment.RecordConflictAssessment(2, ModerationReviewerConflictState.Independent,
            Guid.CreateVersion7(), owner.ReviewerId, Now);
        database.Context.AddRange(caseRow, finding, assignment);
        await database.Context.SaveChangesAsync();
        return (finding, assignment);
    }

    private sealed class ReviewDatabase(SourceDatabase owner, ExploreDbContext context,
        ModerationScheduleRiskReview review, ModerationSanction sanction) : IAsyncDisposable
    {
        public SourceDatabase Owner { get; } = owner;
        public ExploreDbContext Context { get; } = context;
        public ModerationScheduleRiskReview Review { get; } = review;
        public ModerationSanction Sanction { get; } = sanction;

        public static async Task<ReviewDatabase> Create()
        {
            var owner = await SourceDatabase.CreateAsync();
            var context = owner.Context();
            context.ModerationTrustDomainId = owner.Scope.TrustDomainId;
            var subject = SubjectReference.Create(ModerationSubjectKind.Human, owner.SubjectId, owner.Instance);
            var issuer = SubjectReference.Create(ModerationSubjectKind.Human, owner.AdministratorId, owner.Instance);
            var reviewer = SubjectReference.Create(ModerationSubjectKind.Human, owner.ReviewerId, owner.Instance);
            var rule = ModerationRuleReference.Create(owner.Scope.Id, "event-risk", 1);
            var moderationCase = ModerationCase.Open(Guid.CreateVersion7(), subject, owner.Scope, Guid.CreateVersion7(), Now);
            var decision = ModerationDecision.Propose(Guid.CreateVersion7(), moderationCase.Id, subject, issuer,
                owner.Instance, owner.Scope, rule, [ModerationCapability.Publish], Now, Now.AddHours(1), null, 1, Now);
            var finding = ModerationFinding.Finalize(Guid.CreateVersion7(), moderationCase, 1, owner.Scope, null, null,
                rule, rule, rule, rule, reviewer,
                ModerationFindingEvidenceReference.Response(Guid.CreateVersion7(), moderationCase.Id, 1, Now),
                "Structural persistence fixture; no native risk admission asserted.", Now);
            decision.RecordResponse(1, Now);
            decision.AcceptFinding(finding.Id, 2, Now);
            decision.Approve(reviewer, owner.Scope, 3, Now);
            var sanction = decision.Activate(Guid.CreateVersion7(), 4, Now);
            var coordinates = ModerationScheduleRiskCoordinates.Create(
                AuthorityScope.Resource(owner.Scope, Guid.CreateVersion7()), Guid.CreateVersion7(), Guid.NewGuid(),
                Guid.CreateVersion7(), subject, Guid.NewGuid(), Guid.CreateVersion7(), 1, Guid.CreateVersion7(), rule);
            var review = ModerationScheduleRiskReview.Open(Guid.CreateVersion7(), sanction, 1, coordinates,
                reviewer, issuer, Now.AddHours(3), Now);
            context.AddRange(moderationCase, finding, decision, sanction, review);
            await context.SaveChangesAsync();
            return new(owner, context, review, sanction);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await Owner.DisposeAsync();
        }
    }
}
