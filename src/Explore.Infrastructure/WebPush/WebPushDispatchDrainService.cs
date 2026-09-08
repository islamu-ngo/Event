using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Models;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.WebPush;

public sealed class WebPushDispatchDrainService(
    IServiceScopeFactory scopeFactory,
    IOptions<WebPushSettings> settings,
    ILogger<WebPushDispatchDrainService> logger)
{
    private const string ProcessingLeaseExpiredFailureCategory = "processing_lease_expired";
    private const string ProcessingLeaseExpiredMessage = "Web Push dispatch processing lease expired before a durable outcome was recorded.";
    private const string PreferenceDisabledFailureCategory = "recipient_notification_preference_disabled";
    private const string PreferenceDisabledMessage = "Recipient disabled this notification category and push channel; Web Push send was skipped before provider handoff.";
    private const string PrivacyErasureFencedFailureCategory = "privacy_erasure_fenced";
    private const string PrivacyErasureFencedMessage = "Web Push dispatch was skipped because the recipient is subject to privacy erasure.";
    private const string MissingSubscriptionFailureCategory = "web_push_subscription_missing";
    private const string MissingSubscriptionMessage = "Active Web Push subscription was missing before provider handoff.";
    private const string TimeToLiveExpiredFailureCategory = "web_push_ttl_expired";
    private const string TimeToLiveExpiredMessage = "Web Push dispatch expired before provider handoff and was discarded to prevent stale delivery.";
    private const string DisplayTag = "islamu-notification";

    private readonly WebPushSettings _settings = settings.Value;

    public async Task<WebPushDispatchDrainResult> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        if (!_settings.Enabled)
        {
            return WebPushDispatchDrainResult.Empty;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IWebPushDispatchOutboxRepository>();
        var pending = await repository.GetPendingBatch(_settings.BatchSize, DateTime.UtcNow, cancellationToken);

        var result = new WebPushDispatchDrainAccumulator(pending.Count);
        foreach (var dispatch in pending)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            result.Add(await ProcessDispatchAsync(dispatch, cancellationToken));
        }

        return result.ToResult();
    }

    public async Task<WebPushDispatchRecoveryResult> RecoverStaleProcessingAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IWebPushDispatchOutboxRepository>();
        var recoveredAt = DateTime.UtcNow;
        var processingStartedBefore = recoveredAt.AddSeconds(-_settings.ProcessingLeaseTimeoutSeconds);
        var recovered = await repository.RecoverStaleProcessing(
            processingStartedBefore,
            recoveredAt,
            ProcessingLeaseExpiredFailureCategory,
            ProcessingLeaseExpiredMessage,
            _settings.BatchSize,
            cancellationToken);

        if (recovered > 0)
        {
            logger.LogWarning("Recovered {RecoveredCount} stale Web Push dispatch processing rows", recovered);
        }

        return new WebPushDispatchRecoveryResult(recovered, processingStartedBefore);
    }

    private async Task<WebPushDispatchDrainOutcome> ProcessDispatchAsync(WebPushDispatchOutbox dispatch, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var leaseToken = Guid.CreateVersion7();
        {
            await using var claimScope = scopeFactory.CreateAsyncScope();
            var claimRepository = claimScope.ServiceProvider.GetRequiredService<IWebPushDispatchOutboxRepository>();
            if (!await claimRepository.TryMarkAsProcessing(dispatch.Id, leaseToken, now, cancellationToken))
            {
                return WebPushDispatchDrainOutcome.AlreadyClaimed;
            }
        }

        await using var executionScope = scopeFactory.CreateAsyncScope();
        var dispatchRepository = executionScope.ServiceProvider.GetRequiredService<IWebPushDispatchOutboxRepository>();
        var activeDispatch = await dispatchRepository.GetActiveClaimAsync(
            dispatch.TenantId,
            dispatch.Id,
            leaseToken,
            cancellationToken);
        if (activeDispatch is null)
        {
            return WebPushDispatchDrainOutcome.StaleLease;
        }

        var privacyErasureStateRepository = executionScope.ServiceProvider.GetRequiredService<IPrivacyErasureStateRepository>();
        if (await privacyErasureStateRepository.GetBySubjectAsync(activeDispatch.UserId, cancellationToken) is not null)
        {
            var skipped = await dispatchRepository.MarkAsSkipped(
                activeDispatch.Id,
                leaseToken,
                PrivacyErasureFencedFailureCategory,
                PrivacyErasureFencedMessage,
                DateTime.UtcNow,
                cancellationToken);
            return skipped ? WebPushDispatchDrainOutcome.Skipped : WebPushDispatchDrainOutcome.StaleLease;
        }

        var categoryCode = activeDispatch.Category?.MasterCode;
        if (string.IsNullOrWhiteSpace(categoryCode))
        {
            await dispatchRepository.MarkAsFailed(
                activeDispatch.Id,
                leaseToken,
                "web_push_category_missing",
                "Web Push dispatch category metadata was unavailable.",
                false,
                TimeSpan.Zero,
                _settings.MaxAttemptCount,
                DateTime.UtcNow,
                cancellationToken);
            return WebPushDispatchDrainOutcome.DeadLettered;
        }

        var deliveryPolicy = WebPushDeliveryPolicy.For(categoryCode);
        var expiresAt = activeDispatch.CreatedAt.AddSeconds(deliveryPolicy.TimeToLiveSeconds);
        if (expiresAt <= now)
        {
            await dispatchRepository.MarkAsSkipped(
                activeDispatch.Id,
                leaseToken,
                TimeToLiveExpiredFailureCategory,
                TimeToLiveExpiredMessage,
                now,
                cancellationToken);
            return WebPushDispatchDrainOutcome.Skipped;
        }

        var preferenceResolver = executionScope.ServiceProvider.GetRequiredService<INotificationPreferenceResolver>();
        var decision = await preferenceResolver.ResolveAsync(
            new NotificationPreferenceResolveRequest(
                activeDispatch.TenantId,
                activeDispatch.UserId,
                null,
                null,
                categoryCode,
                NotificationPreferenceChannelCodes.Push),
            cancellationToken);

        if (!decision.IsEnabled)
        {
            await dispatchRepository.MarkAsSkipped(
                activeDispatch.Id,
                leaseToken,
                PreferenceDisabledFailureCategory,
                PreferenceDisabledMessage,
                DateTime.UtcNow,
                cancellationToken);
            return WebPushDispatchDrainOutcome.Skipped;
        }

        var subscriptionRepository = executionScope.ServiceProvider.GetRequiredService<IWebPushSubscriptionRepository>();
        var subscription = await subscriptionRepository.GetActiveByIdAsync(
            activeDispatch.TenantId,
            activeDispatch.SubscriptionId,
            cancellationToken);
        if (subscription is null)
        {
            await dispatchRepository.MarkAsFailed(
                activeDispatch.Id,
                leaseToken,
                MissingSubscriptionFailureCategory,
                MissingSubscriptionMessage,
                false,
                TimeSpan.Zero,
                _settings.MaxAttemptCount,
                DateTime.UtcNow,
                cancellationToken);
            return WebPushDispatchDrainOutcome.DeadLettered;
        }

        var sender = executionScope.ServiceProvider.GetRequiredService<IWebPushNotificationSender>();
        var sendResult = await sender.SendAsync(new WebPushSendEnvelope(
            subscription.Endpoint,
            subscription.P256Dh,
            subscription.AuthSecret,
            BuildPayloadJson(deliveryPolicy.Topic),
            activeDispatch.Id.ToString(),
            Math.Max(1, (int)Math.Floor((expiresAt - DateTime.UtcNow).TotalSeconds)),
            deliveryPolicy.Topic,
            deliveryPolicy.Urgency), cancellationToken);

        return await PersistSendResultAsync(activeDispatch, leaseToken, sendResult, expiresAt, cancellationToken);
    }

    private async Task<WebPushDispatchDrainOutcome> PersistSendResultAsync(
        WebPushDispatchOutbox dispatch,
        Guid leaseToken,
        WebPushSendResult sendResult,
        DateTime expiresAt,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IWebPushDispatchOutboxRepository>();
        var completedAt = DateTime.UtcNow;

        if (sendResult.Success)
        {
            var delivered = await repository.MarkAsDelivered(dispatch.Id, leaseToken, completedAt, cancellationToken);
            return delivered ? WebPushDispatchDrainOutcome.Delivered : WebPushDispatchDrainOutcome.StaleLease;
        }

        var message = BuildFailureMessage(sendResult);
        return sendResult.FailureKind switch
        {
            WebPushSendFailureKind.StaleSubscription =>
                await repository.MarkPermanentFailureAndDeactivateSubscription(
                    dispatch.TenantId,
                    dispatch.Id,
                    leaseToken,
                    dispatch.SubscriptionId,
                    sendResult.FailureCategory,
                    message,
                    completedAt,
                    cancellationToken)
                    ? WebPushDispatchDrainOutcome.PermanentFailed
                    : WebPushDispatchDrainOutcome.StaleLease,
            WebPushSendFailureKind.Retryable =>
                await MarkFailedAsync(repository, dispatch, leaseToken, sendResult.FailureCategory, message, true, completedAt, expiresAt, cancellationToken),
            WebPushSendFailureKind.PermanentNonRetryable =>
                await MarkFailedAsync(repository, dispatch, leaseToken, sendResult.FailureCategory, message, false, completedAt, expiresAt, cancellationToken),
            _ => WebPushDispatchDrainOutcome.DeadLettered
        };
    }

    private async Task<WebPushDispatchDrainOutcome> MarkFailedAsync(
        IWebPushDispatchOutboxRepository repository,
        WebPushDispatchOutbox dispatch,
        Guid leaseToken,
        string category,
        string message,
        bool retryable,
        DateTime failedAt,
        DateTime expiresAt,
        CancellationToken cancellationToken)
    {
        var delay = retryable ? TimeSpan.FromSeconds(_settings.CalculateRetryDelay(dispatch.AttemptCount + 1)) : TimeSpan.Zero;
        if (retryable && failedAt.Add(delay) >= expiresAt)
        {
            var skipped = await repository.MarkAsSkipped(
                dispatch.Id,
                leaseToken,
                TimeToLiveExpiredFailureCategory,
                TimeToLiveExpiredMessage,
                failedAt,
                cancellationToken);
            return skipped ? WebPushDispatchDrainOutcome.Skipped : WebPushDispatchDrainOutcome.StaleLease;
        }

        var updated = await repository.MarkAsFailed(
            dispatch.Id,
            leaseToken,
            category,
            message,
            retryable,
            delay,
            _settings.MaxAttemptCount,
            failedAt,
            cancellationToken);
        if (!updated)
        {
            return WebPushDispatchDrainOutcome.StaleLease;
        }

        return retryable && dispatch.AttemptCount + 1 < Math.Min(dispatch.MaxAttempts, _settings.MaxAttemptCount)
            ? WebPushDispatchDrainOutcome.RetryScheduled
            : WebPushDispatchDrainOutcome.DeadLettered;
    }

    private string BuildPayloadJson(string topic)
    {
        return JsonSerializer.Serialize(new
        {
            type = "notification_refresh",
            openPath = _settings.NotificationOpenPath,
            refreshPath = _settings.NotificationRefreshPath,
            tag = DisplayTag,
            topic
        });
    }

    private static string BuildFailureMessage(WebPushSendResult result)
    {
        return result.StatusCode is null
            ? result.SanitizedErrorMessage ?? "Web Push send failed."
            : $"HTTP {result.StatusCode}: {result.SanitizedErrorMessage ?? "Web Push send failed."}";
    }
}

