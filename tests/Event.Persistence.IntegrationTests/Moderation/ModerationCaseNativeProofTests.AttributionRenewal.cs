using System.Data;
using Explore.Application.Contracts.Moderation;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationCaseNativeProofTests
{
    [Test]
    public async Task ExpiredAttributionRenewsSameProceedingAndExactRetryRetainsItsGeneration()
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.EventVersion);
        var prepared = await PrepareAttributionRenewalAsync(database, true);
        await using var context = database.Context();
        var writer = AttributionWriter(context, database, prepared.Review.Caller);
        var accepted = await writer.RenewAsync(prepared.Request, default);
        await Assert.That(accepted).IsNotNull();
        await Assert.That(accepted!.Generation).IsEqualTo(2L);
        await Assert.That(accepted.CaseId).IsEqualTo(prepared.Case.Id);
        await Assert.That(accepted.Id).IsEqualTo(prepared.Request.Qualification.OperationId);
        var replay = await writer.RenewAsync(prepared.Request, default);
        await Assert.That(replay).IsNotNull();
        await Assert.That(replay!.Id).IsEqualTo(accepted.Id);
        await Assert.That(replay.QualifiedAt).IsEqualTo(accepted.QualifiedAt);
        await Assert.That(replay.ValidUntil).IsEqualTo(accepted.ValidUntil);
        await Assert.That(await writer.RenewAsync(prepared.Request with
        {
            Qualification = prepared.Request.Qualification with
            {
                ValidUntil = prepared.Request.Qualification.ValidUntil.AddSeconds(-1)
            }
        }, default)).IsNull();
        await using var inspect = database.Context();
        var head = await inspect.ModerationCaseAttributionHeads.AsNoTracking().SingleAsync();
        var receipt = await inspect.ModerationCaseAttributionRenewalReceipts.AsNoTracking().SingleAsync();
        var original = await inspect.ModerationCaseAttributionSources.AsNoTracking()
            .SingleAsync(row => row.Id == prepared.Source.Id);
        var owner = await inspect.ModerationCases.AsNoTracking().SingleAsync(row => row.Id == prepared.Case.Id);
        await Assert.That(head.SourceId).IsEqualTo(accepted.Id);
        await Assert.That(head.OpeningSourceId).IsEqualTo(original.Id);
        await Assert.That(head.Generation).IsEqualTo(2L);
        await Assert.That(head.Revision).IsEqualTo(2L);
        await Assert.That(receipt.Id).IsEqualTo(accepted.Id);
        await Assert.That(receipt.ExpectedSourceId).IsEqualTo(original.Id);
        await Assert.That(receipt.ExpectedGeneration).IsEqualTo(1L);
        await Assert.That(receipt.ExpectedHeadRevision).IsEqualTo(1L);
        await Assert.That(receipt.ExpectedCaseRevision).IsEqualTo(prepared.Case.Revision);
        await Assert.That(original.ValidUntil).IsEqualTo(prepared.Source.ValidUntil);
        await Assert.That(original.QualifiedAt).IsEqualTo(prepared.Source.QualifiedAt);
        await Assert.That(original.WithdrawnAt).IsNull();
        await Assert.That(owner.AttributionSourceId).IsEqualTo(original.Id);
        await Assert.That(owner.CreatedAt).IsEqualTo(prepared.Case.CreatedAt);
        await Assert.That(owner.Revision).IsEqualTo(prepared.Case.Revision);
        await Assert.That(await inspect.ModerationCaseAttributionSources.CountAsync()).IsEqualTo(2);
        await Assert.That(await inspect.ModerationCases.CountAsync()).IsEqualTo(2);
        await Assert.That(await inspect.ModerationFindings.AnyAsync()).IsFalse();
        await Assert.That(await inspect.ModerationSanctions.AnyAsync()).IsFalse();
        inspect.CurrentUserService = prepared.Review.Caller;
        await using var transaction = await inspect.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var observer = new ModerationCaseTargetProofOwner(inspect, prepared.Review.Caller, database.Machine, database.Clock);
        await Assert.That((await observer.AcquireCurrentAttributionAsync(owner.Id, default))!.Id).IsEqualTo(accepted.Id);
        await Assert.That(await observer.RecheckAttributionAsync(default)).IsTrue();
    }

    [Test]
    public async Task NewReadGrantCannotRenewUnchangedLiveResponsibilityGrounds()
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.EventVersion);
        var prepared = await PrepareAttributionRenewalAsync(database, false);
        await Assert.That(prepared.Request.Qualification.ReadGrantId).IsNotEqualTo(prepared.Source.ReadGrantId);
        await using (var context = database.Context())
            await Assert.That(await AttributionWriter(context, database, prepared.Review.Caller)
                .RenewAsync(prepared.Request, default)).IsNull();
        await using var inspect = database.Context();
        var head = await inspect.ModerationCaseAttributionHeads.SingleAsync();
        await Assert.That(head.SourceId).IsEqualTo(prepared.Source.Id);
        await Assert.That(head.Generation).IsEqualTo(1L);
        await Assert.That(await inspect.ModerationCaseAttributionSources.CountAsync()).IsEqualTo(1);
        await Assert.That(await inspect.ModerationCaseAttributionRenewalReceipts.AnyAsync()).IsFalse();
    }

    [Test]
    [Arguments("source")]
    [Arguments("generation")]
    [Arguments("head-revision")]
    [Arguments("case-revision")]
    [Arguments("operation")]
    public async Task RenewalRejectsStaleExpectedTupleWithoutCreatingAnotherProceeding(string defect)
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.EventVersion);
        var prepared = await PrepareAttributionRenewalAsync(database, true);
        var request = defect switch
        {
            "source" => prepared.Request with { ExpectedSourceId = Guid.CreateVersion7() },
            "generation" => prepared.Request with { ExpectedGeneration = 2 },
            "head-revision" => prepared.Request with { ExpectedHeadRevision = 2 },
            "case-revision" => prepared.Request with { ExpectedCaseRevision = 2 },
            _ => prepared.Request with
            {
                Qualification = prepared.Request.Qualification with { OperationId = prepared.Source.Id }
            }
        };
        await using (var context = database.Context())
            await Assert.That(await AttributionWriter(context, database, prepared.Review.Caller)
                .RenewAsync(request, default)).IsNull();
        await using var inspect = database.Context();
        await Assert.That((await inspect.ModerationCaseAttributionHeads.SingleAsync()).SourceId)
            .IsEqualTo(prepared.Source.Id);
        await Assert.That(await inspect.ModerationCaseAttributionSources.CountAsync()).IsEqualTo(1);
        await Assert.That(await inspect.ModerationCaseAttributionRenewalReceipts.AnyAsync()).IsFalse();
        await Assert.That(await inspect.ModerationCases.CountAsync()).IsEqualTo(2);
    }

    [Test]
    [Arguments("grant")]
    [Arguments("head")]
    [Arguments("clock")]
    [Arguments("caller")]
    public async Task FinalRenewalCheckpointRollsBackSourceReceiptAndHeadTogether(string defect)
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.EventVersion);
        var prepared = await PrepareAttributionRenewalAsync(database, true);
        var flushed = false;
        var checkpoint = new AttributionSavedCheckpoint(async context =>
        {
            var head = await context.ModerationCaseAttributionHeads.AsNoTracking().SingleAsync();
            flushed = head.Generation == 2
                && await context.ModerationCaseAttributionRenewalReceipts.AnyAsync(
                    row => row.Id == prepared.Request.Qualification.OperationId);
            if (defect == "grant")
                await context.ModerationCaseReadGrants.Where(
                    row => row.Id == prepared.Request.Qualification.ReadGrantId).ExecuteUpdateAsync(update =>
                    update.SetProperty(row => row.RevokedAt, database.Clock.Now).SetProperty(row => row.Revision, 2));
            else if (defect == "head")
                await context.ModerationCaseAttributionHeads.Where(row => row.Id == head.Id).ExecuteUpdateAsync(update =>
                    update.SetProperty(row => row.SourceId, prepared.Source.Id)
                        .SetProperty(row => row.Generation, 1L).SetProperty(row => row.Revision, 1L));
            else if (defect == "clock") database.Clock.Now = prepared.Request.Qualification.ValidUntil;
            else prepared.Review.Caller.IsAuthenticated = false;
        });
        await using (var context = database.Context(checkpoint))
            await Assert.That(await AttributionWriter(context, database, prepared.Review.Caller)
                .RenewAsync(prepared.Request, default)).IsNull();
        await Assert.That(checkpoint.Observed).IsTrue();
        await Assert.That(flushed).IsTrue();
        await using var inspect = database.Context();
        var retainedHead = await inspect.ModerationCaseAttributionHeads.SingleAsync();
        await Assert.That(retainedHead.SourceId).IsEqualTo(prepared.Source.Id);
        await Assert.That(retainedHead.Generation).IsEqualTo(1L);
        await Assert.That(await inspect.ModerationCaseAttributionSources.CountAsync()).IsEqualTo(1);
        await Assert.That(await inspect.ModerationCaseAttributionRenewalReceipts.AnyAsync()).IsFalse();
        await Assert.That((await inspect.ModerationCases.SingleAsync(row => row.Id == prepared.Case.Id))
            .AttributionSourceId).IsEqualTo(prepared.Source.Id);
    }

    private static async Task<PreparedAttributionRenewal> PrepareAttributionRenewalAsync(
        NativeDatabase database, bool expire)
    {
        var review = await PrepareAttributionReviewAsync(database);
        var initial = review.Request(ModerationResponsibilityBasis.EventContent,
            ModerationSubjectKind.Content, database.EventId);
        ModerationCase owner;
        await using (var context = database.Context())
            owner = await AttributionWriter(context, database, review.Caller).AttributeAsync(initial, default)
                ?? throw new InvalidOperationException("Initial native attribution failed.");
        ModerationCaseAttributionSource source;
        await using (var inspect = database.Context())
            source = await inspect.ModerationCaseAttributionSources.AsNoTracking().SingleAsync();
        if (expire) database.Clock.Now = source.ValidUntil;
        var until = database.Clock.Now.AddMinutes(1);
        Guid grantId;
        await using (var grants = database.Context())
        {
            grants.CurrentUserService = review.Caller;
            await using var transaction = await grants.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var qualifier = new ModerationCaseTargetProofOwner(grants, review.Caller, database.Machine, database.Clock);
            var grant = await qualifier.GrantReadAsync(new(Guid.CreateVersion7(), review.RootId, 1,
                source.EvidenceSourceId, review.Caller.UserId!.Value, until), default);
            await Assert.That(grant).IsNotNull();
            grantId = grant!.Id;
            await grants.SaveChangesAsync();
            await Assert.That(await qualifier.RecheckIssuanceAsync(default)).IsTrue();
            await transaction.CommitAsync();
        }
        var qualification = initial with
        {
            OperationId = Guid.CreateVersion7(),
            ReadGrantId = grantId,
            ValidUntil = until
        };
        return new(review, owner, source, new(owner.Id, owner.Revision, source.Id, 1, 1, qualification));
    }

    private sealed record PreparedAttributionRenewal(
        AttributionReview Review, ModerationCase Case, ModerationCaseAttributionSource Source,
        ModerationCaseAttributionRenewalRequest Request);
}
