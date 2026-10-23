using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.Models.InternalEvents;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Services;

public sealed class EventPublishedNotificationFanoutService : IEventPublishedNotificationFanoutService
{
    public const string FanoutKind = "event-published";
    public const string StatusProcessing = "processing";
    public const string StatusCompleted = "completed";
    public const string StatusFailed = "failed";
    public const string OutcomeCompleted = "completed";
    public const string OutcomeDuplicateSkipped = "duplicate_skipped";
    public const string OutcomeFailed = "failed";
    public const string OutcomeNotificationCreated = "notification_created";
    public const string OutcomeProcessing = "processing";
    public const string OutcomeProcessed = "processed";
    public const string OutcomeSkippedCompleted = "skipped_completed";

    private const int BatchSize = 250;
    private const int RecipientOutcomeFenced = 0;
    private const int RecipientOutcomePreferenceDisabled = 1;
    private const int RecipientOutcomeNotificationCreated = 2;
    private const int RecipientOutcomeDuplicateSkipped = 3;

    private readonly IActorSubscriptionRepository actorSubscriptionRepository;
    private readonly INotificationRepository notificationRepository;
    private readonly INotificationFanoutRunRepository fanoutRunRepository;
    private readonly IPrivacyErasureStateRepository privacyErasureStateRepository;
    private readonly IUnitOfWork unitOfWork;
    private readonly INotificationPreferenceResolver notificationPreferenceResolver;
    private readonly IWebPushSubscriptionRepository webPushSubscriptionRepository;
    private readonly IWebPushDispatchOutboxRepository webPushDispatchOutboxRepository;
    private readonly BusinessMetrics metrics;
    private readonly ILogger<EventPublishedNotificationFanoutService> logger;
    private readonly IEventRepository eventRepository;
    private readonly ITenantContext tenantContext;

    public EventPublishedNotificationFanoutService(
        IActorSubscriptionRepository actorSubscriptionRepository,
        INotificationRepository notificationRepository,
        INotificationFanoutRunRepository fanoutRunRepository,
        IPrivacyErasureStateRepository privacyErasureStateRepository,
        IUnitOfWork unitOfWork,
        INotificationPreferenceResolver notificationPreferenceResolver,
        IWebPushSubscriptionRepository webPushSubscriptionRepository,
        IWebPushDispatchOutboxRepository webPushDispatchOutboxRepository,
        BusinessMetrics metrics,
        ILogger<EventPublishedNotificationFanoutService> logger,
        IEventRepository eventRepository,
        ITenantContext tenantContext)
    {
        this.actorSubscriptionRepository = actorSubscriptionRepository;
        this.notificationRepository = notificationRepository;
        this.fanoutRunRepository = fanoutRunRepository;
        this.privacyErasureStateRepository = privacyErasureStateRepository;
        this.unitOfWork = unitOfWork;
        this.notificationPreferenceResolver = notificationPreferenceResolver;
        this.webPushSubscriptionRepository = webPushSubscriptionRepository;
        this.webPushDispatchOutboxRepository = webPushDispatchOutboxRepository;
        this.metrics = metrics;
        this.logger = logger;
        this.eventRepository = eventRepository;
        this.tenantContext = tenantContext;
    }

