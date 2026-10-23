using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Models;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationNoticeEmailTests
{
    [Test]
    public async Task OriginalContentEmailUsesTheQualifiedRepresentativeWithoutSubjectAcknowledgement()
    {
        await using var fixture = await EmailFixture.CreateAsync(content: true);
        var request = await fixture.AdmitAsync();
        var delivered = false;
        var result = await fixture.SendAsync(request, async (message, _) =>
        {
            delivered = true;
            await Assert.That(message.To).IsEqualTo("original-notice@example.invalid");
            await Assert.That(message.DisclosureUntilUtc).IsEqualTo(fixture.Notice.Clock.GetUtcNow().AddDays(1).UtcDateTime);
            return EmailResult.Ok();
        });
        await Assert.That(result.Success).IsTrue();
        await Assert.That(delivered).IsTrue();
        await using var context = fixture.Notice.Database.Context();
        context.ModerationTrustDomainId = fixture.Notice.Database.Scope.TrustDomainId;
        await Assert.That((await context.ModerationNoticeSources.SingleAsync()).SubjectKind)
            .IsEqualTo((int)ISLAMU.Moderation.Domain.ModerationSubjectKind.Content);
        await Assert.That(await context.Set<ModerationNoticeNotificationReceipt>().CountAsync()).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ChangedOriginalContentAssignmentCannotReachSmtpOrBorrowItsReplacement(bool replacement)
    {
        await using var fixture = await EmailFixture.CreateAsync(content: true);
        var request = await fixture.AdmitAsync();
        await fixture.Notice.MutateAsync(async context =>
        {
            var original = await context.EventRoleAssignments.SingleAsync();
            original.Revoke(fixture.Notice.Database.AdministratorId, fixture.Notice.Clock.GetUtcNow().UtcDateTime);
            if (replacement)
                context.EventRoleAssignments.Add(EventRoleAssignment.Create(fixture.Notice.Database.TenantId,
                    original.EventId, original.UserId, (int)RoleEnum.EventManager, EventRoleAssignmentStatus.Active,
                    fixture.Notice.Clock.GetUtcNow().UtcDateTime, fixture.Notice.Clock.GetUtcNow().AddDays(1).UtcDateTime,
                    fixture.Notice.Database.AdministratorId));
        });
        var result = await fixture.SendAsync(request, (_, _) =>
            throw new InvalidOperationException("Revoked original Content custody reached SMTP."));
        await Assert.That(result.Outcome).IsEqualTo(SmtpDeliveryOutcome.AuthorityRejected);
    }

    [Test]
    public async Task OriginalProducerQueuesOnlyPointerWithoutAcknowledgingTheSubject()
    {
        await using var fixture = await EmailFixture.CreateAsync();
        await using var context = fixture.Notice.Database.Context();
        var graph = await context.NotificationIntents.AsNoTracking().Include(row => row.Deliveries)
            .ThenInclude(row => row.EmailDispatchOutbox).SingleAsync();
        var dispatch = graph.Deliveries.Single(row => row.ChannelId == (int)NotificationPreferenceChannelEnum.Email)
            .EmailDispatchOutbox!;
        await Assert.That(dispatch.Kind).IsEqualTo(EmailDispatchKind.AccountModerationOriginalNotice);
        await Assert.That(dispatch.SourceId).IsEqualTo(fixture.Notice.SubjectNoticeId);
        await Assert.That(dispatch.PlainTextBody).DoesNotContain(fixture.Notice.Approval.RuleMeaning);
        await Assert.That(dispatch.PlainTextBody).DoesNotContain(fixture.Notice.PrivateJustification);
        context.ModerationTrustDomainId = fixture.Notice.Database.Scope.TrustDomainId;
        await Assert.That(await context.Set<ModerationNoticeNotificationReceipt>().CountAsync()).IsEqualTo(0);
        await Assert.That((await context.ModerationNoticeSources.SingleAsync()).RetainUntil)
            .IsEqualTo(fixture.Notice.Approval.RetainUntil);
    }

    [Test]
    public async Task ActualFinalAuthorityUsesCurrentVerifiedAddressAndNeverAcknowledges()
    {
        await using var fixture = await EmailFixture.CreateAsync();
        var request = await fixture.AdmitAsync();
        await fixture.ChangeAddressAsync();
        var delivered = 0;
        var result = await fixture.SendAsync(request, async (content, _) =>
        {
            delivered++;
            await Assert.That(content.To).IsEqualTo("current-notice@example.invalid");
            await Assert.That(content.DisclosureUntilUtc).IsEqualTo(fixture.Notice.Approval.RetainUntil.UtcDateTime);
            return EmailResult.Ok();
        });
        await Assert.That(result.Success).IsTrue();
        await Assert.That(delivered).IsEqualTo(1);
        await using var context = fixture.Notice.Database.Context();
        await new EmailDispatchOutboxRepository(context).SettleProviderAccepted(new(
            request.TenantId, request.OutboxId, request.ProcessingLeaseToken, request.AttemptNumber,
            fixture.Notice.Clock.GetUtcNow().UtcDateTime, null), CancellationToken.None);
        await Assert.That((await context.EmailDispatchOutbox.AsNoTracking().SingleAsync()).Status)
            .IsEqualTo(EmailDispatchStatus.Sent);
        context.ModerationTrustDomainId = fixture.Notice.Database.Scope.TrustDomainId;
        await Assert.That(await context.Set<ModerationNoticeNotificationReceipt>().CountAsync()).IsEqualTo(0);
    }

    [Test]
    [Arguments("deadline")]
    [Arguments("source")]
    [Arguments("approval")]
    [Arguments("recipient")]
    [Arguments("preference")]
    [Arguments("email")]
    [Arguments("tenant")]
    [Arguments("hash")]
    [Arguments("template")]
    [Arguments("pointer")]
    [Arguments("kind")]
    public async Task ChangeAfterQueuedAdmissionCannotReachTheTransport(string change)
    {
        await using var fixture = await EmailFixture.CreateAsync();
        var request = await fixture.AdmitAsync();
        await fixture.InvalidateAsync(change);
        var delivered = false;
        var result = await fixture.SendAsync(request, (_, _) =>
        {
            delivered = true;
            return Task.FromResult(EmailResult.Ok());
        });
        await Assert.That(result.Outcome).IsEqualTo(SmtpDeliveryOutcome.AuthorityRejected);
        await Assert.That(delivered).IsFalse();
    }

    [Test]
    public async Task MutedEmailLeavesTheOriginalPortalHonestWithoutAnotherDeliveryKind()
    {
        await using var fixture = await EmailFixture.CreateAsync(muted: true);
        await using var context = fixture.Notice.Database.Context();
        await Assert.That(await context.EmailDispatchOutbox.CountAsync()).IsEqualTo(0);
        var delivery = await context.NotificationDeliveries.SingleAsync(
            row => row.ChannelId == (int)NotificationPreferenceChannelEnum.Email);
        await Assert.That(delivery.StatusId).IsEqualTo((int)NotificationDeliveryStatusEnum.Skipped);
        context.ModerationTrustDomainId = fixture.Notice.Database.Scope.TrustDomainId;
        var notice = await context.ModerationNotices.SingleAsync(row => row.Id == fixture.Notice.SubjectNoticeId);
        await Assert.That(notice.DeliveryDisposition).IsEqualTo(ModerationNoticeDeliveryDisposition.NativePortalEmailUnavailable);
        await Assert.That(await context.Set<ModerationNoticeNotificationReceipt>().CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task OriginalReviewerEmailRequiresCurrentIndependentQualification()
    {
        await using var fixture = await EmailFixture.CreateAsync(reviewer: true);
        var request = await fixture.AdmitAsync();
        await fixture.Notice.MutateAsync(async context =>
            context.PlatformUserRoles.Remove(await context.PlatformUserRoles.SingleAsync(
                row => row.UserId == fixture.Notice.Database.ReviewerId)));
        var result = await fixture.SendAsync(request, (_, _) =>
            throw new InvalidOperationException("An unqualified original reviewer reached SMTP."));
        await Assert.That(result.Outcome).IsEqualTo(SmtpDeliveryOutcome.AuthorityRejected);
    }

    [Test]
    public async Task ReversingTheFindingDoesNotReplaceTheOriginalNoticeOrRecipient()
    {
        await using var fixture = await EmailFixture.CreateAsync();
        var request = await fixture.AdmitAsync();
        await fixture.Notice.MutateAsync(async context =>
        {
            var finding = await context.ModerationFindings.SingleAsync();
            finding.Reverse(Guid.CreateVersion7(), ISLAMU.Moderation.Domain.SubjectReference.Create(
                ISLAMU.Moderation.Domain.ModerationSubjectKind.Human,
                fixture.Notice.Database.UrgencyAssessorId, fixture.Notice.Database.Instance),
                "Independent correction.", finding.Revision, fixture.Notice.Clock.GetUtcNow());
        });
        var result = await fixture.SendAsync(request, async (content, _) =>
        {
            await Assert.That(content.To).IsEqualTo("original-notice@example.invalid");
            await Assert.That(content.ModerationNoticeWindow!.UnacknowledgedCalendarMonths).IsEqualTo(6);
            await Assert.That(content.ModerationNoticeWindow.Allows(new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero))).IsTrue();
            await Assert.That(content.ModerationNoticeWindow.Allows(new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero).AddTicks(1))).IsFalse();
            return EmailResult.Ok();
        });
        await Assert.That(result.Success).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RetainedErasureFactBlocksOldRecipientBeforeThePrimarySagaExists(bool content)
    {
        await using var fixture = await EmailFixture.CreateAsync(content: content);
        var request = await fixture.AdmitAsync();
        await using var context = fixture.Notice.Database.Context();
        await fixture.Authority(context).AppendAsync(new(Guid.CreateVersion7(), PrivacyErasureSubjectKind.User,
            fixture.Notice.Database.SubjectId, PrivacyErasureReasonCode.AccountDeletion, 1));
        await Assert.That(await context.PrivacyErasureSagas.AnyAsync()).IsFalse();
        var result = await fixture.SendAsync(request, (_, _) =>
            throw new InvalidOperationException("An erased original recipient reached SMTP."));
        await Assert.That(result.Outcome).IsEqualTo(SmtpDeliveryOutcome.AuthorityRejected);
    }

    [Test]
    public async Task RealTransientRetryAtRetentionCeilingIsSkippedBeforeHandoff()
    {
        await using var fixture = await EmailFixture.CreateAsync();
        var first = await fixture.AdmitAsync();
        await using var context = fixture.Notice.Database.Context();
        var repository = new EmailDispatchOutboxRepository(context);
        var failed = await repository.SettleProviderFailure(new(
            first.TenantId, first.OutboxId, first.ProcessingLeaseToken, first.AttemptNumber,
            SmtpDeliveryOutcome.TransientFailure, TimeSpan.FromMinutes(1), 5,
            fixture.Notice.Clock.GetUtcNow().UtcDateTime), CancellationToken.None);
        await Assert.That(failed).IsEqualTo(EmailDispatchFailureSettlementOutcome.RetryScheduled);
        fixture.Notice.Clock.UtcNow = fixture.Notice.Approval.RetainUntil;
        var row = await context.EmailDispatchOutbox.AsNoTracking().SingleAsync();
        var lease = Guid.CreateVersion7();
        var claim = await repository.TryClaimSpecificAsync(new(
            first.TenantId, row.PublishEventId, lease, 10, 5, 100, 50, fixture.Notice.Clock.GetUtcNow().UtcDateTime),
            CancellationToken.None);
        await Assert.That(claim).IsNotNull();
        var admission = await EmailDispatchSqliteFixture.CreateEvaluator(context).EvaluateAndBeginProviderHandoffAsync(
            new(first.TenantId, first.OutboxId, lease, claim!.AttemptCount, 60, 60,
                "original-notice-retry-witness", fixture.Notice.Clock.GetUtcNow().UtcDateTime));
        await Assert.That(admission.Outcome).IsEqualTo(Explore.Application.Contracts.Notifications.EmailDispatchEligibilityOutcome.Skipped);
        await Assert.That((await context.EmailDispatchOutbox.AsNoTracking().SingleAsync()).NextAttemptAt).IsNull();
        context.ModerationTrustDomainId = fixture.Notice.Database.Scope.TrustDomainId;
        await Assert.That(await context.Set<ModerationNoticeNotificationReceipt>().CountAsync()).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExpiredNativeCleanupRemovesDispatchAndItsReceiptWithoutResurrection(bool content)
    {
        await using var fixture = await EmailFixture.CreateAsync(content: content);
        var admitted = await fixture.AdmitAsync();
        fixture.Notice.Clock.UtcNow = fixture.Notice.Approval.RetainUntil;
        await using (var context = fixture.Notice.Database.Context())
        {
            context.ModerationTrustDomainId = fixture.Notice.Database.Scope.TrustDomainId;
            await using var transaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            var notice = await context.ModerationNotices.SingleAsync(row => row.Id == fixture.Notice.SubjectNoticeId);
            var owner = new NotificationIntentRepository(context,
                new RelationalSettingMutationLock(context, new EfCoreUnitOfWork(context)), fixture.Notice.Clock);
            var request = new ModerationNoticeNotificationCleanupRequest(transaction.TransactionId,
                fixture.Notice.Database.HostId, fixture.Notice.Database.Scope.TrustDomainId,
                fixture.Notice.Database.TenantId, notice.EffectId, fixture.Notice.Approval.RetainUntil,
                [new(notice.Id, notice.NativeIntentId!.Value, notice.RecipientSubjectId, (int)notice.Audience)]);
            context.Remove(notice);
            await Assert.That(await owner.StageAsync(request, CancellationToken.None)).IsTrue();
            await context.SaveChangesAsync();
            await Assert.That(await owner.VerifyRemovedAsync(request, CancellationToken.None)).IsTrue();
            await Assert.That(await context.EmailDispatchAttempts.CountAsync()).IsEqualTo(0);
            await Assert.That(await context.EmailDispatchReceipts.CountAsync()).IsEqualTo(0);
            await transaction.CommitAsync();
        }
        var result = await fixture.SendAsync(admitted, (_, _) =>
            throw new InvalidOperationException("A deleted original pointer reached SMTP."));
        await Assert.That(result.Outcome).IsEqualTo(SmtpDeliveryOutcome.AuthorityRejected);
    }

    [Test]
    public async Task DefiniteNoSendSettlesSkippedAndCannotAutomaticallyRetry()
    {
        await using var fixture = await EmailFixture.CreateAsync();
        var request = await fixture.AdmitAsync();
        fixture.Notice.Clock.UtcNow = fixture.Notice.Approval.RetainUntil;
        var result = await fixture.SendAsync(request, (_, _) => throw new InvalidOperationException("Expired notice reached SMTP."));
        await using var context = fixture.Notice.Database.Context();
        var repository = new EmailDispatchOutboxRepository(context);
        var settlement = await repository.SettleProviderFailure(new(
            request.TenantId, request.OutboxId, request.ProcessingLeaseToken, request.AttemptNumber,
            result.Outcome, TimeSpan.Zero, 5, fixture.Notice.Clock.GetUtcNow().UtcDateTime), CancellationToken.None);
        await Assert.That(settlement).IsEqualTo(EmailDispatchFailureSettlementOutcome.Skipped);
        var row = await context.EmailDispatchOutbox.AsNoTracking().SingleAsync();
        await Assert.That(row.Status).IsEqualTo(EmailDispatchStatus.Skipped);
        await Assert.That(row.NextAttemptAt).IsNull();
        await Assert.That(row.SentAt).IsNull();
        await Assert.That((await context.EmailDispatchAttempts.SingleAsync()).Outcome)
            .IsEqualTo(EmailDispatchAttemptOutcome.Skipped);
    }

    [Test]
    public async Task OriginalGraphCannotBeRepairedAtTheRetentionCeiling()
    {
        await using var fixture = await EmailFixture.CreateAsync();
        fixture.Notice.Clock.UtcNow = fixture.Notice.Approval.RetainUntil;
        await using var context = fixture.Notice.Database.Context();
        context.ModerationTrustDomainId = fixture.Notice.Database.Scope.TrustDomainId;
        var graph = await context.NotificationIntents.Include(row => row.Deliveries).SingleAsync();
        var owner = new NotificationIntentRepository(context,
            new RelationalSettingMutationLock(context, new EfCoreUnitOfWork(context)), fixture.Notice.Clock);
        await using var transaction = await context.Database.BeginTransactionAsync();
        await Assert.That(async () => await owner.RepairMissingRecipientDeliveryRowsAsync(
            graph, graph.Deliveries.ToArray(), null, null, CancellationToken.None))
            .Throws<InvalidOperationException>();
    }
}