internal sealed record WebPushDeliveryPolicy(int TimeToLiveSeconds, string Topic, WebPushUrgency Urgency)
{
    public static WebPushDeliveryPolicy For(string categoryCode) => categoryCode switch
    {
        NotificationPreferenceCategoryCodes.AccountSecurity => new(300, categoryCode, WebPushUrgency.High),
        NotificationPreferenceCategoryCodes.TrustSafety => new(3600, categoryCode, WebPushUrgency.High),
        NotificationPreferenceCategoryCodes.BillingLegal => new(86400, categoryCode, WebPushUrgency.Normal),
        NotificationPreferenceCategoryCodes.RegistrationStatus => new(21600, categoryCode, WebPushUrgency.Normal),
        NotificationPreferenceCategoryCodes.EventUpdates => new(21600, categoryCode, WebPushUrgency.Normal),
        NotificationPreferenceCategoryCodes.OrganizationUpdates => new(86400, categoryCode, WebPushUrgency.Low),
        NotificationPreferenceCategoryCodes.GroupUpdates => new(86400, categoryCode, WebPushUrgency.Low),
        NotificationPreferenceCategoryCodes.ProductAnnouncements => new(86400, categoryCode, WebPushUrgency.Low),
        NotificationPreferenceCategoryCodes.Marketing => new(21600, categoryCode, WebPushUrgency.VeryLow),
        _ => new(21600, "notification-refresh", WebPushUrgency.Normal)
    };
}

