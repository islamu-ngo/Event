using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationCaseNativeProofTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task ResourceFindingCorrectionUsesRetainedActionScopeNotLiveMaterial(
        bool departedAdjudicator, bool foreignResource)
    {
        await using var fixture = await PreFindingFixture.CreateAsync(ModerationCaseTargetKind.Resource);
        var response = fixture.Response();
        await Assert.That((await fixture.ExecuteAsync(fixture.Database.CallerId, response)).Outcome)
            .IsEqualTo(ModerationQualificationWriteOutcome.Known);
        var scope = AuthorityScope.Resource(fixture.Case.Scope, fixture.Database.ResourceId);
        var rule = ModerationRuleReference.Create(fixture.Case.Scope.Id, "historical-resource", 1);
        var reason = ModerationRuleReference.Create(fixture.Case.Scope.Id, "historical-resource-reason", 1);
        var finding = ModerationFinding.Finalize(Guid.CreateVersion7(), fixture.Case, fixture.Case.Revision,
            scope, fixture.Case.AttributionSourceId, 1, rule, rule, reason, reason,
            SubjectReference.Create(ModerationSubjectKind.Human, fixture.Review.Caller.UserId!.Value,
                fixture.Case.Scope.Parent!),
            ModerationFindingEvidenceReference.Response(response.OperationId, fixture.Case.Id,
                fixture.Case.Revision, fixture.Database.Clock.Now),
            "Retained independent Resource finding with its original protected response.",
            fixture.Database.Clock.Now);
        await using (var history = fixture.Database.Context())
        {
            history.ModerationFindings.Add(finding);
            await history.SaveChangesAsync();
            fixture.Database.Clock.Now = (await history.ModerationCaseAttributionSources.AsNoTracking()
                .SingleAsync(row => row.Id == fixture.Case.AttributionSourceId)).ValidUntil;
            await history.Set<EventResource>().Where(row => row.Id == fixture.Database.ResourceId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.IsDeleted, true));
            if (departedAdjudicator)
                await history.Users.Where(row => row.Id == fixture.Review.Caller.UserId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.IsDeleted, true));
        }
        await using (var cleanup = fixture.Database.Context())
            await fixture.Repository(cleanup, fixture.Assessor).PurgeExpiredPreFindingAsync(100, default);
        await using var writer = fixture.Database.Context();
        var epoch = await writer.ModerationQualificationEpochs.AsNoTracking().SingleAsync();
        var request = new ReverseModerationFindingSource
        {
            OperationId = Guid.CreateVersion7(),
            Target = foreignResource ? AuthorityScope.Resource(fixture.Case.Scope, Guid.CreateVersion7()) : scope,
            ExpectedQualificationRevision = epoch.Revision, FindingId = finding.Id,
            ExpectedFindingRevision = finding.Revision, ReversalReferenceId = Guid.CreateVersion7(),
            Justification = "Current independent correction remains within the retained Resource action scope."
        };
        var repository = fixture.Repository(writer, fixture.Assessor);
        var result = await repository.ExecuteAsync(request);
        await Assert.That(result.Outcome).IsEqualTo(foreignResource
            ? ModerationQualificationWriteOutcome.Denied : ModerationQualificationWriteOutcome.Known);
        if (!foreignResource)
            await Assert.That(await repository.ExecuteAsync(request)).IsEqualTo(result);
        await using var inspect = fixture.Database.Context();
        var retained = await inspect.ModerationFindings.AsNoTracking().SingleAsync();
        await Assert.That(retained.Scope).IsEqualTo(scope);
        await Assert.That(retained.State).IsEqualTo(foreignResource
            ? ModerationFindingState.Final : ModerationFindingState.Reversed);
        await Assert.That(retained.AttributionSourceId).IsEqualTo(finding.AttributionSourceId);
        await Assert.That(retained.EvidenceReferenceId).IsEqualTo(response.OperationId);
        await Assert.That((await inspect.Set<ModerationPreFindingNotice>().AsNoTracking().SingleAsync())
            .ProtectedResponse.IsEmpty).IsTrue();
        await Assert.That(await inspect.ModerationSanctions.AnyAsync()).IsFalse();
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    public async Task HistoricalFindingReversalNeedsRetainedOwnerNotLiveOriginalAdjudicator(
        bool departed, bool foreignCase)
    {
        await using var fixture = await ScheduledProvenanceFixture.CreateAsync();
        await using (var cleanup = fixture.Database.Context())
        {
            fixture.Database.Clock.Now = (await cleanup.ModerationCaseAttributionSources.AsNoTracking()
                .SingleAsync(row => row.Id == fixture.Case.AttributionSourceId)).ValidUntil;
            await fixture.Original.Repository(cleanup, fixture.Original.Assessor)
                .PurgeExpiredPreFindingAsync(100, default);
            if (departed)
                await cleanup.Users.Where(row => row.Id == fixture.Original.Review.Caller.UserId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.IsDeleted, true));
            if (foreignCase)
                await cleanup.ModerationFindings.ExecuteUpdateAsync(setters =>
                    setters.SetProperty(row => row.CaseId, fixture.Original.Case.Id));
        }
        await using var writer = fixture.Database.Context();
        var finding = await writer.ModerationFindings.AsNoTracking().SingleAsync();
        var epoch = await writer.ModerationQualificationEpochs.AsNoTracking().SingleAsync();
        var request = new ReverseModerationFindingSource
        {
            OperationId = Guid.CreateVersion7(), Target = finding.Scope,
            ExpectedQualificationRevision = epoch.Revision, FindingId = finding.Id,
            ExpectedFindingRevision = finding.Revision, ReversalReferenceId = Guid.CreateVersion7(),
            Justification = "Current independent correction of the retained finding."
        };
        var repository = fixture.Original.Repository(writer, fixture.Original.Assessor);
        var reversed = await repository.ExecuteAsync(request);
        await Assert.That(reversed.Outcome).IsEqualTo(foreignCase
            ? ModerationQualificationWriteOutcome.Denied : ModerationQualificationWriteOutcome.Known);
        if (!foreignCase)
            await Assert.That(await repository.ExecuteAsync(request)).IsEqualTo(reversed);
        await using var inspect = fixture.Database.Context();
        var retained = await inspect.ModerationFindings.AsNoTracking().SingleAsync();
        await Assert.That(retained.State).IsEqualTo(foreignCase
            ? ModerationFindingState.Final : ModerationFindingState.Reversed);
        await Assert.That(retained.Revision).IsEqualTo(finding.Revision + (foreignCase ? 0 : 1));
        await Assert.That(retained.AdjudicatorId).IsEqualTo(finding.AdjudicatorId);
        await Assert.That(retained.EvidenceReferenceId).IsEqualTo(finding.EvidenceReferenceId);
        await Assert.That(retained.AttributionSourceId).IsEqualTo(finding.AttributionSourceId);
        await Assert.That(retained.ReversedById).IsEqualTo(foreignCase ? null : (Guid?)fixture.Original.Assessor);
        await Assert.That((await inspect.Set<ModerationPreFindingNotice>().AsNoTracking()
            .SingleAsync(row => row.Id == fixture.NoticeId)).ProtectedResponse.IsEmpty).IsTrue();
        await Assert.That((await inspect.ModerationSanctions.AsNoTracking().SingleAsync()).Revision)
            .IsEqualTo(fixture.Sanction.Revision);
    }

    [Test]
    [Arguments(false, false, false)]
    [Arguments(false, true, false)]
    [Arguments(true, false, false)]
    [Arguments(true, true, false)]
    [Arguments(false, false, true)]
    [Arguments(false, true, true)]
    [Arguments(true, false, true)]
    [Arguments(true, true, true)]
    public async Task RetainedProceduralWithdrawalDoesNotRequalifyExpiredAttribution(
        bool withdrawAttribution, bool reviewer, bool closedCase)
    {
        await using var fixture = await ScheduledProvenanceFixture.CreateAsync();
        var database = fixture.Database;
        if (withdrawAttribution)
        {
            await using var withdrawal = database.Context();
            await Assert.That(await AttributionWriter(withdrawal, database, fixture.Original.Review.Caller)
                .WithdrawAsync(fixture.Case.AttributionSourceId!.Value, Guid.CreateVersion7(), 1, default))
                .IsNotNull();
            database.Clock.Now = fixture.Until;
        }
        else
        {
            await using var source = database.Context();
            database.Clock.Now = (await source.ModerationCaseAttributionSources.AsNoTracking()
                .SingleAsync(row => row.Id == fixture.Case.AttributionSourceId)).ValidUntil;
        }
        await using (var cleanup = database.Context())
            await fixture.Original.Repository(cleanup, fixture.Original.Assessor)
                .PurgeExpiredPreFindingAsync(100, default);
        await using var writer = database.Context();
        var owner = await writer.ModerationCases.SingleAsync(row => row.Id == fixture.Case.Id);
        if (closedCase)
        {
            owner.Close(owner.Revision, database.Clock.Now);
            await writer.SaveChangesAsync();
            writer.ChangeTracker.Clear();
        }
        var epoch = await writer.ModerationQualificationEpochs.AsNoTracking().SingleAsync();
        var assignment = await writer.ModerationReviewerAssignments.AsNoTracking().SingleAsync(
            row => row.CaseId == fixture.Case.Id && row.ReviewerHumanId == fixture.Original.Review.Caller.UserId);
        var policy = await writer.ModerationQualificationPolicySources.AsNoTracking()
            .SingleAsync(row => row.CaseId == fixture.Case.Id);
        var adoption = await writer.ModerationQualificationPolicyAdoptions.AsNoTracking()
            .SingleAsync(row => row.SourceId == policy.Id);
        ModerationQualificationMutation request = reviewer
            ? new WithdrawModerationReviewerSource
            {
                OperationId = Guid.CreateVersion7(), Target = fixture.Case.Scope,
                ExpectedQualificationRevision = epoch.Revision, AssignmentId = assignment.Id,
                ExpectedAssignmentRevision = assignment.Revision
            }
            : new RevokeModerationQualificationPolicyAdoption
            {
                OperationId = Guid.CreateVersion7(), Target = fixture.Case.Scope,
                ExpectedQualificationRevision = epoch.Revision, CaseId = fixture.Case.Id,
                ExpectedCaseRevision = owner.Revision, SourceId = policy.Id,
                ExpectedSourceRevision = policy.Revision, AdoptionId = adoption.Id,
                ExpectedAdoptionRevision = adoption.Revision
            };
        var repository = fixture.Original.Repository(writer, fixture.Original.Assessor);
        var first = await repository.ExecuteAsync(request);
        await Assert.That(first.Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        await Assert.That(await repository.ExecuteAsync(request)).IsEqualTo(first);
        await using var inspect = database.Context();
        var retainedReviewer = await inspect.ModerationReviewerAssignments.AsNoTracking()
            .SingleAsync(row => row.Id == assignment.Id);
        var retainedAdoption = await inspect.ModerationQualificationPolicyAdoptions.AsNoTracking()
            .SingleAsync(row => row.Id == adoption.Id);
        await Assert.That(retainedReviewer.WithdrawnAt.HasValue).IsEqualTo(reviewer);
        await Assert.That(retainedReviewer.Revision).IsEqualTo(assignment.Revision + (reviewer ? 1 : 0));
        await Assert.That(retainedReviewer.QualifiedUntil).IsEqualTo(assignment.QualifiedUntil);
        await Assert.That(retainedAdoption.RevokedAt.HasValue).IsEqualTo(!reviewer);
        await Assert.That(retainedAdoption.Revision).IsEqualTo(adoption.Revision + (reviewer ? 0 : 1));
        await Assert.That(retainedAdoption.ValidUntil).IsEqualTo(adoption.ValidUntil);
        await Assert.That((await inspect.ModerationFindings.AsNoTracking().SingleAsync()).State)
            .IsEqualTo(ModerationFindingState.Final);
        await Assert.That((await inspect.ModerationCases.AsNoTracking()
            .SingleAsync(row => row.Id == fixture.Case.Id)).State).IsEqualTo(closedCase
                ? ModerationCaseState.Closed : ModerationCaseState.Open);
        await Assert.That((await inspect.Set<ModerationPreFindingNotice>().AsNoTracking()
            .SingleAsync(row => row.Id == fixture.NoticeId)).ProtectedResponse.IsEmpty).IsTrue();
        await Assert.That((await inspect.ModerationQualificationEpochs.AsNoTracking().SingleAsync()).Revision)
            .IsEqualTo(epoch.Revision + 1);
    }

    [Test]
    [Arguments(false, "authorized")]
    [Arguments(true, "authorized")]
    [Arguments(false, "case-revision")]
    [Arguments(false, "foreign-case")]
    [Arguments(false, "approval-revision")]
    [Arguments(false, "qualification-revision")]
    [Arguments(false, "caller")]
    public async Task RetainedDisclosureCorrectionRequiresCurrentAuthorityButNotOriginalPrivateCustody(
        bool withdraw, string change)
    {
        await using var fixture = await ScheduledProvenanceFixture.CreateAsync();
        var database = fixture.Database;
        var approvedAt = database.Clock.Now;
        ModerationNoticeDisclosureApproval approval;
        await using (var seed = database.Context())
        {
            var publication = await seed.ModerationPolicyPublications.AsNoTracking().SingleAsync();
            var intent = new ModerationNoticeDisclosureIntent(
                Guid.CreateVersion7(), Guid.CreateVersion7(), fixture.Decision.Id, fixture.Decision.Revision,
                fixture.Case.Id, fixture.Case.Revision, publication.PolicyId, publication.Revision,
                publication.Id, Guid.CreateVersion7(), fixture.Sanction.Id, new string('a', 64),
                new("Original retained rule."), new("Original retained reason."),
                [new("Original approved subject-visible explanation.")],
                new("Protected remedies remain available."), new("Request independent correction."),
                new("Human adjudication."), new("Finite original retention."),
                approvedAt.AddMinutes(10), approvedAt.AddYears(1));
            var host = await seed.ModerationHostBindings.AsNoTracking().SingleAsync();
            var epoch = await seed.ModerationQualificationEpochs.AsNoTracking().SingleAsync();
            approval = ModerationNoticeDisclosureApproval.Approve(intent, host.Id, database.TrustId,
                database.TenantId, fixture.Original.Assessor, epoch.Revision, approvedAt);
            seed.ModerationNoticeDisclosureApprovals.Add(approval);
            await seed.SaveChangesAsync();
        }
        if (withdraw)
        {
            await using var withdrawal = database.Context();
            await Assert.That(await AttributionWriter(withdrawal, database, fixture.Original.Review.Caller)
                .WithdrawAsync(fixture.Case.AttributionSourceId!.Value, Guid.CreateVersion7(), 1, default))
                .IsNotNull();
            database.Clock.Now = fixture.Until;
        }
        else
        {
            await using var source = database.Context();
            database.Clock.Now = (await source.ModerationCaseAttributionSources.AsNoTracking()
                .SingleAsync(row => row.Id == fixture.Case.AttributionSourceId)).ValidUntil;
        }
        await using (var cleanup = database.Context())
        {
            await fixture.Original.Repository(cleanup, fixture.Original.Assessor)
                .PurgeExpiredPreFindingAsync(100, default);
        }
        await using var writer = database.Context();
        var currentEpoch = await writer.ModerationQualificationEpochs.AsNoTracking().SingleAsync();
        var request = new RevokeModerationNoticeDisclosureSource
        {
            OperationId = Guid.CreateVersion7(), Target = fixture.Case.Scope,
            ExpectedQualificationRevision = currentEpoch.Revision,
            CaseId = fixture.Case.Id, ExpectedCaseRevision = fixture.Case.Revision,
            ApprovalId = approval.Id, ExpectedApprovalRevision = 1
        };
        request = change switch
        {
            "case-revision" => request with { ExpectedCaseRevision = request.ExpectedCaseRevision + 1 },
            "foreign-case" => request with { CaseId = fixture.Original.Case.Id },
            "approval-revision" => request with { ExpectedApprovalRevision = 2 },
            "qualification-revision" => request with { ExpectedQualificationRevision = currentEpoch.Revision - 1 },
            "authorized" or "caller" => request,
            _ => throw new ArgumentException("Unknown correction boundary.", nameof(change))
        };
        var repository = fixture.Original.Repository(writer,
            change == "caller" ? database.CallerId : fixture.Original.Assessor);
        var first = await repository.ExecuteAsync(request);
        if (change != "authorized")
        {
            await Assert.That(first.Outcome).IsNotEqualTo(ModerationQualificationWriteOutcome.Known);
            await using var rejected = database.Context();
            await Assert.That((await rejected.ModerationNoticeDisclosureApprovals.AsNoTracking().SingleAsync()).Revision)
                .IsEqualTo(1L);
            await Assert.That(await rejected.ModerationQualificationMutationReceipts.AsNoTracking()
                .AnyAsync(row => row.Id == request.OperationId)).IsFalse();
            await Assert.That((await rejected.ModerationQualificationEpochs.AsNoTracking().SingleAsync()).Revision)
                .IsEqualTo(currentEpoch.Revision);
            return;
        }
        var replay = await repository.ExecuteAsync(request);
        await Assert.That(first.Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        await Assert.That(replay).IsEqualTo(first);
        await Assert.That((await repository.ExecuteAsync(request with
        {
            ExpectedCaseRevision = request.ExpectedCaseRevision + 1
        })).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Conflict);
        await using var inspect = database.Context();
        var retained = await inspect.ModerationNoticeDisclosureApprovals.AsNoTracking().SingleAsync();
        await Assert.That(retained.Revision).IsEqualTo(2L);
        await Assert.That(retained.RevokedByHumanId).IsEqualTo((Guid?)fixture.Original.Assessor);
        await Assert.That(retained.ValidUntil).IsEqualTo(approval.ValidUntil);
        await Assert.That(retained.RetainUntil).IsEqualTo(approval.RetainUntil);
        await Assert.That(retained.FactsJson).IsEqualTo(approval.FactsJson);
        await Assert.That((await inspect.ModerationCases.AsNoTracking()
            .SingleAsync(row => row.Id == fixture.Case.Id)).AttributionSourceId)
            .IsEqualTo(fixture.Case.AttributionSourceId);
        await Assert.That((await inspect.ModerationFindings.AsNoTracking().SingleAsync()).State)
            .IsEqualTo(ModerationFindingState.Final);
        await Assert.That((await inspect.ModerationSanctions.AsNoTracking().SingleAsync()).Revision)
            .IsEqualTo(fixture.Sanction.Revision);
        var notice = await inspect.Set<ModerationPreFindingNotice>().AsNoTracking()
            .SingleAsync(row => row.Id == fixture.NoticeId);
        await Assert.That(notice.ProtectedNotice.IsEmpty).IsTrue();
        await Assert.That(notice.ProtectedResponse.IsEmpty).IsTrue();
        await Assert.That(notice.RetainUntil).IsEqualTo(fixture.Until);
        await Assert.That(await inspect.ModerationQualificationMutationReceipts.AsNoTracking()
            .CountAsync(row => row.Id == request.OperationId)).IsEqualTo(1);
        await Assert.That((await inspect.ModerationQualificationEpochs.AsNoTracking().SingleAsync()).Revision)
            .IsEqualTo(currentEpoch.Revision + 1);
    }
}
