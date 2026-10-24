using Explore.Application.Contracts.Persistence;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationResourceQuarantineExecutionTests
{
    [Test]
    public async Task Exact_review_deadline_is_materialized_once_without_extending_the_original_sanction()
    {
        await using var fixture = await Fixture.CreateAsync();
        var reviewId = await StoreScheduleReviewAsync(fixture);
        await using var privacy = await DutyPrivacyDatabase.CreateAsync();
        fixture.Clock.UtcNow = Now.AddHours(3).AddTicks(-1);
        var early = await ScheduleRiskPassAsync(fixture, privacy);
        await Assert.That(early.Available).IsTrue();
        await Assert.That(early.Results.Single().Outcome).IsEqualTo(ModerationScheduleRiskReconciliationOutcome.AwaitingDeadline);
        fixture.Clock.UtcNow = Now.AddHours(3);
        var overdue = await ScheduleRiskPassAsync(fixture, privacy);
        await Assert.That(overdue.Results.Single().Outcome).IsEqualTo(ModerationScheduleRiskReconciliationOutcome.Overdue);
        await Assert.That(overdue.Results.Single().Review!.Id).IsEqualTo(reviewId);
        var first = await ReadScheduleReviewAsync(fixture, reviewId);
        await Assert.That(first.State).IsEqualTo(ModerationScheduleRiskReviewState.Overdue);
        await Assert.That(first.UpdatedAt).IsEqualTo(Now.AddHours(3));
        await Assert.That(first.Revision).IsEqualTo(2L);
        await Assert.That(first.OriginalSanctionEndsAt).IsEqualTo(Now.AddHours(1));
        fixture.Clock.UtcNow = Now.AddDays(1);
        var replay = await ScheduleRiskPassAsync(fixture, privacy);
        await Assert.That(replay.Results.Length).IsEqualTo(0);
        var retained = await ReadScheduleReviewAsync(fixture, reviewId);
        await Assert.That(retained.UpdatedAt).IsEqualTo(first.UpdatedAt);
        await Assert.That(retained.Revision).IsEqualTo(first.Revision);
        await using var reader = fixture.Context();
        reader.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        var source = await reader.ModerationSanctions.SingleAsync(row => row.Id == first.SourceSanctionId);
        await Assert.That(source.EndsAt).IsEqualTo(Now.AddHours(1));
        await Assert.That(source.Revision).IsEqualTo(1L);
        await Assert.That(await reader.ModerationEffectIntents.AnyAsync(row => row.SanctionId == source.Id)).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Foreign_tenant_or_revoked_maintenance_cannot_reconcile_owned_review(bool revoked)
    {
        await using var fixture = await Fixture.CreateAsync();
        var reviewId = await StoreScheduleReviewAsync(fixture);
        await using var privacy = await DutyPrivacyDatabase.CreateAsync();
        fixture.Clock.UtcNow = Now.AddHours(3);
        var result = await ScheduleRiskPassAsync(fixture, privacy,
            tenantId: revoked ? null : fixture.OtherTenantId, expireKeyAfterAuthentication: revoked);
        if (revoked) await Assert.That(result.Available).IsFalse();
        else await Assert.That(result.Results.Length).IsEqualTo(0);
        var retained = await ReadScheduleReviewAsync(fixture, reviewId);
        await Assert.That(retained.State).IsEqualTo(ModerationScheduleRiskReviewState.Pending);
        await Assert.That(retained.Revision).IsEqualTo(1L);
    }

    [Test]
    public async Task Maintenance_purpose_loss_after_flush_rolls_back_the_overdue_transition()
    {
        await using var fixture = await Fixture.CreateAsync();
        var reviewId = await StoreScheduleReviewAsync(fixture);
        await using var privacy = await DutyPrivacyDatabase.CreateAsync();
        fixture.Clock.UtcNow = Now.AddHours(3);
        var barrier = new ExpireDutyAfterFlush(fixture.Clock);
        var result = await ScheduleRiskPassAsync(fixture, privacy, interceptors: [barrier]);
        await Assert.That(barrier.Observed).IsTrue();
        await Assert.That(result.Results.Single().Outcome).IsEqualTo(ModerationScheduleRiskReconciliationOutcome.Unavailable);
        var retained = await ReadScheduleReviewAsync(fixture, reviewId);
        await Assert.That(retained.State).IsEqualTo(ModerationScheduleRiskReviewState.Pending);
        await Assert.That(retained.Revision).IsEqualTo(1L);
    }

    [Test]
    public async Task Full_review_page_returns_its_cursor_and_empty_page_wraps()
    {
        await using var fixture = await Fixture.CreateAsync();
        var reviewId = await StoreScheduleReviewAsync(fixture);
        await using var privacy = await DutyPrivacyDatabase.CreateAsync();
        var page = await ScheduleRiskPassAsync(fixture, privacy);
        await Assert.That(page.LastScannedReviewId).IsEqualTo(reviewId);
        var next = await ScheduleRiskPassAsync(fixture, privacy, after: page.LastScannedReviewId);
        await Assert.That(next.Results.Length).IsEqualTo(0);
        await Assert.That(next.LastScannedReviewId).IsNull();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task Valid_shaped_foreign_owner_interval_or_deadline_after_flush_cannot_replace_frozen_work(int mutation)
    {
        await using var fixture = await Fixture.CreateAsync();
        var reviewId = await StoreScheduleReviewAsync(fixture);
        var original = await ReadScheduleReviewAsync(fixture, reviewId);
        await using var privacy = await DutyPrivacyDatabase.CreateAsync();
        fixture.Clock.UtcNow = Now.AddHours(3);
        var barrier = new ReplaceScheduleWorkAfterFlush(reviewId, fixture.Database.AdministratorId, mutation);
        var result = await ScheduleRiskPassAsync(fixture, privacy, interceptors: [barrier]);
        await Assert.That(barrier.Observed).IsTrue();
        await Assert.That(result.Results.Single().Outcome).IsEqualTo(ModerationScheduleRiskReconciliationOutcome.Unavailable);
        var retained = await ReadScheduleReviewAsync(fixture, reviewId);
        await Assert.That(retained.Owner).IsEqualTo(original.Owner);
        await Assert.That(retained.OriginalSanctionEndsAt).IsEqualTo(original.OriginalSanctionEndsAt);
        await Assert.That(retained.DueAt).IsEqualTo(original.DueAt);
        await Assert.That(retained.Revision).IsEqualTo(1L);
        await Assert.That(retained.State).IsEqualTo(ModerationScheduleRiskReviewState.Pending);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task Bypassed_bound_trust_or_borrowed_transaction_is_not_a_native_deadline_owner(int borrowed)
    {
        await using var fixture = await Fixture.CreateAsync();
        var reviewId = await StoreScheduleReviewAsync(fixture);
        await using var privacy = await DutyPrivacyDatabase.CreateAsync();
        fixture.Clock.UtcNow = Now.AddHours(3);
        await using var context = fixture.Context();
        var identity = await fixture.AuthenticateAsync(context);
        await using var transaction = borrowed == 2
            ? await context.Database.BeginTransactionAsync() : null;
        if (borrowed == 0) context.EnableTenantFilterBypass("Borrowed authority regression.");
        if (borrowed == 1) context.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        var result = await CreateDutyRepository(fixture, privacy, context, identity).ReconcileScheduleRiskAsync(null, 1);
        await Assert.That(result.Available).IsFalse();
        await Assert.That(context.Database.CurrentTransaction).IsEqualTo(transaction);
        if (transaction is not null) await transaction.RollbackAsync();
        var retained = await ReadScheduleReviewAsync(fixture, reviewId);
        await Assert.That(retained.State).IsEqualTo(ModerationScheduleRiskReviewState.Pending);
        await Assert.That(retained.Revision).IsEqualTo(1L);
    }

    [Test]
    public async Task Regressed_clock_rejects_one_review_without_throwing_out_the_bounded_pass()
    {
        await using var fixture = await Fixture.CreateAsync();
        var reviewId = await StoreScheduleReviewAsync(fixture);
        await using (var writer = fixture.Context())
        {
            writer.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
            var review = await writer.ModerationScheduleRiskReviews.SingleAsync(row => row.Id == reviewId);
            var original = review.Coordinates;
            review.Rebind(ModerationScheduleRiskCoordinates.Create(original.EventScope, original.SessionId,
                Guid.NewGuid(), original.ParticipantId, original.Participant, original.ParticipantGeneration,
                original.PolicyId, original.PolicyRevision, original.PublicationId, original.Policy),
                review.DueAt, review.Revision, Now.AddMinutes(1));
            await writer.SaveChangesAsync();
        }
        await using var privacy = await DutyPrivacyDatabase.CreateAsync();
        fixture.Clock.UtcNow = Now;
        var pass = await ScheduleRiskPassAsync(fixture, privacy);
        await Assert.That(pass.Available).IsTrue();
        await Assert.That(pass.Results.Single().Outcome).IsEqualTo(ModerationScheduleRiskReconciliationOutcome.Unavailable);
        var retained = await ReadScheduleReviewAsync(fixture, reviewId);
        await Assert.That(retained.UpdatedAt).IsEqualTo(Now.AddMinutes(1));
        await Assert.That(retained.Revision).IsEqualTo(2L);
        await Assert.That(retained.State).IsEqualTo(ModerationScheduleRiskReviewState.Pending);
        await Assert.That(retained.OriginalSanctionEndsAt).IsEqualTo(Now.AddHours(1));
    }

    private static async Task<Guid> StoreScheduleReviewAsync(Fixture fixture)
    {
        await using var context = fixture.Context();
        context.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        var source = fixture.Database;
        var subject = SubjectReference.Create(ModerationSubjectKind.Human, source.SubjectId, source.Instance);
        var issuer = SubjectReference.Create(ModerationSubjectKind.Human, source.AdministratorId, source.Instance);
        var reviewer = SubjectReference.Create(ModerationSubjectKind.Human, source.ReviewerId, source.Instance);
        var rule = ModerationRuleReference.Create(source.Scope.Id, "event-risk", 1);
        var caseRow = ModerationCase.Open(Guid.CreateVersion7(), subject, source.Scope, Guid.CreateVersion7(), Now);
        var decision = ModerationDecision.Propose(Guid.CreateVersion7(), caseRow.Id, subject, issuer, source.Instance,
            source.Scope, rule, [ModerationCapability.Publish], Now, Now.AddHours(1), null, 1, Now);
        var finding = ModerationFinding.Finalize(Guid.CreateVersion7(), caseRow, 1, source.Scope, null, null,
            rule, rule, rule, rule, reviewer,
            ModerationFindingEvidenceReference.Response(Guid.CreateVersion7(), caseRow.Id, 1, Now),
            "Deadline custody fixture; no native risk admission asserted.", Now);
        decision.RecordResponse(1, Now);
        decision.AcceptFinding(finding.Id, 2, Now);
        decision.Approve(reviewer, source.Scope, 3, Now);
        var sanction = decision.Activate(Guid.CreateVersion7(), 4, Now);
        var eventId = await context.EventResources.Where(row => row.Id == fixture.ResourceId)
            .Select(row => row.EventId).SingleAsync();
        var coordinates = ModerationScheduleRiskCoordinates.Create(
            AuthorityScope.Resource(source.Scope, eventId), Guid.CreateVersion7(), Guid.NewGuid(),
            Guid.CreateVersion7(), subject, Guid.NewGuid(), Guid.CreateVersion7(), 1, Guid.CreateVersion7(), rule);
        var review = ModerationScheduleRiskReview.Open(Guid.CreateVersion7(), sanction, 1, coordinates,
            reviewer, issuer, Now.AddHours(3), Now);
        context.AddRange(caseRow, finding, decision, sanction, review);
        await context.SaveChangesAsync();
        return review.Id;
    }

    private static async Task<ModerationScheduleRiskReview> ReadScheduleReviewAsync(Fixture fixture, Guid id)
    {
        await using var context = fixture.Context();
        context.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        var review = await context.ModerationScheduleRiskReviews.AsNoTracking().SingleAsync(row => row.Id == id);
        review.ValidatePersistedState();
        return review;
    }

    private static async Task<ModerationScheduleRiskReconciliationBatch> ScheduleRiskPassAsync(
        Fixture fixture, DutyPrivacyDatabase privacy, Guid? tenantId = null,
        bool expireKeyAfterAuthentication = false, Guid? after = null, params IInterceptor[] interceptors)
    {
        await using var context = fixture.Context(interceptors);
        var identity = await fixture.AuthenticateAsync(context);
        if (tenantId is { } tenant) context.TenantContext = new SelectedTenant(tenant);
        if (expireKeyAfterAuthentication)
        {
            await using var writer = fixture.Context();
            await writer.ExternalApiKeys.Where(row => row.KeyId == fixture.KeyId)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.ExpiresAt, fixture.Clock.UtcNow.UtcDateTime));
        }
        var repository = CreateDutyRepository(fixture, privacy, context, identity);
        return await repository.ReconcileScheduleRiskAsync(after, 1);
    }

    private sealed class ReplaceScheduleWorkAfterFlush(Guid reviewId, Guid ownerId, int mutation) : SaveChangesInterceptor
    {
        public bool Observed { get; private set; }

        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (!Observed && eventData.Context is { } context)
            {
                Observed = true;
                var (column, value) = mutation switch
                {
                    0 => ("owner_id", (object)ownerId),
                    1 => ("original_sanction_ends_at", (object)Now.AddHours(2)),
                    _ => ("due_at", (object)Now.AddHours(2))
                };
                await context.Database.ExecuteSqlRawAsync(
                    $"UPDATE moderation_schedule_risk_reviews SET {column} = {{0}} WHERE id = {{1}}",
                    [value, reviewId], cancellationToken);
            }
            return result;
        }
    }
}
