using Explore.Application.Notifications;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationPolicyPublicationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Scheduled_owner_escalation_commits_obligations_and_verifies_actual_native_delivery(bool initiallyDisabled)
    {
        await using var fixture = await RecognitionErasureFixture.CreateAsync(lawfulDeadline: TimeSpan.FromHours(1));
        await using var context = fixture.Database.Context();
        var repository = fixture.Repository(context);
        fixture.Keys.Available = false;
        await repository.ReceiveAsync(fixture.Request, fixture.ReceiptHash, Now.AddHours(2), CancellationToken.None);
        await repository.ComputeAsync(fixture.Request, CancellationToken.None);
        var pending = (await repository.FindAsync(fixture.Request.IntentId, CancellationToken.None))!;
        fixture.Clock.UtcNow = pending.EscalateAt;
        var settings = new RelationalSettingMutationLock(context, new EfCoreUnitOfWork(context));
        var materializer = new RecipientNotificationMaterializer(new NotificationIntentRepository(context, settings, fixture.Clock),
            new EfCoreUnitOfWork(context), settings, new PrivacyErasureStateRepository(context));
        ModerationRecognitionRecoveryScheduler Scheduler(NotificationOwnership owner) => new(
            repository, repository,
            new ModerationRecognitionRecoveryNotifications(new DefaultNotificationOwnershipResolver(
                Options.Create(new NotificationRoutingOptions { PlatformOperationsOwner = owner })), materializer),
            settings, fixture.Clock, fixture.KeyLifecycle(context));
        await Scheduler(initiallyDisabled ? NotificationOwnership.Disabled : NotificationOwnership.IslamuEvent)
            .ReconcileAsync(CancellationToken.None);
        var obligations = await context.Set<ModerationRecognitionEscalation>()
            .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure).OrderBy(row => row.Id).ToArrayAsync();
        await Assert.That(obligations.Length).IsEqualTo(2);
        await Assert.That(obligations.Select(row => row.RecipientHumanId).ToHashSet())
            .IsEquivalentTo(new[] { pending.PrivacyOwnerHumanId, pending.RecoveryOwnerHumanId });
        await Assert.That(obligations.All(row => row.OriginalDeadline == pending.SettleBy)).IsTrue();
        if (initiallyDisabled)
        {
            await Assert.That(obligations.All(row => row.NotificationId is null && row.DeliveredAt is null)).IsTrue();
            await Scheduler(NotificationOwnership.IslamuEvent).ReconcileAsync(CancellationToken.None);
        }
        var delivered = await context.Set<ModerationRecognitionEscalation>()
            .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure).ToArrayAsync();
        await Assert.That(delivered.All(row => row.NotificationId is not null && row.DeliveredAt is not null
            && row.AcknowledgedAt is null)).IsTrue();
        foreach (var escalation in delivered)
        {
            var notification = await context.Notifications.SingleAsync(row => row.Id == escalation.NotificationId);
            await Assert.That(notification.UserId).IsEqualTo(escalation.RecipientHumanId);
            var intent = await context.Set<NotificationIntent>()
                .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure)
                .SingleAsync(row => row.Id == notification.NotificationIntentId);
            await Assert.That(intent.SafePayloadReference).IsEqualTo($"recognition-recovery:{escalation.Id:D}");
            await Assert.That(await context.Set<NotificationDelivery>()
                .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure)
                .AnyAsync(row => row.NotificationId == notification.Id && row.NotificationIntentId == intent.Id
                    && row.IsRequired && row.ChannelId == (int)Explore.Domain.Enums.NotificationPreferenceChannelEnum.InApp
                    && row.StatusId == (int)Explore.Domain.Enums.NotificationDeliveryStatusEnum.Delivered)).IsTrue();
        }
        await Scheduler(NotificationOwnership.IslamuEvent).ReconcileAsync(CancellationToken.None);
        await Assert.That(await context.Set<ModerationRecognitionEscalation>()
            .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure).CountAsync()).IsEqualTo(2);
        await Assert.That((await repository.FindAsync(fixture.Request.IntentId, CancellationToken.None))!.AuthoritySequence).IsNull();
    }
}
