using System.Data;
using System.Security.Cryptography;
using System.Text;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationPolicyPublicationTests
{
    [Test]
    public async Task Qualified_legal_append_preserves_approved_request_revision_without_claiming_matching_settlement()
    {
        await using var fixture = await RecognitionErasureFixture.CreateAsync(lawfulDeadline: TimeSpan.FromHours(1));
        await using var context = fixture.Database.Context();
        var repository = fixture.Repository(context);
        var approved = await AuthorizeLawfulErasureAsync(fixture, repository, context);
        var service = new ModerationRecognitionErasure(repository, fixture.Retained.Repository, fixture.Retained.Repository,
            new RelationalSettingMutationLock(context, new EfCoreUnitOfWork(context)));
        var fact = await service.AppendAsync(fixture.Request, fixture.ReceiptHash, Now.AddHours(2), CancellationToken.None);
        await Assert.That(fact).IsNotNull();
        var pending = (await repository.FindAsync(fixture.Request.IntentId, CancellationToken.None))!;
        await Assert.That(pending.Revision).IsEqualTo(approved.PendingRevision);
        await Assert.That(pending.AuthoritySequence).IsNull();
        await Assert.That(pending.HasCompleteMaterial).IsFalse();
        var legal = await context.Set<ModerationRecognitionDisposition>()
            .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure).SingleAsync();
        await Assert.That(legal.AuthoritySequence).IsEqualTo(fact!.AuthoritySequence);
        await Assert.That(legal.Revision).IsEqualTo(3L);
        await Assert.That((await repository.ReadReceivedAfterAsync(null, 100, CancellationToken.None)).Count).IsEqualTo(0);
        await Assert.That(await context.Set<UserPii>().AnyAsync(row => row.UserId == fixture.Subject)).IsTrue();
    }

    [Test]
    public async Task Consumed_legal_erasure_replays_after_source_withdrawal_and_input_destruction_without_keys()
    {
        await using var fixture = await RecognitionErasureFixture.CreateAsync(lawfulDeadline: TimeSpan.FromHours(1));
        await using var context = fixture.Database.Context();
        var repository = fixture.Repository(context);
        await AuthorizeLawfulErasureAsync(fixture, repository, context);
        var service = new ModerationRecognitionErasure(repository, fixture.Retained.Repository, fixture.Retained.Repository,
            new RelationalSettingMutationLock(context, new EfCoreUnitOfWork(context)));
        var fact = (await service.AppendAsync(fixture.Request, fixture.ReceiptHash, Now.AddHours(2), CancellationToken.None))!;
        await service.ExecutePurgeAsync(async token =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
            await repository.VerifyPurgeAsync(fact, token);
            await context.UserExternalLogins.Where(row => row.UserId == fixture.Subject).ExecuteDeleteAsync(token);
            await context.Set<UserPii>().Where(row => row.UserId == fixture.Subject).ExecuteDeleteAsync(token);
            var user = await context.Users.SingleAsync(row => row.Id == fixture.Subject, token);
            user.IsDeleted = true;
            user.DeletedAt = fixture.Clock.UtcNow.UtcDateTime;
            await context.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
            return true;
        }, CancellationToken.None);
        context.ChangeTracker.Clear();
        await fixture.WithdrawPrivacySourceAsync();
        fixture.Clock.UtcNow = Now.AddHours(2);
        var replay = await service.AppendAsync(fixture.Request, fixture.ReceiptHash, Now.AddHours(3), CancellationToken.None);
        await Assert.That(replay!.AuthoritySequence).IsEqualTo(fact.AuthoritySequence);
        await service.ExecutePurgeAsync(async token =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
            await repository.VerifyPurgeAsync(fact, token);
            return true;
        }, CancellationToken.None);
        await Assert.That(await context.UserExternalLogins.AnyAsync(row => row.UserId == fixture.Subject)).IsFalse();
        await Assert.That(await context.Set<UserPii>().AnyAsync(row => row.UserId == fixture.Subject)).IsFalse();
        await Assert.That((await repository.FindAsync(fixture.Request.IntentId, CancellationToken.None))!.AuthoritySequence).IsNull();
    }

    [Test]
    public async Task Original_legal_remedy_receipt_authenticates_after_short_status_receipt_expiry_without_current_source()
    {
        await using var fixture = await RecognitionErasureFixture.CreateAsync(lawfulDeadline: TimeSpan.FromHours(1));
        var receipt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        SHA256.HashData(Encoding.UTF8.GetBytes(receipt)).CopyTo(fixture.ReceiptHash, 0);
        await using var context = fixture.Database.Context();
        await AuthorizeLawfulErasureAsync(fixture, fixture.Repository(context), context);
        await fixture.WithdrawPrivacySourceAsync();
        fixture.Clock.UtcNow = Now.AddDays(1);
        await using var runtime = Event.Persistence.IntegrationTests.Privacy.GlobalLocationPrivacyErasureTests
            .CreateRuntime(context, fixture.Retained.Repository, fixture.Clock);
        await Assert.That(await runtime.Service.AuthenticateReceiptAsync(receipt, CancellationToken.None)).IsNull();
        var correction = new ModerationRecognitionCorrectionReceiptAuthenticator(fixture.Repository(context));
        await Assert.That(await correction.AuthenticateAsync(receipt, CancellationToken.None))
            .IsEqualTo((Guid?)fixture.Request.IntentId);
        await Assert.That(await correction.AuthenticateAsync(
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)), CancellationToken.None)).IsNull();
        var protectedUntil = (await context.Set<ModerationRecognitionDispositionRemedy>()
            .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure).SingleAsync()).RemedyUntil;
        fixture.Clock.UtcNow = protectedUntil;
        await Assert.That(await correction.AuthenticateAsync(receipt, CancellationToken.None)).IsNull();
    }

    [Test]
    public async Task Changed_pending_revision_cannot_consume_an_earlier_qualified_legal_approval()
    {
        await using var fixture = await RecognitionErasureFixture.CreateAsync(lawfulDeadline: TimeSpan.FromHours(1));
        await using var context = fixture.Database.Context();
        var repository = fixture.Repository(context);
        var approved = await AuthorizeLawfulErasureAsync(fixture, repository, context);
        await repository.RecordFailureAsync(fixture.Request, ModerationRecognitionPendingFailure.StoreUnavailable,
            CancellationToken.None);
        var service = new ModerationRecognitionErasure(repository, fixture.Retained.Repository, fixture.Retained.Repository,
            new RelationalSettingMutationLock(context, new EfCoreUnitOfWork(context)));
        await Assert.That(await service.AppendAsync(fixture.Request, fixture.ReceiptHash, Now.AddHours(2), CancellationToken.None)).IsNull();
        var pending = (await repository.FindAsync(fixture.Request.IntentId, CancellationToken.None))!;
        await Assert.That(pending.Revision > approved.PendingRevision).IsTrue();
        await Assert.That(pending.AuthoritySequence).IsNull();
        await Assert.That((await fixture.Retained.Repository.ReadAfterAsync(0, 100, CancellationToken.None)).Count).IsEqualTo(0);
        await Assert.That(await context.Set<UserPii>().AnyAsync(row => row.UserId == fixture.Subject)).IsTrue();
    }

    private static async Task<ModerationRecognitionDisposition> AuthorizeLawfulErasureAsync(
        RecognitionErasureFixture fixture, ModerationRecognitionErasureRepository repository, ExploreDbContext context)
    {
        fixture.Keys.Available = false;
        await repository.ReceiveAsync(fixture.Request, fixture.ReceiptHash, Now.AddHours(2), CancellationToken.None);
        await repository.ComputeAsync(fixture.Request, CancellationToken.None);
        var pending = (await repository.FindAsync(fixture.Request.IntentId, CancellationToken.None))!;
        fixture.Clock.UtcNow = pending.EscalateAt;
        var source = await context.Set<ModerationRecognitionPrivacySource>()
            .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure).SingleAsync();
        var adoption = await context.Set<ModerationRecognitionPrivacyAdoption>()
            .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure).SingleAsync();
        var assessment = await repository.AssessAsync(new(
            Guid.CreateVersion7(), pending.IntentId, pending.Revision,
            source.Id, source.Revision, source.WriterGeneration,
            adoption.Id, adoption.Revision, adoption.WriterGeneration,
            ModerationRecognitionDispositionGrounds.OriginalKeyRecoveryUnavailable, fixture.Clock.UtcNow.AddMinutes(20)),
            pending.RecoveryOwnerHumanId, CancellationToken.None);
        return await repository.AuthorizeAsync(assessment.Id, assessment.Revision,
            pending.PrivacyOwnerHumanId, CancellationToken.None);
    }
}
