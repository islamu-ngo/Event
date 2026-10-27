using Explore.Application.Contracts.Persistence;
using Explore.Application.Notifications;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationCaseNativeProofTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NativePreFindingIssueCreatesRequiredPointerAndExactRetryNeverRenewsIt(bool muted)
    {
        await using var fixture = await PreFindingFixture.CreateAsync(issueNotice: false);
        if (muted)
        {
            await using var preferences = fixture.Database.Context();
            await new NotificationPreferenceProfileRepository(preferences).UpsertUserMuteAsync(
                fixture.Database.TenantId, fixture.Database.CallerId, true);
        }
        var issued = await fixture.ExecuteAsync(fixture.Review.Caller.UserId!.Value, fixture.Issue);
        await Assert.That(issued.Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        await using var inspect = fixture.Database.Context();
        var notice = await inspect.Set<ModerationPreFindingNotice>().AsNoTracking().SingleAsync();
        var intent = await inspect.NotificationIntents.AsNoTracking().SingleAsync();
        var notification = await inspect.Notifications.AsNoTracking().SingleAsync();
        var delivery = await inspect.NotificationDeliveries.AsNoTracking().SingleAsync();
        await Assert.That(intent.TemplateKey).IsEqualTo(ModerationPreFindingNoticeTemplate.Key);
        await Assert.That(intent.SafePayloadReference).IsEqualTo(ModerationPreFindingNoticeTemplate.Reference(notice.Id));
        await Assert.That(intent.DeduplicationKey).IsEqualTo(
            ModerationPreFindingNoticeTemplate.DeduplicationKey(notice.Id, notice.RecipientHumanId));
        await Assert.That(intent.TenantId).IsEqualTo(notice.TenantId);
        await Assert.That(intent.RecipientUserId).IsEqualTo(notice.RecipientHumanId);
        await Assert.That(intent.RecipientKindId).IsEqualTo((int)NotificationRecipientKindEnum.User);
        await Assert.That(intent.OwnershipTypeId).IsEqualTo((int)NotificationOwnershipTypeEnum.IslamuEvent);
        await Assert.That(intent.SafePayloadHash).IsNull();
        await Assert.That(intent.CorrelationId).IsNull();
        await Assert.That(intent.EventId).IsNull();
        await Assert.That(intent.CreatedAt).IsEqualTo(notice.IssuedAt.UtcDateTime);
        await Assert.That(notification.NotificationIntentId).IsEqualTo(intent.Id);
        await Assert.That(notification.UserId).IsEqualTo(notice.RecipientHumanId);
        await Assert.That(notification.TenantId).IsEqualTo(notice.TenantId);
        await Assert.That(notification.Title).IsEqualTo(ModerationPreFindingNoticeTemplate.Title);
        await Assert.That(notification.Body).IsNull();
        await Assert.That(notification.EntityId).IsEqualTo(notice.Id.ToString("D"));
        await Assert.That(notification.NotificationTypeId).IsEqualTo((int)NotificationTypeEnum.General);
        await Assert.That(notification.NotificationScopeId).IsEqualTo((int)ActorTypeEnum.User);
        await Assert.That(notification.NotificationReasonId).IsEqualTo((int)NotificationReasonEnum.System);
        await Assert.That(notification.CreatedAt).IsEqualTo(notice.IssuedAt.UtcDateTime);
        await Assert.That(delivery.NotificationIntentId).IsEqualTo(intent.Id);
        await Assert.That(delivery.NotificationId).IsEqualTo(notification.Id);
        await Assert.That(delivery.ChannelId).IsEqualTo((int)NotificationPreferenceChannelEnum.InApp);
        await Assert.That(delivery.IsRequired).IsTrue();
        await Assert.That(delivery.DeliveryPolicyId).IsEqualTo((int)NotificationDeliveryPolicyEnum.ModerationAvailabilityRequired);
        await Assert.That(delivery.StatusId).IsEqualTo((int)NotificationDeliveryStatusEnum.Delivered);
        await Assert.That(delivery.DisclosureLevel).IsEqualTo("moderation_pre_finding_pointer");
        await Assert.That(delivery.PolicyVersion).IsEqualTo(1);
        await Assert.That(delivery.TemplateVersion).IsEqualTo(1);
        await Assert.That(delivery.LinkAllowed).IsFalse();
        await Assert.That(delivery.EmailDispatchOutboxId).IsNull();
        await Assert.That(await inspect.EmailDispatchOutbox.AnyAsync()).IsFalse();
        await Assert.That(notice.AcknowledgedAt).IsNull();
        await Assert.That(notice.ResponseId).IsNull();
        await Assert.That(notice.RetainUntil).IsEqualTo(fixture.Until);

        fixture.Database.Clock.Now = fixture.Database.Clock.Now.AddSeconds(1);
        await Assert.That(await fixture.ExecuteAsync(fixture.Review.Caller.UserId!.Value, fixture.Issue)).IsEqualTo(issued);
        var retriedNotice = await inspect.Set<ModerationPreFindingNotice>().AsNoTracking().SingleAsync();
        await Assert.That(retriedNotice.IssuedAt).IsEqualTo(notice.IssuedAt);
        await Assert.That(retriedNotice.RetainUntil).IsEqualTo(notice.RetainUntil);
        await Assert.That(retriedNotice.Revision).IsEqualTo(1L);
        await Assert.That(retriedNotice.AcknowledgedAt).IsNull();
        await Assert.That(await inspect.NotificationIntents.Select(row => row.Id).SingleAsync()).IsEqualTo(intent.Id);
        await Assert.That(await inspect.Notifications.Select(row => row.Id).SingleAsync()).IsEqualTo(notification.Id);
        await Assert.That(await inspect.NotificationDeliveries.Select(row => row.Id).SingleAsync()).IsEqualTo(delivery.Id);
        await Assert.That(await inspect.Set<ModerationPreFindingReceipt>().CountAsync(row => row.Id == fixture.NoticeId)).IsEqualTo(1);
    }

    [Test]
    public async Task NativePreFindingIssueWithoutNativeMaterializerFailsUnavailableBeforeCapture()
    {
        await using var fixture = await PreFindingFixture.CreateAsync(issueNotice: false);
        await using var context = fixture.Database.Context();
        var result = await fixture.Repository(context, fixture.Review.Caller.UserId!.Value, includeNativeNotices: false)
            .ExecutePreFindingAsync(fixture.Issue, default);
        await Assert.That(result.Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Unavailable);
        await Assert.That(await context.Set<ModerationPreFindingNotice>().AnyAsync()).IsFalse();
        await Assert.That(await context.Set<ModerationPreFindingReceipt>().AnyAsync(row => row.Id == fixture.NoticeId)).IsFalse();
        await Assert.That(await context.NotificationIntents.AnyAsync()).IsFalse();
    }

    [Test]
    public async Task NativePreFindingResponseDoesNotRequireIssuingMaterializer()
    {
        await using var fixture = await PreFindingFixture.CreateAsync();
        await using var context = fixture.Database.Context();
        var result = await fixture.Repository(context, fixture.Database.CallerId, includeNativeNotices: false)
            .ExecutePreFindingAsync(fixture.Response(), default);
        await Assert.That(result.Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        await Assert.That(await context.NotificationIntents.CountAsync()).IsEqualTo(1);
        await Assert.That((await context.Set<ModerationPreFindingNotice>().SingleAsync()).ResponseId).IsNotNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NativePreFindingIssueCannotBorrowExternalOrDisabledRouting(bool external)
    {
        await using var fixture = await PreFindingFixture.CreateAsync(issueNotice: false);
        await using var context = fixture.Database.Context();
        var routing = new NotificationRoutingOptions
        {
            TrustSafetyModerationOwner = external ? NotificationOwnership.ExternalWorkflowProvider : NotificationOwnership.Disabled,
            AllowExternalUserFacingModerationEmails = external,
            ExternalUserFacingModerationProvider = external ? ExternalWorkflowProviderKind.Other : ExternalWorkflowProviderKind.None
        };
        var result = await fixture.Repository(context, fixture.Review.Caller.UserId!.Value, routing: routing)
            .ExecutePreFindingAsync(fixture.Issue, default);
        await Assert.That(result.Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Unavailable);
        await Assert.That(await context.Set<ModerationPreFindingNotice>().AnyAsync()).IsFalse();
        await Assert.That(await context.Set<ModerationPreFindingReceipt>().AnyAsync(row => row.Id == fixture.NoticeId)).IsFalse();
        await Assert.That(await context.NotificationIntents.AnyAsync()).IsFalse();
        await Assert.That(await context.Notifications.AnyAsync()).IsFalse();
        await Assert.That(await context.NotificationDeliveries.AnyAsync()).IsFalse();
        await Assert.That(await context.EmailDispatchOutbox.AnyAsync()).IsFalse();
        await Assert.That((await context.ModerationCases.SingleAsync(row => row.Id == fixture.Case.Id)).SubjectKind)
            .IsEqualTo(ISLAMU.Moderation.Domain.ModerationSubjectKind.Content);
    }

    [Test]
    [Arguments("delivery")]
    [Arguments("notification")]
    [Arguments("recipient")]
    public async Task NativePreFindingExactRetryDeniesMissingOrSubstitutedGraphWithoutRepair(string loss)
    {
        await using var fixture = await PreFindingFixture.CreateAsync();
        await using var mutate = fixture.Database.Context();
        var original = await mutate.Set<ModerationPreFindingNotice>().AsNoTracking().SingleAsync();
        if (loss == "delivery")
            await mutate.NotificationDeliveries.ExecuteDeleteAsync();
        else if (loss == "notification")
        {
            await mutate.NotificationDeliveries.ExecuteDeleteAsync();
            await mutate.Notifications.ExecuteDeleteAsync();
        }
        else
            await mutate.Notifications.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.UserId, fixture.Assessor));
        fixture.Database.Clock.Now = fixture.Database.Clock.Now.AddSeconds(1);
        var result = await fixture.ExecuteAsync(fixture.Review.Caller.UserId!.Value, fixture.Issue);
        await Assert.That(result.Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Unavailable);
        var retained = await mutate.Set<ModerationPreFindingNotice>().AsNoTracking().SingleAsync();
        await Assert.That(retained.IssuedAt).IsEqualTo(original.IssuedAt);
        await Assert.That(retained.RetainUntil).IsEqualTo(original.RetainUntil);
        await Assert.That(retained.Revision).IsEqualTo(original.Revision);
        await Assert.That(retained.AcknowledgedAt).IsNull();
        await Assert.That(await mutate.NotificationIntents.CountAsync()).IsEqualTo(1);
        await Assert.That(await mutate.NotificationDeliveries.CountAsync()).IsEqualTo(loss == "recipient" ? 1 : 0);
        await Assert.That(await mutate.Notifications.CountAsync()).IsEqualTo(loss == "notification" ? 0 : 1);
        if (loss == "recipient")
            await Assert.That(await mutate.Notifications.Select(row => row.UserId).SingleAsync()).IsEqualTo(fixture.Assessor);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task NativePreFindingIssueRollsBackActualGraphOrReceiptFlushLoss(bool afterReceipt, bool recipientLoss)
    {
        await using var fixture = await PreFindingFixture.CreateAsync(issueNotice: false);
        var flush = new PreFindingPointerFlush(fixture, afterReceipt, recipientLoss);
        await using var context = fixture.Database.Context(flush);
        var result = await fixture.Repository(context, fixture.Review.Caller.UserId!.Value)
            .ExecutePreFindingAsync(fixture.Issue, default);
        await Assert.That(flush.ObservedFlushes).IsEqualTo(1);
        await Assert.That(result.Outcome).IsNotEqualTo(ModerationQualificationWriteOutcome.Known);
        await using var inspect = fixture.Database.Context();
        await Assert.That(await inspect.Set<ModerationPreFindingNotice>().AnyAsync()).IsFalse();
        await Assert.That(await inspect.Set<ModerationPreFindingReceipt>().AnyAsync(row => row.Id == fixture.NoticeId)).IsFalse();
        await Assert.That(await inspect.NotificationIntents.AnyAsync()).IsFalse();
        await Assert.That(await inspect.NotificationDeliveries.AnyAsync()).IsFalse();
        await Assert.That(await inspect.Notifications.AnyAsync()).IsFalse();
        await Assert.That(await inspect.EmailDispatchOutbox.AnyAsync()).IsFalse();
        await Assert.That(await inspect.EventRoleAssignments.Where(row => row.Id == fixture.Database.RoleId)
            .Select(row => row.Version).SingleAsync()).IsEqualTo(7L);
    }

    private sealed class PreFindingPointerFlush(PreFindingFixture fixture, bool afterReceipt, bool recipientLoss)
        : SaveChangesInterceptor
    {
        public int ObservedFlushes { get; private set; }

        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken token = default)
        {
            var context = (ExploreDbContext)eventData.Context!;
            if (ObservedFlushes != 0 || context.ChangeTracker.HasChanges()
                || !await context.Set<ModerationPreFindingNotice>().AsNoTracking()
                    .AnyAsync(row => row.Id == fixture.NoticeId, token))
                return result;
            var receiptExists = await context.Set<ModerationPreFindingReceipt>().AsNoTracking()
                .AnyAsync(row => row.Id == fixture.NoticeId, token);
            if (receiptExists != afterReceipt) return result;
            var intent = await context.NotificationIntents.AsNoTracking().SingleAsync(token);
            var delivery = await context.NotificationDeliveries.AsNoTracking().SingleAsync(token);
            var notification = await context.Notifications.AsNoTracking().SingleAsync(token);
            await Assert.That(context.Database.CurrentTransaction).IsNotNull();
            await Assert.That(intent.RecipientUserId).IsEqualTo(fixture.Database.CallerId);
            await Assert.That(delivery.NotificationId).IsEqualTo(notification.Id);
            await Assert.That(delivery.IsRequired).IsTrue();
            await Assert.That(delivery.StatusId).IsEqualTo((int)NotificationDeliveryStatusEnum.Delivered);
            ObservedFlushes++;
            if (recipientLoss)
                await Assert.That(await context.EventRoleAssignments.Where(row => row.Id == fixture.Database.RoleId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Version, row => row.Version + 1), token))
                    .IsEqualTo(1);
            else
                await Assert.That(await context.NotificationDeliveries.Where(row => row.Id == delivery.Id)
                    .ExecuteDeleteAsync(token)).IsEqualTo(1);
            return result;
        }
    }
}