    public async Task FanoutAsync(EventPublishedNotificationFanoutRequested request, CancellationToken cancellationToken = default)
    {
        if (request.TenantId == Guid.Empty || request.TenantId != tenantContext.TenantId)
            throw new InvalidOperationException("The publication fanout tenant is not selected.");
        var source = await eventRepository.GetAuthorizationTargetByIdAsync(request.EventId, cancellationToken);
        if (source is null || source.TenantId != request.TenantId || source.ActorId != request.SourceActorId
            || source.EventStatusId != (int)EventStatusEnum.Published)
            throw new InvalidOperationException("The publication fanout source is unavailable.");
        request = request with
        {
            EventTitle = source.Title,
            StartDate = source.FirstSessionStartUtc ?? request.PublishedAt,
            EndDate = source.LastSessionStartUtc
        };
        var run = await GetOrCreateRunAsync(request, cancellationToken);
        if (string.Equals(run.Status, StatusCompleted, StringComparison.Ordinal))
        {
            metrics.RecordNotificationFanoutRun(FanoutKind, OutcomeSkippedCompleted);
            logger.LogInformation("Skipping completed notification fanout run.");
            return;
        }

        run.Status = StatusProcessing;
        run.StartedAt ??= DateTime.UtcNow;
        run.FailedAt = null;
        run.LastError = null;
        await fanoutRunRepository.Update(run);
        metrics.RecordNotificationFanoutRun(FanoutKind, OutcomeProcessing);
        logger.LogInformation("Started notification fanout run.");

        var processedThisAttempt = 0;
        var createdThisAttempt = 0;
        var duplicateSkippedThisAttempt = 0;
        try
        {
            while (true)
            {
                var batch = await actorSubscriptionRepository.GetActiveFanoutBatchAsync(
                    request.TenantId,
                    request.SourceActorId,
                    run.CursorSubscriberTenantUserId,
                    BatchSize,
                    cancellationToken);

                if (batch.Count == 0)
                {
                    break;
                }

                foreach (var subscription in batch)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    run.ProcessedCount++;
                    processedThisAttempt++;
                    run.CursorSubscriberTenantUserId = subscription.SubscriberTenantUserId;

                    if (await IsFencedAsync(subscription.SubscriberUserId, cancellationToken))
                    {
                        continue;
                    }

                    var deduplicationKey = BuildDeduplicationKey(request, subscription);
                    var outcome = await unitOfWork.ExecuteSerializableAsync(
                        token => ProcessSubscriberAsync(request, subscription, deduplicationKey, token),
                        cancellationToken);

                    if (outcome == RecipientOutcomeNotificationCreated)
                    {
                        run.CreatedNotificationCount++;
                        createdThisAttempt++;
                    }
                    else if (outcome == RecipientOutcomeDuplicateSkipped)
                    {
                        duplicateSkippedThisAttempt++;
                    }
                }

                await fanoutRunRepository.Update(run);

                if (batch.Count < BatchSize)
                {
                    break;
                }
            }

            run.Status = StatusCompleted;
            run.CompletedAt = DateTime.UtcNow;
            run.FailedAt = null;
            run.LastError = null;
            await fanoutRunRepository.Update(run);
            metrics.RecordNotificationFanoutRun(FanoutKind, OutcomeCompleted);
            metrics.RecordNotificationFanoutSubscribers(processedThisAttempt, FanoutKind, OutcomeProcessed);
            metrics.RecordNotificationFanoutSubscribers(createdThisAttempt, FanoutKind, OutcomeNotificationCreated);
            metrics.RecordNotificationFanoutSubscribers(duplicateSkippedThisAttempt, FanoutKind, OutcomeDuplicateSkipped);
            logger.LogInformation(
                "Completed notification fanout run: processed {ProcessedCount}, created {CreatedCount}, duplicate skipped {DuplicateSkippedCount}",
                processedThisAttempt,
                createdThisAttempt,
                duplicateSkippedThisAttempt);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            run.Status = StatusFailed;
            run.FailedAt = DateTime.UtcNow;
            run.LastError = "event_publication_fanout_failed";
            await fanoutRunRepository.Update(run);
            metrics.RecordNotificationFanoutRun(FanoutKind, OutcomeFailed);
            metrics.RecordNotificationFanoutSubscribers(processedThisAttempt, FanoutKind, OutcomeProcessed);
            metrics.RecordNotificationFanoutSubscribers(createdThisAttempt, FanoutKind, OutcomeNotificationCreated);
            metrics.RecordNotificationFanoutSubscribers(duplicateSkippedThisAttempt, FanoutKind, OutcomeDuplicateSkipped);
            logger.LogError(
                "Failed notification fanout run: processed {ProcessedCount}, created {CreatedCount}, duplicate skipped {DuplicateSkippedCount}",
                processedThisAttempt,
                createdThisAttempt,
                duplicateSkippedThisAttempt);
            throw;
        }
    }

    private async Task<NotificationFanoutRun> GetOrCreateRunAsync(
        EventPublishedNotificationFanoutRequested request,
        CancellationToken cancellationToken)
    {
        var run = await fanoutRunRepository.GetBySourceAsync(
            request.TenantId,
            FanoutKind,
            (int)NotificationEntityTypeEnum.Event,
            request.EventId,
            request.SourceActorId,
            trackChanges: true,
            cancellationToken);

        if (run is not null)
        {
            return run;
        }

        return await fanoutRunRepository.Create(new NotificationFanoutRun
        {
            Id = Guid.CreateVersion7(),
            TenantId = request.TenantId,
            Tenant = null!,
            FanoutKind = FanoutKind,
            NotificationEntityTypeId = (int)NotificationEntityTypeEnum.Event,
            NotificationEntityType = null!,
            EntityId = request.EventId,
            SourceActorId = request.SourceActorId,
            SourceActor = null!,
            Status = StatusProcessing,
            StartedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            ConcurrencyStamp = Guid.CreateVersion7()
        });
    }

    private static Notification CreateNotification(
        EventPublishedNotificationFanoutRequested request,
        ActorSubscription subscription,
        string deduplicationKey)
    {
        return new Notification
        {
            Id = Guid.CreateVersion7(),
            TenantId = request.TenantId,
            Tenant = null!,
            UserId = subscription.SubscriberUserId,
            User = null!,
            NotificationTypeId = (int)NotificationTypeEnum.EventCreated,
            NotificationType = null!,
            Title = $"New event: {request.EventTitle}",
            Body = "An organization or group you follow published a new event.",
            DeduplicationKey = deduplicationKey,
            NotificationEntityTypeId = (int)NotificationEntityTypeEnum.Event,
            EntityId = request.EventId.ToString(),
            NotificationScopeId = subscription.TargetActorTypeId,
            NotificationScope = null!,
            SourceActorId = request.SourceActorId,
            RecipientContextActorId = subscription.TargetActorId,
            NotificationReasonId = (int)NotificationReasonEnum.Subscription,
            CreatedAt = DateTime.UtcNow
        };
    }

    private static string BuildDeduplicationKey(EventPublishedNotificationFanoutRequested request, ActorSubscription subscription)
    {
        return $"event-published:{request.TenantId:N}:{request.EventId:N}:{subscription.SubscriberTenantUserId:N}";
    }

    private async Task<int> ProcessSubscriberAsync(
        EventPublishedNotificationFanoutRequested request,
        ActorSubscription subscription,
        string deduplicationKey,
        CancellationToken cancellationToken)
    {
        var source = await eventRepository.GetAuthorizationTargetByIdAsync(request.EventId, cancellationToken);
        if (request.TenantId != tenantContext.TenantId || source is null
            || source.TenantId != request.TenantId || source.ActorId != request.SourceActorId
            || source.EventStatusId != (int)EventStatusEnum.Published)
            return RecipientOutcomeFenced;
        request = request with
        {
            EventTitle = source.Title,
            StartDate = source.FirstSessionStartUtc ?? request.PublishedAt,
            EndDate = source.LastSessionStartUtc
        };
        if (await IsFencedAsync(subscription.SubscriberUserId, cancellationToken))
        {
            return RecipientOutcomeFenced;
        }

        var notification = await notificationRepository.GetByDeduplicationKeyAsync(
            request.TenantId,
            subscription.SubscriberUserId,
            deduplicationKey,
            cancellationToken);
        if (notification is not null)
        {
            await EnqueueWebPushDispatchesAsync(request, subscription, notification.Id, cancellationToken);
            return RecipientOutcomeDuplicateSkipped;
        }

        var preference = await notificationPreferenceResolver.ResolveAsync(
            new NotificationPreferenceResolveRequest(
                request.TenantId,
                subscription.SubscriberUserId,
                null,
                null,
                NotificationPreferenceCategoryCodes.EventUpdates,
                NotificationPreferenceChannelCodes.InApp),
            cancellationToken);
        if (!preference.IsEnabled)
        {
            return RecipientOutcomePreferenceDisabled;
        }

        notification = await notificationRepository.Create(CreateNotification(request, subscription, deduplicationKey));
        await EnqueueWebPushDispatchesAsync(request, subscription, notification.Id, cancellationToken);
        return RecipientOutcomeNotificationCreated;
    }

    private async Task<bool> IsFencedAsync(Guid userId, CancellationToken cancellationToken) =>
        await privacyErasureStateRepository.GetBySubjectAsync(userId, cancellationToken) is not null;

    private async Task EnqueueWebPushDispatchesAsync(
        EventPublishedNotificationFanoutRequested request,
        ActorSubscription subscription,
        Guid notificationId,
        CancellationToken cancellationToken)
    {
        var pushPreference = await notificationPreferenceResolver.ResolveAsync(
            new NotificationPreferenceResolveRequest(
                request.TenantId,
                subscription.SubscriberUserId,
                null,
                null,
                NotificationPreferenceCategoryCodes.EventUpdates,
                NotificationPreferenceChannelCodes.Push),
            cancellationToken);

        if (!pushPreference.IsEnabled)
        {
            return;
        }

        var pushSubscriptions = await webPushSubscriptionRepository.ListActiveForUserAsync(
            request.TenantId,
            subscription.SubscriberUserId,
            cancellationToken);

        foreach (var pushSubscription in pushSubscriptions)
        {
            await webPushDispatchOutboxRepository.CreateIfNotExistsAsync(new WebPushDispatchOutbox
            {
                Id = Guid.CreateVersion7(),
                TenantId = request.TenantId,
                Tenant = null,
                NotificationId = notificationId,
                CategoryId = (int)NotificationPreferenceCategoryEnum.EventUpdates,
                Category = null,
                SubscriptionId = pushSubscription.Id,
                Subscription = null,
                UserId = subscription.SubscriberUserId,
                User = null,
                PayloadJson = "{\"kind\":\"event-published\"}",
                Status = WebPushDispatchStatus.Pending,
                CreatedAt = DateTime.UtcNow
            }, cancellationToken);
        }
    }
}
