using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Notifications;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Domain.Enums;
using NSubstitute;
using Category = Explore.Application.Notifications.NotificationCategory;

namespace Event.Application.UnitTests.Moderation;

public sealed class ModerationNoticeNativeMaterializerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    [Test]
    [Arguments(true, true)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(false, false)]
    public async Task Actual_native_graph_preserves_portal_pointer_without_unsafe_email(
        bool verified, bool enabled)
    {
        var userId = Guid.CreateVersion7();
        var tenantId = Guid.CreateVersion7();
        var user = new User
        {
            Id = userId,
            Pii = new() { UserId = userId, Email = "subject@example.test", FirstName = "", LastName = "" }
        };
        if (verified)
        {
            var claim = UserIdentityEmailClaim.Create(userId, "subject@example.test");
            claim.Evidence.Add(UserIdentityEmailEvidence.Create(userId, claim.Id, Guid.CreateVersion7(), Now.UtcDateTime));
            user.IdentityEmailClaims.Add(claim);
        }
        var effect = ModerationEffectIntent.Create(Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), 1, null, null, Guid.CreateVersion7(), 4, tenantId, 1, userId, 1, 1, 1,
            Now, null, null);
        var source = ModerationNoticeSource.Capture(effect, tenantId, "{\"Version\":1}", Now.AddYears(1));
        var route = Substitute.For<INotificationOwnershipResolver>();
        route.ResolveAsync(Arg.Any<NotificationIntentDraft>(), Arg.Any<CancellationToken>())
            .Returns(new NotificationOwnershipDecision(Category.TrustSafetyModeration, NotificationOwnership.IslamuEvent));
        var preferences = Substitute.For<INotificationPreferenceResolver>();
        preferences.ResolveAsync(Arg.Any<NotificationPreferenceResolveRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var request = call.Arg<NotificationPreferenceResolveRequest>()
                    ?? throw new InvalidOperationException("The preference request is required.");
                return new NotificationPreferenceDecision(request.CategoryCode, request.ChannelCode,
                    enabled, false, false, false, "user", null);
            });
        NotificationIntent? persisted = null;
        var repository = Substitute.For<IRecipientNotificationGraphRepository>();
        repository.CreateGraphAsync(Arg.Any<NotificationIntent>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var intent = call.Arg<NotificationIntent>()
                    ?? throw new InvalidOperationException("The materialized graph is required.");
                persisted = intent;
                return intent;
            });
        // Use the actual native graph builder. Only storage and current external policy reads are substituted.
        var builder = new RecipientNotificationMaterializer(repository,
            Substitute.For<IUnitOfWork>(), Substitute.For<ISettingMutationLock>());
        var service = new ModerationNoticeNativeMaterializer(route, preferences, builder);
        var noticeId = Guid.CreateVersion7();
        var result = await service.MaterializeAsync(source, noticeId, ModerationNoticeAudience.Subject, user, default);
        var notice = ModerationNotice.Create(noticeId, source, ModerationNoticeAudience.Subject, 1, userId,
            result.IntentId, result.Disposition);

        await Assert.That(persisted is not null).IsTrue();
        await Assert.That(notice.NativeIntentId).IsEqualTo(persisted!.Id);
        await Assert.That(persisted.RecipientUserId).IsEqualTo(userId);
        await Assert.That(persisted.TenantId).IsEqualTo(tenantId);
        await Assert.That(persisted.Deliveries.Count).IsEqualTo(2);
        var email = persisted.Deliveries.Single(row => row.ChannelId == (int)NotificationPreferenceChannelEnum.Email);
        var inApp = persisted.Deliveries.Single(row => row.ChannelId == (int)NotificationPreferenceChannelEnum.InApp);
        if (verified && enabled)
        {
            await Assert.That(email.StatusId).IsEqualTo((int)NotificationDeliveryStatusEnum.Queued);
            await Assert.That(email.EmailDispatchOutbox).IsNotNull();
            var dispatch = email.EmailDispatchOutbox!;
            await Assert.That(ModerationNoticeEmailTemplate.Matches(dispatch, persisted, noticeId)).IsTrue();
            await Assert.That(dispatch.RecipientEmail).IsEqualTo("subject@example.test");
            await Assert.That(dispatch.TenantId).IsEqualTo(tenantId);
            await Assert.That(dispatch.RecipientUserId).IsEqualTo(userId);
            await Assert.That(email.FailureCategory).IsNull();
            await Assert.That(result.Disposition).IsEqualTo(ModerationNoticeDeliveryDisposition.NativePortalEmailQueued);
        }
        else
        {
            await Assert.That(email.StatusId).IsEqualTo((int)NotificationDeliveryStatusEnum.Skipped);
            await Assert.That(email.EmailDispatchOutbox is null).IsTrue();
            await Assert.That(email.FailureCategory).IsEqualTo(!verified
                ? RecipientEmailAddressResolver.RecipientEmailUnverified : "email_preference_disabled");
        }
        await Assert.That(inApp.StatusId).IsEqualTo((int)(enabled
            ? NotificationDeliveryStatusEnum.Delivered : NotificationDeliveryStatusEnum.Skipped));
        await Assert.That(inApp.Notification?.Body is null).IsTrue();
        await Assert.That(effect.CompletedAt is null).IsTrue();
    }
}
