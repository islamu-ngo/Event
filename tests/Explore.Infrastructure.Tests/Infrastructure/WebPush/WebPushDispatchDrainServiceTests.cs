using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Models;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.WebPush;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure.WebPush;

public sealed class WebPushDispatchDrainServiceTests
{
    [Test]
    public async Task ProcessBatchAsync_WhenPreferenceAllowsDelivery_SendsGenericPayloadAndMarksDelivered()
    {
        var fixture = new Fixture();
        var dispatch = CreateDispatch(NotificationPreferenceCategoryCodes.EventUpdates);
        var subscription = CreateSubscription(dispatch);
        WebPushSendEnvelope? sentRequest = null;
        fixture.DispatchRepository.GetPendingBatch(Arg.Any<int>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns([dispatch]);
        fixture.DispatchRepository.TryMarkAsProcessing(dispatch.Id, Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(true);
        fixture.ConfigureActiveClaim(dispatch);
        fixture.DispatchRepository.MarkAsDelivered(dispatch.Id, Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(true);
        fixture.SubscriptionRepository.GetActiveByIdAsync(dispatch.TenantId, dispatch.SubscriptionId, Arg.Any<CancellationToken>()).Returns(subscription);
        fixture.Sender.SendAsync(Arg.Do<WebPushSendEnvelope>(request => sentRequest = request), Arg.Any<CancellationToken>())
            .Returns(WebPushSendResult.Succeeded(201));

        WebPushDispatchDrainResult result = await fixture.Service.ProcessBatchAsync(CancellationToken.None);

        await Assert.That(result.Delivered).IsEqualTo(1);
        await Assert.That(sentRequest).IsNotNull();
        await Assert.That(sentRequest!.PayloadJson).Contains("notification_refresh");
        await Assert.That(sentRequest.PayloadJson).DoesNotContain("<script>");
        await Assert.That(sentRequest.TimeToLiveSeconds).IsGreaterThanOrEqualTo(21590);
        await Assert.That(sentRequest.TimeToLiveSeconds).IsLessThanOrEqualTo(21600);
        await Assert.That(sentRequest.Topic).IsEqualTo("event-updates");
        await Assert.That(sentRequest.Urgency).IsEqualTo(WebPushUrgency.Normal);
        await fixture.PreferenceResolver.Received(1).ResolveAsync(
            Arg.Is<NotificationPreferenceResolveRequest>(request =>
                request.TenantId == dispatch.TenantId &&
                request.UserId == dispatch.UserId &&
                request.CategoryCode == NotificationPreferenceCategoryCodes.EventUpdates &&
                request.ChannelCode == NotificationPreferenceChannelCodes.Push),
            Arg.Any<CancellationToken>());
        await fixture.DispatchRepository.Received(1).MarkAsDelivered(
            dispatch.Id,
            Arg.Any<Guid>(),
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ProcessBatchAsync_WhenPreferenceDisablesPush_SkipsBeforeProviderSend()
    {
        var fixture = new Fixture(enabled: false);
        var dispatch = CreateDispatch(NotificationPreferenceCategoryCodes.Marketing);
        fixture.DispatchRepository.GetPendingBatch(Arg.Any<int>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns([dispatch]);
        fixture.DispatchRepository.TryMarkAsProcessing(dispatch.Id, Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(true);
        fixture.ConfigureActiveClaim(dispatch);
        fixture.DispatchRepository.MarkAsSkipped(dispatch.Id, Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(true);

        WebPushDispatchDrainResult result = await fixture.Service.ProcessBatchAsync(CancellationToken.None);

        await Assert.That(result.Skipped).IsEqualTo(1);
        await fixture.Sender.DidNotReceiveWithAnyArgs().SendAsync(default!, default);
        await fixture.DispatchRepository.Received(1).MarkAsSkipped(
            dispatch.Id,
            Arg.Any<Guid>(),
            "recipient_notification_preference_disabled",
            Arg.Is<string>(message => message.Contains("disabled", StringComparison.OrdinalIgnoreCase)),
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ProcessBatchAsync_WhenProviderReportsGone_PermanentlyFailsAndDeactivatesSubscription()
    {
        var fixture = new Fixture();
        var dispatch = CreateDispatch(NotificationPreferenceCategoryCodes.EventUpdates);
        fixture.DispatchRepository.GetPendingBatch(Arg.Any<int>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns([dispatch]);
        fixture.DispatchRepository.TryMarkAsProcessing(dispatch.Id, Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(true);
        fixture.ConfigureActiveClaim(dispatch);
        fixture.DispatchRepository.MarkPermanentFailureAndDeactivateSubscription(
            dispatch.TenantId,
            dispatch.Id,
            Arg.Any<Guid>(),
            dispatch.SubscriptionId,
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>()).Returns(true);
        fixture.SubscriptionRepository.GetActiveByIdAsync(dispatch.TenantId, dispatch.SubscriptionId, Arg.Any<CancellationToken>())
            .Returns(CreateSubscription(dispatch));
        fixture.Sender.SendAsync(Arg.Any<WebPushSendEnvelope>(), Arg.Any<CancellationToken>())
            .Returns(WebPushSendResult.StaleSubscription(410, "gone"));

        WebPushDispatchDrainResult result = await fixture.Service.ProcessBatchAsync(CancellationToken.None);

        await Assert.That(result.PermanentFailed).IsEqualTo(1);
        await fixture.DispatchRepository.Received(1).MarkPermanentFailureAndDeactivateSubscription(
            dispatch.TenantId,
            dispatch.Id,
            Arg.Any<Guid>(),
            dispatch.SubscriptionId,
            "web_push_subscription_stale",
            Arg.Is<string>(message => message.Contains("410", StringComparison.Ordinal)),
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ProcessBatchAsync_WhenProviderReportsRateLimit_SchedulesRetryWithoutCleanup()
    {
        var fixture = new Fixture();
        var dispatch = CreateDispatch(NotificationPreferenceCategoryCodes.EventUpdates);
        fixture.DispatchRepository.GetPendingBatch(Arg.Any<int>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns([dispatch]);
        fixture.DispatchRepository.TryMarkAsProcessing(dispatch.Id, Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(true);
        fixture.ConfigureActiveClaim(dispatch);
        fixture.DispatchRepository.MarkAsFailed(dispatch.Id, Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), true, Arg.Any<TimeSpan>(), Arg.Any<int>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(true);
        fixture.SubscriptionRepository.GetActiveByIdAsync(dispatch.TenantId, dispatch.SubscriptionId, Arg.Any<CancellationToken>())
            .Returns(CreateSubscription(dispatch));
        fixture.Sender.SendAsync(Arg.Any<WebPushSendEnvelope>(), Arg.Any<CancellationToken>())
            .Returns(WebPushSendResult.Retryable(429, "rate limited"));

        WebPushDispatchDrainResult result = await fixture.Service.ProcessBatchAsync(CancellationToken.None);

        await Assert.That(result.RetryScheduled).IsEqualTo(1);
        await fixture.DispatchRepository.Received(1).MarkAsFailed(
            dispatch.Id,
            Arg.Any<Guid>(),
            "web_push_retryable",
            Arg.Any<string>(),
            true,
            Arg.Is<TimeSpan>(delay => delay > TimeSpan.Zero),
            5,
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
        await fixture.DispatchRepository.DidNotReceiveWithAnyArgs().MarkPermanentFailureAndDeactivateSubscription(default, default, default, default, default!, default!, default, default);
    }

    [Test]
    public async Task ProcessBatchAsync_WhenDispatchTtlExpired_SkipsWithoutProviderSend()
    {
        var fixture = new Fixture();
        var dispatch = CreateDispatch(NotificationPreferenceCategoryCodes.EventUpdates);
        dispatch.CreatedAt = DateTime.UtcNow.AddHours(-7);
        fixture.DispatchRepository.GetPendingBatch(Arg.Any<int>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns([dispatch]);
        fixture.DispatchRepository.TryMarkAsProcessing(dispatch.Id, Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(true);
        fixture.ConfigureActiveClaim(dispatch);
        fixture.DispatchRepository.MarkAsSkipped(dispatch.Id, Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(true);

        var result = await fixture.Service.ProcessBatchAsync(CancellationToken.None);

        await Assert.That(result.Skipped).IsEqualTo(1);
        await fixture.Sender.DidNotReceiveWithAnyArgs().SendAsync(default!, default);
        await fixture.DispatchRepository.Received(1).MarkAsSkipped(
            dispatch.Id,
            Arg.Any<Guid>(),
            "web_push_ttl_expired",
            Arg.Any<string>(),
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ProcessBatchAsync_WhenProviderReportsBadRequest_DeadLettersWithoutDeletingValidSubscription()
    {
        var fixture = new Fixture();
        var dispatch = CreateDispatch(NotificationPreferenceCategoryCodes.EventUpdates);
        fixture.DispatchRepository.GetPendingBatch(Arg.Any<int>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns([dispatch]);
        fixture.DispatchRepository.TryMarkAsProcessing(dispatch.Id, Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(true);
        fixture.ConfigureActiveClaim(dispatch);
        fixture.DispatchRepository.MarkAsFailed(dispatch.Id, Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), false, Arg.Any<TimeSpan>(), Arg.Any<int>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(true);
        fixture.SubscriptionRepository.GetActiveByIdAsync(dispatch.TenantId, dispatch.SubscriptionId, Arg.Any<CancellationToken>())
            .Returns(CreateSubscription(dispatch));
        fixture.Sender.SendAsync(Arg.Any<WebPushSendEnvelope>(), Arg.Any<CancellationToken>())
            .Returns(WebPushSendResult.PermanentNonRetryable(400, "bad request"));

        WebPushDispatchDrainResult result = await fixture.Service.ProcessBatchAsync(CancellationToken.None);

        await Assert.That(result.DeadLettered).IsEqualTo(1);
        await fixture.DispatchRepository.Received(1).MarkAsFailed(
            dispatch.Id,
            Arg.Any<Guid>(),
            "web_push_permanent_provider_failure",
            Arg.Any<string>(),
            false,
            TimeSpan.Zero,
            5,
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
        await fixture.DispatchRepository.DidNotReceiveWithAnyArgs().MarkPermanentFailureAndDeactivateSubscription(default, default, default, default, default!, default!, default, default);
    }

    [Test]
    public async Task ProcessBatchAsync_WhenClaimDisappearsAfterClaim_ReturnsStaleLeaseWithoutPreferenceOrProviderAccess()
    {
        var fixture = new Fixture();
        var dispatch = CreateDispatch(NotificationPreferenceCategoryCodes.EventUpdates);
        fixture.DispatchRepository.GetPendingBatch(Arg.Any<int>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns([dispatch]);
        fixture.DispatchRepository.TryMarkAsProcessing(dispatch.Id, Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(true);
        fixture.ConfigureMissingActiveClaim(dispatch);
        fixture.SubscriptionRepository.GetActiveByIdAsync(dispatch.TenantId, dispatch.SubscriptionId, Arg.Any<CancellationToken>())
            .Returns(CreateSubscription(dispatch));
        fixture.Sender.SendAsync(Arg.Any<WebPushSendEnvelope>(), Arg.Any<CancellationToken>())
            .Returns(WebPushSendResult.Succeeded(201));
        fixture.DispatchRepository.MarkAsDelivered(dispatch.Id, Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(true);

        WebPushDispatchDrainResult result = await fixture.Service.ProcessBatchAsync(CancellationToken.None);

        await Assert.That(result.StaleLease).IsEqualTo(1);
        await fixture.PreferenceResolver.DidNotReceiveWithAnyArgs().ResolveAsync(default!, default);
        await fixture.SubscriptionRepository.DidNotReceiveWithAnyArgs().GetActiveByIdAsync(default, default, default);
        await fixture.Sender.DidNotReceiveWithAnyArgs().SendAsync(default!, default);
    }

    [Test]
    public async Task ProcessBatchAsync_WhenUserIsFencedAfterClaim_SkipsWithoutPreferenceOrSubscriptionSecretAccess()
    {
        var fixture = new Fixture();
        var dispatch = CreateDispatch(NotificationPreferenceCategoryCodes.EventUpdates);
        fixture.DispatchRepository.GetPendingBatch(Arg.Any<int>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns([dispatch]);
        fixture.DispatchRepository.TryMarkAsProcessing(dispatch.Id, Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(true);
        fixture.ConfigureActiveClaim(dispatch);
        fixture.PrivacyErasureStateRepository.GetBySubjectAsync(dispatch.UserId, Arg.Any<CancellationToken>())
            .Returns(CreateFencedSaga(dispatch.UserId));
        fixture.DispatchRepository.MarkAsSkipped(dispatch.Id, Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(true);
        fixture.SubscriptionRepository.GetActiveByIdAsync(dispatch.TenantId, dispatch.SubscriptionId, Arg.Any<CancellationToken>())
            .Returns(CreateSubscription(dispatch));
        fixture.Sender.SendAsync(Arg.Any<WebPushSendEnvelope>(), Arg.Any<CancellationToken>())
            .Returns(WebPushSendResult.Succeeded(201));
        fixture.DispatchRepository.MarkAsDelivered(dispatch.Id, Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(true);

        WebPushDispatchDrainResult result = await fixture.Service.ProcessBatchAsync(CancellationToken.None);

        await Assert.That(result.Skipped).IsEqualTo(1);
        await fixture.PrivacyErasureStateRepository.Received(1).GetBySubjectAsync(dispatch.UserId, Arg.Any<CancellationToken>());
        await fixture.PreferenceResolver.DidNotReceiveWithAnyArgs().ResolveAsync(default!, default);
        await fixture.SubscriptionRepository.DidNotReceiveWithAnyArgs().GetActiveByIdAsync(default, default, default);
        await fixture.Sender.DidNotReceiveWithAnyArgs().SendAsync(default!, default);
        await fixture.DispatchRepository.Received(1).MarkAsSkipped(
            dispatch.Id,
            Arg.Any<Guid>(),
            "privacy_erasure_fenced",
            Arg.Any<string>(),
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
    }

    private static WebPushDispatchOutbox CreateDispatch(string categoryCode) => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = Guid.CreateVersion7(),
        NotificationId = Guid.CreateVersion7(),
        SubscriptionId = Guid.CreateVersion7(),
        UserId = Guid.CreateVersion7(),
        CategoryId = 4,
        Category = new NotificationPreferenceCategory
        {
            Id = 4,
            MasterCode = categoryCode,
            FullName = categoryCode,
            IsRequired = categoryCode == NotificationPreferenceCategoryCodes.AccountSecurity,
            DefaultEmailEnabled = true,
            DefaultInAppEnabled = true,
            DefaultPushEnabled = true
        },
        PayloadJson = "{\"title\":\"<script>alert(1)</script>\"}",
        AttemptCount = 0,
        MaxAttempts = 5,
        Status = WebPushDispatchStatus.Pending,
        CreatedAt = DateTime.UtcNow
    };

    private static WebPushSubscription CreateSubscription(WebPushDispatchOutbox dispatch) => new()
    {
        Id = dispatch.SubscriptionId,
        TenantId = dispatch.TenantId,
        UserId = dispatch.UserId,
        DeviceIdentifier = "device-1",
        Endpoint = "https://push.example.test/send/1",
        P256Dh = "p256dh-key",
        AuthSecret = "auth-secret",
        IsActive = true,
        LastSeenAt = DateTime.UtcNow
    };

    private static PrivacyErasureSaga CreateFencedSaga(Guid userId)
    {
        DateTime nowUtc = DateTime.UtcNow;
        PrivacyErasureIntent intent = PrivacyErasureIntent.Record(
            Guid.CreateVersion7(),
            1,
            PrivacyErasureSubjectKind.User,
            userId,
            PrivacyErasureReasonCode.AccountDeletion,
            1,
            nowUtc,
            nowUtc);
        return PrivacyErasureSaga.Start(intent, 1, new byte[32], nowUtc.AddMinutes(5), nowUtc);
    }

    private sealed class Fixture
    {
        public Fixture(bool enabled = true)
        {
            DispatchRepository = Substitute.For<IWebPushDispatchOutboxRepository>();
            SubscriptionRepository = Substitute.For<IWebPushSubscriptionRepository>();
            Sender = Substitute.For<IWebPushNotificationSender>();
            PreferenceResolver = Substitute.For<INotificationPreferenceResolver>();
            PrivacyErasureStateRepository = Substitute.For<IPrivacyErasureStateRepository>();
            PreferenceResolver.ResolveAsync(Arg.Any<NotificationPreferenceResolveRequest>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    var request = call.Arg<NotificationPreferenceResolveRequest>();
                    return new NotificationPreferenceDecision(
                        request.CategoryCode,
                        request.ChannelCode,
                        enabled,
                        false,
                        false,
                        false,
                        "Test",
                    null);
                });
            PrivacyErasureStateRepository.GetBySubjectAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns((PrivacyErasureSaga?)null);

            var services = new ServiceCollection();
            services.AddSingleton(DispatchRepository);
            services.AddSingleton(SubscriptionRepository);
            services.AddSingleton(Sender);
            services.AddSingleton(PreferenceResolver);
            services.AddSingleton(PrivacyErasureStateRepository);
            ServiceProvider = services.BuildServiceProvider();

            Service = new WebPushDispatchDrainService(
                ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
                Options.Create(new WebPushSettings
                {
                    Enabled = true,
                    VapidSubject = "mailto:ops@example.test",
                    VapidPublicKey = "public-key",
                    VapidPrivateKey = "private-key",
                    BatchSize = 10,
                    MaxAttemptCount = 5,
                    InitialRetryDelaySeconds = 5,
                    MaxRetryDelaySeconds = 60
                }),
                NullLogger<WebPushDispatchDrainService>.Instance);
        }

        public IWebPushDispatchOutboxRepository DispatchRepository { get; }
        public IWebPushSubscriptionRepository SubscriptionRepository { get; }
        public IWebPushNotificationSender Sender { get; }
        public INotificationPreferenceResolver PreferenceResolver { get; }
        public IPrivacyErasureStateRepository PrivacyErasureStateRepository { get; }
        public WebPushDispatchDrainService Service { get; }
        private ServiceProvider ServiceProvider { get; }

        public void ConfigureActiveClaim(WebPushDispatchOutbox dispatch)
        {
            DispatchRepository.GetActiveClaimAsync(
                    dispatch.TenantId,
                    dispatch.Id,
                    Arg.Any<Guid>(),
                    Arg.Any<CancellationToken>())
                .Returns(dispatch);
        }

        public void ConfigureMissingActiveClaim(WebPushDispatchOutbox dispatch)
        {
            DispatchRepository.GetActiveClaimAsync(
                    dispatch.TenantId,
                    dispatch.Id,
                    Arg.Any<Guid>(),
                    Arg.Any<CancellationToken>())
                .Returns((WebPushDispatchOutbox?)null);
        }
    }
}
