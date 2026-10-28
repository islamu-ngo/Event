using System.Data;
using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Repositories;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationCaseNativeProofTests
{
    [Test]
    public async Task FreshMaterialGenerationCannotBorrowUnexpiredOriginalPrivateResponse()
    {
        await using var fixture = await PreFindingFixture.CreateAsync();
        var response = fixture.Response();
        await Assert.That((await fixture.ExecuteAsync(fixture.Database.CallerId, response)).Outcome)
            .IsEqualTo(ModerationQualificationWriteOutcome.Known);
        Guid priorSourceId;
        DateTimeOffset until;
        byte[] originalCiphertext;
        await using (var original = fixture.Database.Context())
        {
            var notice = await original.Set<ModerationPreFindingNotice>().AsNoTracking().SingleAsync();
            priorSourceId = notice.AttributionSourceId;
            until = notice.RetainUntil;
            originalCiphertext = notice.ProtectedResponse.ToArray();
        }
        fixture.Database.Clock.Now = fixture.Database.Clock.Now.AddSeconds(1);
        var issued = await fixture.Database.IssueAsync(material: "Additional independently retained material.");
        await using (var intake = fixture.Database.Context())
        {
            var accepted = await Committer(intake, fixture.Database).SubmitAsync(
                new(ModerationCaseAccessPurpose.SubmitAllegation, Guid.Empty, Guid.CreateVersion7(), 0,
                    issued.Receipt.Id, issued.Token)
                {
                    Narrative = fixture.Database.Request(material: "Additional independently retained material.").Material
                }, default);
            await Assert.That(accepted.Outcome).IsEqualTo(ModerationCaseIntakeCommitOutcome.Accepted);
            await Assert.That(accepted.Acknowledgement!.CaseId).IsEqualTo(fixture.Review.RootId);
        }
        long rootRevision;
        Guid grantId;
        var qualifiedUntil = fixture.Database.Clock.Now.AddSeconds(30);
        await using (var grants = fixture.Database.Context())
        {
            grants.CurrentUserService = fixture.Review.Caller;
            rootRevision = (await grants.ModerationCases.SingleAsync(row => row.Id == fixture.Review.RootId)).Revision;
            await using var transaction = await grants.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var owner = new ModerationCaseTargetProofOwner(
                grants, fixture.Review.Caller, fixture.Database.Machine, fixture.Database.Clock);
            var grant = await owner.GrantReadAsync(new(Guid.CreateVersion7(), fixture.Review.RootId, rootRevision,
                issued.Receipt.SourceId, fixture.Review.Caller.UserId!.Value, qualifiedUntil), default);
            await Assert.That(grant).IsNotNull();
            grantId = grant!.Id;
            await grants.SaveChangesAsync();
            await Assert.That(await owner.RecheckIssuanceAsync(default)).IsTrue();
            await transaction.CommitAsync();
        }
        var qualification = new ModerationCaseAttributionRequest(Guid.CreateVersion7(), fixture.Review.RootId,
            rootRevision, rootRevision, grantId, ModerationResponsibilityBasis.EventContent,
            ModerationSubjectKind.Content, fixture.Database.EventId, qualifiedUntil);
        await using (var renewal = fixture.Database.Context())
        {
            var renewed = await AttributionWriter(renewal, fixture.Database, fixture.Review.Caller).RenewAsync(
                new(fixture.Case.Id, fixture.Case.Revision, priorSourceId, 1, 1, qualification), default);
            await Assert.That(renewed).IsNotNull();
            await Assert.That(renewed!.Generation).IsEqualTo(2L);
        }
        await using var observed = fixture.Database.Context();
        var repository = fixture.Repository(observed, fixture.Assessor);
        await fixture.Privacy.Authority(observed, fixture.Database.Clock).ExecuteSerializedAsync(async token =>
        {
            await using var transaction = await observed.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
            await Assert.That(await repository.DiscoverResponseHumansAsync(fixture.Case.Id, token)).IsNotNull();
            await Assert.That(await repository.AcquireCurrentResponseAsync(
                fixture.Case.Id, response.OperationId, token)).IsNull();
            return true;
        }, default);
        await using var inspect = fixture.Database.Context();
        var retained = await inspect.Set<ModerationPreFindingNotice>().AsNoTracking().SingleAsync();
        await Assert.That(fixture.Database.Clock.Now < until).IsTrue();
        await Assert.That(retained.HasResponseAt(fixture.Database.Clock.Now)).IsTrue();
        await Assert.That(retained.AttributionSourceId).IsEqualTo(priorSourceId);
        await Assert.That(retained.AttributionGeneration).IsEqualTo(1L);
        await Assert.That(retained.RetainUntil).IsEqualTo(until);
        await Assert.That(retained.ProtectedResponse.ToArray().SequenceEqual(originalCiphertext)).IsTrue();
        await Assert.That((await inspect.ModerationCases.SingleAsync(row => row.Id == fixture.Case.Id))
            .AttributionSourceId).IsEqualTo(priorSourceId);
    }
}
