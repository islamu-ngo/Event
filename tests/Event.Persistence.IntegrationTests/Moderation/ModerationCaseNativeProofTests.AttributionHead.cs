using System.Data;
using Explore.Domain;
using Explore.Persistence.Repositories;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationCaseNativeProofTests
{
    [Test]
    public async Task NativeAttributionCommitsOneOpeningHeadAndReplayRetainsItsExactCaseAndSource()
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.EventVersion);
        var review = await PrepareAttributionReviewAsync(database);
        var request = review.Request(ModerationResponsibilityBasis.EventContent, ModerationSubjectKind.Content, database.EventId);
        Guid caseId;
        await using (var context = database.Context())
        {
            var accepted = await AttributionWriter(context, database, review.Caller).AttributeAsync(request, default);
            await Assert.That(accepted).IsNotNull();
            caseId = accepted!.Id;
        }
        await using var original = database.Context();
        var head = await original.ModerationCaseAttributionHeads.AsNoTracking().SingleAsync();
        var source = await original.ModerationCaseAttributionSources.AsNoTracking().SingleAsync();
        var adjudication = await original.ModerationCases.AsNoTracking().SingleAsync(row => row.Id == caseId);
        var root = await original.ModerationCases.AsNoTracking().SingleAsync(row => row.Id == review.RootId);
        var host = await original.ModerationHostBindings.AsNoTracking().SingleAsync();
        await Assert.That(head.CaseId).IsEqualTo(caseId);
        await Assert.That(head.SourceId).IsEqualTo(request.OperationId);
        await Assert.That(head.OpeningSourceId).IsEqualTo(request.OperationId);
        await Assert.That(head.Generation).IsEqualTo(1L);
        await Assert.That(head.Revision).IsEqualTo(1L);
        await Assert.That(head.TenantId).IsEqualTo(database.TenantId);
        await Assert.That(head.TrustDomainId).IsEqualTo(database.TrustId);
        await Assert.That(head.HostBindingId).IsEqualTo(host.Id);
        await Assert.That(head.SolutionId).IsEqualTo(host.SolutionId);
        await Assert.That(head.InstanceId).IsEqualTo(host.InstanceId);
        await Assert.That(head.OccurrenceCaseId).IsEqualTo(review.RootId);
        await Assert.That(head.OccurrenceId).IsEqualTo(root.OccurrenceId);
        await Assert.That(head.OccurrenceEvidenceRevision).IsEqualTo(1L);
        await Assert.That(head.SubjectKind).IsEqualTo(ModerationSubjectKind.Content);
        await Assert.That(head.SubjectId).IsEqualTo(database.EventId);
        await Assert.That(source.Id).IsEqualTo(request.OperationId);
        await Assert.That(source.CaseId).IsEqualTo(caseId);
        await Assert.That(source.Generation).IsEqualTo(1L);
        await Assert.That(adjudication.AttributionSourceId).IsEqualTo(source.Id);
        await Assert.That(adjudication.OccurrenceCaseId).IsEqualTo(review.RootId);
        await Assert.That(root.Subject).IsNull();
        await Assert.That(root.Revision).IsEqualTo(1L);

        await using (var retry = database.Context())
        {
            var replay = await AttributionWriter(retry, database, review.Caller).AttributeAsync(request, default);
            await Assert.That(replay).IsNotNull();
            await Assert.That(replay!.Id).IsEqualTo(caseId);
            await Assert.That(replay.AttributionSourceId).IsEqualTo(request.OperationId);
            await Assert.That(replay.CreatedAt).IsEqualTo(adjudication.CreatedAt);
        }
        await using var retained = database.Context();
        var replayHead = await retained.ModerationCaseAttributionHeads.AsNoTracking().SingleAsync();
        await Assert.That(replayHead.Id).IsEqualTo(head.Id);
        await Assert.That(replayHead.CaseId).IsEqualTo(head.CaseId);
        await Assert.That(replayHead.SourceId).IsEqualTo(head.SourceId);
        await Assert.That(replayHead.OpeningSourceId).IsEqualTo(head.OpeningSourceId);
        await Assert.That(replayHead.Generation).IsEqualTo(head.Generation);
        await Assert.That(replayHead.Revision).IsEqualTo(head.Revision);
        await Assert.That(replayHead.TenantId).IsEqualTo(head.TenantId);
        await Assert.That(replayHead.TrustDomainId).IsEqualTo(head.TrustDomainId);
        await Assert.That(replayHead.HostBindingId).IsEqualTo(head.HostBindingId);
        await Assert.That(replayHead.SolutionId).IsEqualTo(head.SolutionId);
        await Assert.That(replayHead.InstanceId).IsEqualTo(head.InstanceId);
        await Assert.That(replayHead.OccurrenceCaseId).IsEqualTo(head.OccurrenceCaseId);
        await Assert.That(replayHead.OccurrenceId).IsEqualTo(head.OccurrenceId);
        await Assert.That(replayHead.OccurrenceEvidenceRevision).IsEqualTo(head.OccurrenceEvidenceRevision);
        await Assert.That(replayHead.SubjectKind).IsEqualTo(head.SubjectKind);
        await Assert.That(replayHead.SubjectId).IsEqualTo(head.SubjectId);
        var retainedSource = await retained.ModerationCaseAttributionSources.AsNoTracking().SingleAsync();
        await Assert.That(retainedSource.Id).IsEqualTo(source.Id);
        await Assert.That(retainedSource.CaseId).IsEqualTo(caseId);
        await Assert.That(retainedSource.QualifiedAt).IsEqualTo(source.QualifiedAt);
        await Assert.That(retainedSource.ValidUntil).IsEqualTo(source.ValidUntil);
        await Assert.That(await retained.ModerationCases.CountAsync()).IsEqualTo(2);
        await Assert.That(await retained.ModerationCaseIntakeAcknowledgements.CountAsync()).IsEqualTo(1);
    }

    [Test]
    [Arguments("grant")]
    [Arguments("caller")]
    public async Task PostflushAttributionAuthorityLossRollsBackOpeningHeadCaseAndSource(string defect)
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.EventVersion);
        var review = await PrepareAttributionReviewAsync(database);
        var request = review.Request(ModerationResponsibilityBasis.EventContent, ModerationSubjectKind.Content, database.EventId);
        var headFlushed = false;
        var checkpoint = new AttributionSavedCheckpoint(async context =>
        {
            var head = await context.ModerationCaseAttributionHeads.AsNoTracking().SingleAsync();
            headFlushed = head.SourceId == request.OperationId && head.Generation == 1
                && await context.ModerationCases.AnyAsync(row => row.Id == head.CaseId)
                && await context.ModerationCaseAttributionSources.AnyAsync(row => row.Id == head.SourceId);
            if (defect == "grant")
                await context.ModerationCaseReadGrants.Where(row => row.Id == review.GrantId).ExecuteUpdateAsync(update =>
                    update.SetProperty(row => row.RevokedAt, database.Clock.Now).SetProperty(row => row.Revision, 2));
            else review.Caller.IsAuthenticated = false;
        });
        await using (var context = database.Context(checkpoint))
            await Assert.That(await AttributionWriter(context, database, review.Caller).AttributeAsync(request, default)).IsNull();
        await Assert.That(checkpoint.Observed).IsTrue();
        await Assert.That(headFlushed).IsTrue();
        await using var retained = database.Context();
        await Assert.That(await retained.ModerationCaseAttributionHeads.AnyAsync()).IsFalse();
        await Assert.That(await retained.ModerationCaseAttributionSources.AnyAsync()).IsFalse();
        var root = await retained.ModerationCases.SingleAsync();
        await Assert.That(root.Id).IsEqualTo(review.RootId);
        await Assert.That(root.Subject).IsNull();
        await Assert.That(root.Revision).IsEqualTo(1L);
        await Assert.That(await retained.ModerationCaseIntakeAcknowledgements.CountAsync()).IsEqualTo(1);
    }

    [Test]
    [Arguments("tenant")]
    [Arguments("trust")]
    [Arguments("unselected-tenant")]
    [Arguments("unbound-trust")]
    public async Task AttributionHeadDisclosureRequiresSelectedTenantAndBoundTrust(string defect)
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.EventVersion);
        var review = await PrepareAttributionReviewAsync(database);
        await using (var context = database.Context())
            await Assert.That(await AttributionWriter(context, database, review.Caller).AttributeAsync(
                review.Request(ModerationResponsibilityBasis.EventContent, ModerationSubjectKind.Content, database.EventId),
                default)).IsNotNull();
        await using var read = database.Context();
        var head = await read.ModerationCaseAttributionHeads.AsNoTracking().SingleAsync();
        if (defect == "tenant") read.TenantContext = new SelectedTenant(Guid.CreateVersion7());
        else if (defect == "trust") read.ModerationTrustDomainId = Guid.CreateVersion7();
        else if (defect == "unselected-tenant") read.TenantContext = null;
        else read.ModerationTrustDomainId = null;
        await Assert.That(await read.ModerationCaseAttributionHeads.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == head.Id)).IsNull();
        read.TenantContext = new SelectedTenant(database.TenantId);
        read.ModerationTrustDomainId = database.TrustId;
        await Assert.That((await read.ModerationCaseAttributionHeads.AsNoTracking().SingleAsync()).Id).IsEqualTo(head.Id);
    }

    [Test]
    [Arguments("foreign-source")]
    [Arguments("subject")]
    [Arguments("occurrence")]
    [Arguments("evidence")]
    [Arguments("solution")]
    [Arguments("instance")]
    public async Task CurrentAttributionRefusesForgedHeadWithoutBorrowingOriginalSameCaseSource(string defect)
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.EventVersion);
        var review = await PrepareAttributionReviewAsync(database);
        var request = review.Request(ModerationResponsibilityBasis.EventContent, ModerationSubjectKind.Content, database.EventId);
        Guid caseId;
        Guid foreignSourceId;
        await using (var context = database.Context())
        {
            var accepted = await AttributionWriter(context, database, review.Caller).AttributeAsync(request, default);
            await Assert.That(accepted).IsNotNull();
            caseId = accepted!.Id;
        }
        await using (var positive = database.Context())
        {
            positive.CurrentUserService = review.Caller;
            await using var transaction = await positive.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var owner = new ModerationCaseTargetProofOwner(positive, review.Caller, database.Machine, database.Clock);
            var acquired = await owner.AcquireCurrentAttributionAsync(caseId, default);
            await Assert.That(acquired).IsNotNull();
            await Assert.That(acquired!.Id).IsEqualTo(request.OperationId);
            await Assert.That(await owner.RecheckAttributionAsync(default)).IsTrue();
        }
        if (defect == "foreign-source")
        {
            var foreignRequest = review.Request(ModerationResponsibilityBasis.EventSubmission,
                ModerationSubjectKind.Human, database.CallerId);
            await using var foreign = database.Context();
            await Assert.That(await AttributionWriter(foreign, database, review.Caller)
                .AttributeAsync(foreignRequest, default)).IsNotNull();
            foreignSourceId = foreignRequest.OperationId;
        }
        else foreignSourceId = Guid.Empty;
        await using (var mutation = database.Context())
        {
            var head = mutation.ModerationCaseAttributionHeads.Where(row => row.CaseId == caseId);
            // Persist a malformed association that still satisfies the native FK/check constraints.
            if (defect == "foreign-source")
                await head.ExecuteUpdateAsync(update => update.SetProperty(row => row.SourceId, foreignSourceId)
                    .SetProperty(row => row.OpeningSourceId, foreignSourceId));
            else if (defect == "subject")
                await head.ExecuteUpdateAsync(update => update.SetProperty(row => row.SubjectId, database.CallerId));
            else if (defect == "occurrence")
                await head.ExecuteUpdateAsync(update => update.SetProperty(row => row.OccurrenceId, Guid.CreateVersion7()));
            else if (defect == "evidence")
                await head.ExecuteUpdateAsync(update => update.SetProperty(row => row.OccurrenceEvidenceRevision, 2L));
            else if (defect == "solution")
                await head.ExecuteUpdateAsync(update => update.SetProperty(row => row.SolutionId, Guid.CreateVersion7()));
            else
                await head.ExecuteUpdateAsync(update => update.SetProperty(row => row.InstanceId, Guid.CreateVersion7()));
        }
        await using var denied = database.Context();
        denied.CurrentUserService = review.Caller;
        await using var deniedTransaction = await denied.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var deniedOwner = new ModerationCaseTargetProofOwner(denied, review.Caller, database.Machine, database.Clock);
        await Assert.That(await deniedOwner.AcquireCurrentAttributionAsync(caseId, default)).IsNull();
        var originalSource = await denied.ModerationCaseAttributionSources.AsNoTracking()
            .SingleAsync(row => row.Id == request.OperationId && row.CaseId == caseId);
        await Assert.That(originalSource.IsCurrentAt(database.Clock.Now)).IsTrue();
        await Assert.That((await denied.ModerationCases.AsNoTracking().SingleAsync(row => row.Id == caseId))
            .AttributionSourceId).IsEqualTo(request.OperationId);
    }
}