public sealed record WebPushDispatchDrainResult(
    int Pending,
    int Delivered,
    int RetryScheduled,
    int DeadLettered,
    int PermanentFailed,
    int Skipped,
    int AlreadyClaimed,
    int StaleLease)
{
    public static WebPushDispatchDrainResult Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0);
}

public sealed record WebPushDispatchRecoveryResult(int Recovered, DateTime ProcessingStartedBefore);

public enum WebPushDispatchDrainOutcome
{
    Delivered,
    RetryScheduled,
    DeadLettered,
    PermanentFailed,
    Skipped,
    AlreadyClaimed,
    StaleLease
}

internal sealed class WebPushDispatchDrainAccumulator(int pending)
{
    private int _delivered;
    private int _retryScheduled;
    private int _deadLettered;
    private int _permanentFailed;
    private int _skipped;
    private int _alreadyClaimed;
    private int _staleLease;

    public void Add(WebPushDispatchDrainOutcome outcome)
    {
        switch (outcome)
        {
            case WebPushDispatchDrainOutcome.Delivered: _delivered++; break;
            case WebPushDispatchDrainOutcome.RetryScheduled: _retryScheduled++; break;
            case WebPushDispatchDrainOutcome.DeadLettered: _deadLettered++; break;
            case WebPushDispatchDrainOutcome.PermanentFailed: _permanentFailed++; break;
            case WebPushDispatchDrainOutcome.Skipped: _skipped++; break;
            case WebPushDispatchDrainOutcome.AlreadyClaimed: _alreadyClaimed++; break;
            case WebPushDispatchDrainOutcome.StaleLease: _staleLease++; break;
        }
    }

    public WebPushDispatchDrainResult ToResult()
    {
        return new WebPushDispatchDrainResult(pending, _delivered, _retryScheduled, _deadLettered, _permanentFailed, _skipped, _alreadyClaimed, _staleLease);
    }
}
