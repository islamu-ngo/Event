using System.Security.Claims;
using Explore.Application.Features.Moderation.RecognitionRecovery;
using Explore.Application.Notifications;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Infrastructure.Identity;
using Explore.Persistence;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationPolicyPublicationTests
{
    [Test]
    public async Task Native_CQS_uses_authenticated_independent_owners_and_preserves_protected_acceptance()
    {
        await using var fixture = await RecognitionErasureFixture.CreateAsync(lawfulDeadline: TimeSpan.FromHours(1));
        var originalReceipt = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(originalReceipt))
            .CopyTo(fixture.ReceiptHash, 0);
        await using var context = fixture.Database.Context();
        var repository = fixture.Repository(context);
        await repository.ReceiveAsync(fixture.Request, fixture.ReceiptHash, Now.AddHours(2), CancellationToken.None);
        fixture.Keys.Available = false;
        await repository.ComputeAsync(fixture.Request, CancellationToken.None);
        var pending = (await repository.FindAsync(fixture.Request.IntentId, CancellationToken.None))!;
        fixture.Clock.UtcNow = pending.EscalateAt;
        var adoption = await context.Set<ModerationRecognitionPrivacyAdoption>()
            .IgnoreAllFilters("Exact native operator command fixture").AsNoTracking()
            .SingleAsync(row => row.Id == fixture.AdoptionId);
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        var user = new UserContext(accessor);
        void Authenticate(Guid human) => accessor.HttpContext.User =
            new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", human.ToString("D"))], "Bearer"));
        var settings = new RelationalSettingMutationLock(context, new EfCoreUnitOfWork(context));
        var materializer = new RecipientNotificationMaterializer(new NotificationIntentRepository(context, settings, fixture.Clock),
            new EfCoreUnitOfWork(context), settings, new PrivacyErasureStateRepository(context));
        var notifications = new ModerationRecognitionRecoveryNotifications(new DefaultNotificationOwnershipResolver(
            Options.Create(new NotificationRoutingOptions { PlatformOperationsOwner = NotificationOwnership.IslamuEvent })), materializer);
        var recovery = new ModerationRecognitionRecovery(repository, user, settings, notifications);
        var assess = new AssessRecognitionRecoveryCommandHandler(recovery, user);
        var authorize = new AuthorizeRecognitionRecoveryCommandHandler(recovery, user);
        var command = new AssessRecognitionRecoveryCommand
        {
            OperationId = Guid.CreateVersion7(), IntentId = pending.IntentId, SourceId = adoption.SourceId,
            AdoptionId = adoption.Id, ExpectedPendingRevision = pending.Revision,
            ExpectedSourceRevision = adoption.SourceRevision, ExpectedSourceGeneration = adoption.SourceWriterGeneration,
            ExpectedAdoptionRevision = adoption.Revision, ExpectedAdoptionGeneration = adoption.WriterGeneration,
            Grounds = ModerationRecognitionDispositionGrounds.OriginalKeyRecoveryUnavailable,
            ValidUntil = fixture.Clock.UtcNow.AddMinutes(5)
        };
        await Assert.That((await assess.ExecuteAsync(command)).IsSuccess).IsFalse();
        Authenticate(pending.PrivacyOwnerHumanId);
        await Assert.That((await assess.ExecuteAsync(command)).IsSuccess).IsFalse();
        Authenticate(pending.RecoveryOwnerHumanId);
        await Assert.That((await assess.ExecuteAsync(command with { ExpectedPendingRevision = pending.Revision - 1 })).IsSuccess).IsFalse();
        await Assert.That((await assess.ExecuteAsync(command)).IsSuccess).IsTrue();
        var authorization = new AuthorizeRecognitionRecoveryCommand { Id = command.OperationId, ExpectedRevision = 1 };
        await Assert.That((await authorize.ExecuteAsync(authorization)).IsSuccess).IsFalse();
        Authenticate(pending.PrivacyOwnerHumanId);
        await Assert.That((await authorize.ExecuteAsync(authorization)).IsSuccess).IsTrue();
        await Assert.That(await context.Set<ModerationRecognitionDispositionAcceptance>()
            .IgnoreAllFilters("Exact independently accepted command receipts")
            .CountAsync(row => row.DispositionId == command.OperationId)).IsEqualTo(2);
        await Assert.That((await repository.FindAsync(pending.IntentId, CancellationToken.None))!.AuthoritySequence).IsNull();
        var receipt = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        // The original fixture hash is independent of this random credential; an intent GUID cannot bypass it.
        var correction = new GetRecognitionCorrectionProofsQueryHandler(
            new ModerationRecognitionCorrectionReceiptAuthenticator(repository), recovery);
        await Assert.That((await correction.QueryAsync(new(pending.IntentId, receipt))).IsSuccess).IsFalse();
        var protectedRead = await correction.QueryAsync(new(pending.IntentId, originalReceipt));
        await Assert.That(protectedRead.IsSuccess).IsTrue();
        await Assert.That(protectedRead.Data!.Single().Id).IsEqualTo(command.OperationId);
        var protectedEntity = (await repository.ReadRemediesAsync(fixture.ReceiptHash, CancellationToken.None)).Single();
        string serialized = System.Text.Json.JsonSerializer.Serialize(protectedRead.Data);
        await Assert.That(serialized).DoesNotContain(protectedEntity.GroundsReceipt);
        await Assert.That(serialized).DoesNotContain(protectedEntity.LegalMandateReceipt);
        await Assert.That(serialized).DoesNotContain(protectedEntity.CoverageReceipt);
        await Assert.That(serialized).DoesNotContain(protectedEntity.ObservedMaterialCommitment);
        await Assert.That((await repository.ReadRemediesAsync(fixture.ReceiptHash, CancellationToken.None)).Count).IsEqualTo(1);
        await recovery.EscalateAsync(pending.IntentId, null, CancellationToken.None);
        var notices = await recovery.ReadEscalationsAsync(null, 25, CancellationToken.None);
        var notice = notices.Single();
        var acknowledge = new AcknowledgeRecognitionRecoveryCommandHandler(recovery, user);
        Authenticate(pending.RecoveryOwnerHumanId);
        await Assert.That((await acknowledge.ExecuteAsync(new(notice.Id))).IsSuccess).IsFalse();
        Authenticate(pending.PrivacyOwnerHumanId);
        await Assert.That((await acknowledge.ExecuteAsync(new(notice.Id))).IsSuccess).IsTrue();
        var acknowledged = await recovery.ReadEscalationAsync(notice.Id, CancellationToken.None);
        await Assert.That(acknowledged!.DeliveredAt).IsNotNull();
        await Assert.That(acknowledged.AcknowledgedAt).IsNotNull();
        await fixture.WithdrawPrivacySourceAsync();
        await Assert.That((await correction.QueryAsync(new(pending.IntentId, originalReceipt))).IsSuccess).IsTrue();
    }
}
