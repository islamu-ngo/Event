using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Models.InternalEvents;
using Explore.Application.Notifications;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Services;

public sealed class EventModerationNotificationFanoutService(
    IRegistrationInventoryRepository registrationInventoryRepository,
    IEventModerationRecordRepository moderationRecordRepository,
    INotificationRepository notificationRepository,
    INotificationFanoutRunRepository fanoutRunRepository,
    INotificationPreferenceResolver notificationPreferenceResolver,
    INotificationFanoutOccurrenceRepository fanoutOccurrenceRepository,
    NotificationFanoutOccurrenceCoordinator fanoutCoordinator,
    IUnitOfWork unitOfWork,
    BusinessMetrics metrics,
    ILogger<EventModerationNotificationFanoutService> logger,
    ISettingMutationLock mutationLock) : IEventModerationNotificationFanoutService
{
    public const string LightFanoutKind = "event-moderated-light";
    public const string HeavyFanoutKind = "event-moderated-heavy";
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

    public async Task FanoutLightModerationAsync(
        EventLightModeratedNotificationFanoutRequested request,
        CancellationToken cancellationToken = default)
    {
        var run = await GetOrCreateRunAsync(request, cancellationToken);
        if (string.Equals(run.Status, StatusCompleted, StringComparison.Ordinal))
        {
            metrics.RecordNotificationFanoutRun(LightFanoutKind, OutcomeSkippedCompleted);
            logger.LogInformation(
                "Skipping completed light moderation notification fanout run {RunId} for moderation record {ModerationRecordId}",
                run.Id,
                request.ModerationRecordId);
            return;
        }

        run.Status = StatusProcessing;
        run.StartedAt ??= DateTime.UtcNow;
        run.FailedAt = null;
        run.LastError = null;
        await fanoutRunRepository.Update(run);
        metrics.RecordNotificationFanoutRun(LightFanoutKind, OutcomeProcessing);

        var processedThisAttempt = 0;
        var createdThisAttempt = 0;
        var duplicateSkippedThisAttempt = 0;

        try
        {
            while (true)
            {
                var attendeeUserIds = await registrationInventoryRepository.GetRegisteredUserFanoutBatchAsync(
                    request.TenantId,
                    request.EventId,
                    run.CursorSubscriberTenantUserId,
                    BatchSize,
                    cancellationToken);

                if (attendeeUserIds.Count == 0)
                {
                    break;
                }

                LightModerationBatchResult batch = LightModerationBatchResult.Blocked;
                await unitOfWork.ExecuteInTransactionAsync(
                    async token =>
                    {
                        bool heavyAuthority = await fanoutOccurrenceRepository
                            .AcquireEventPrecedenceLockAndHasHeavyAuthorityAsync(
                                request.TenantId,
                                request.EventId,
                                token);
                        if (heavyAuthority)
                        {
                            batch = LightModerationBatchResult.Blocked;
                            return;
                        }

                        var processed = 0;
                        var created = 0;
                        var duplicateSkipped = 0;
                        Guid? cursor = null;
                        foreach (Guid userId in attendeeUserIds)
                        {
                            token.ThrowIfCancellationRequested();
                            processed++;
                            cursor = userId;

                            string deduplicationKey = BuildLightModerationDeduplicationKey(request, userId);
                            bool alreadyCreated = await notificationRepository.ExistsByDeduplicationKeyAsync(
                                request.TenantId,
                                userId,
                                deduplicationKey,
                                token);
                            if (alreadyCreated)
                            {
                                duplicateSkipped++;
                                continue;
                            }

                            NotificationPreferenceDecision preference = await notificationPreferenceResolver.ResolveAsync(
                                new NotificationPreferenceResolveRequest(
                                    request.TenantId,
                                    userId,
                                    null,
                                    null,
                                    NotificationPreferenceCategoryCodes.TrustSafety,
                                    NotificationPreferenceChannelCodes.InApp),
                                token);
                            if (!preference.IsEnabled)
                            {
                                continue;
                            }

                            await notificationRepository.Create(
                                CreateLightModerationNotification(request, userId, deduplicationKey));
                            created++;
                        }

                        batch = new LightModerationBatchResult(
                            BlockedByHeavyAuthority: false,
                            processed,
                            created,
                            duplicateSkipped,
                            cursor);
                    },
                    cancellationToken);
                if (batch.BlockedByHeavyAuthority)
                {
                    run.Status = StatusCompleted;
                    run.CompletedAt = DateTime.UtcNow;
                    run.FailedAt = null;
                    run.LastError = null;
                    await fanoutRunRepository.Update(run);
                    metrics.RecordNotificationFanoutRun(LightFanoutKind, OutcomeSkippedCompleted);
                    return;
                }

                run.ProcessedCount += batch.ProcessedCount;
                run.CreatedNotificationCount += batch.CreatedCount;
                run.CursorSubscriberTenantUserId = batch.CursorSubscriberTenantUserId;
                processedThisAttempt += batch.ProcessedCount;
                createdThisAttempt += batch.CreatedCount;
                duplicateSkippedThisAttempt += batch.DuplicateSkippedCount;
                await fanoutRunRepository.Update(run);

                if (attendeeUserIds.Count < BatchSize)
                {
                    break;
                }
            }

            run.Status = StatusCompleted;
            run.CompletedAt = DateTime.UtcNow;
            run.FailedAt = null;
            run.LastError = null;
            await fanoutRunRepository.Update(run);
            metrics.RecordNotificationFanoutRun(LightFanoutKind, OutcomeCompleted);
            metrics.RecordNotificationFanoutSubscribers(processedThisAttempt, LightFanoutKind, OutcomeProcessed);
            metrics.RecordNotificationFanoutSubscribers(createdThisAttempt, LightFanoutKind, OutcomeNotificationCreated);
            metrics.RecordNotificationFanoutSubscribers(duplicateSkippedThisAttempt, LightFanoutKind, OutcomeDuplicateSkipped);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            run.Status = StatusFailed;
            run.FailedAt = DateTime.UtcNow;
            run.LastError = ex.Message;
            await fanoutRunRepository.Update(run);
            metrics.RecordNotificationFanoutRun(LightFanoutKind, OutcomeFailed);
            metrics.RecordNotificationFanoutSubscribers(processedThisAttempt, LightFanoutKind, OutcomeProcessed);
            metrics.RecordNotificationFanoutSubscribers(createdThisAttempt, LightFanoutKind, OutcomeNotificationCreated);
            metrics.RecordNotificationFanoutSubscribers(duplicateSkippedThisAttempt, LightFanoutKind, OutcomeDuplicateSkipped);
            logger.LogError(
                ex,
                "Failed light moderation notification fanout run {RunId} for moderation record {ModerationRecordId}",
                run.Id,
                request.ModerationRecordId);
            throw;
        }
    }

    public async Task FanoutHeavyRedactionAsync(
        EventHeavyRedactedNotificationFanoutRequested request,
        CancellationToken cancellationToken = default)
    {
        if (request.TenantId == Guid.Empty
            || request.ModerationRecordId == Guid.Empty
            || request.Version != EventHeavyRedactedNotificationFanoutRequested.CurrentVersion)
        {
            throw new InvalidOperationException("The retained heavy moderation fanout pointer is invalid.");
        }

        Guid occurrenceId = Guid.CreateVersion7();
        Guid pointerOutboxMessageId = Guid.CreateVersion7();
        NotificationFanoutOccurrenceCoordinationResult result = await mutationLock.ExecuteOrderedGroupsAsync(
            [[GovernanceSettingKeys.Email.DeliveryEnabled]],
            policyToken => unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                EventModerationRecord moderationRecord = await moderationRecordRepository.GetByIdAsync(
                    request.TenantId,
                    request.ModerationRecordId,
                    token)
                    ?? throw new InvalidOperationException("The retained heavy moderation authority is unavailable.");
                if (moderationRecord.TenantId != request.TenantId
                    || moderationRecord.ActionKind != EventModerationActionKind.HeavyRedacted
                    || !moderationRecord.IsIrreversible)
                {
                    throw new InvalidOperationException("The retained heavy moderation authority is invalid.");
                }

                Guid aggregateVersion = moderationRecord.SourceReportDecisionId ?? moderationRecord.Id;
                NotificationFanoutOccurrence? existingOccurrence = await fanoutOccurrenceRepository
                    .GetBySourceIdentityForCoordinationAsync(
                        moderationRecord.TenantId,
                        "event_moderation_record",
                        moderationRecord.Id,
                        aggregateVersion,
                        token);
                DateTime occurredAt = moderationRecord.CreatedAt.UtcDateTime;
                return await fanoutCoordinator.CoordinateInCurrentTransactionAsync(
                    new NotificationFanoutOccurrenceCandidate(
                        existingOccurrence?.Id ?? occurrenceId,
                        pointerOutboxMessageId,
                        moderationRecord.TenantId,
                        moderationRecord.EventId,
                        SessionId: null,
                        occurredAt,
                        AudienceCutoffAt: occurredAt,
                        aggregateVersion,
                        ChangeSetJson: "{}",
                        SafeBeforeSnapshotJson: "{}",
                        SafeAfterSnapshotJson: "{}",
                        NotificationFanoutOccurrenceCoordinationPolicy.HeavyModerationUnavailableTemplateKey,
                        NotificationFanoutRecipientTemplateFactory.CurrentTemplateVersion,
                        (int)NotificationDeliveryPolicyEnum.ModerationAvailabilityRequired,
                        NotificationFanoutRecipientTemplateFactory.CurrentPolicyVersion,
                        RequestedNotBefore: occurredAt,
                        SourceType: "event_moderation_record",
                        SourceId: moderationRecord.Id),
                    token);
            },
            policyToken), cancellationToken);

        metrics.RecordNotificationFanoutRun(
            HeavyFanoutKind,
            result.Outcome == NotificationFanoutOccurrenceCoordinationOutcome.NewlyActive
                ? OutcomeCompleted
                : OutcomeSkippedCompleted);
        logger.LogInformation(
            "Retained heavy moderation pointer converged on occurrence {OccurrenceId} in tenant {TenantId} with outcome {Outcome}.",
            result.ActiveOccurrenceId,
            request.TenantId,
            result.Outcome);
    }

    private async Task<NotificationFanoutRun> GetOrCreateRunAsync(
        EventLightModeratedNotificationFanoutRequested request,
        CancellationToken cancellationToken)
    {
        var run = await fanoutRunRepository.GetBySourceAsync(
            request.TenantId,
            LightFanoutKind,
            (int)NotificationEntityTypeEnum.Event,
            request.ModerationRecordId,
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
            FanoutKind = LightFanoutKind,
            NotificationEntityTypeId = (int)NotificationEntityTypeEnum.Event,
            NotificationEntityType = null!,
            EntityId = request.ModerationRecordId,
            SourceActorId = request.SourceActorId,
            SourceActor = null!,
            Status = StatusProcessing,
            StartedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            ConcurrencyStamp = Guid.CreateVersion7()
        });
    }

    private static Notification CreateLightModerationNotification(
        EventLightModeratedNotificationFanoutRequested request,
        Guid userId,
        string deduplicationKey)
    {
        return new Notification
        {
            Id = Guid.CreateVersion7(),
            TenantId = request.TenantId,
            Tenant = null!,
            UserId = userId,
            User = null!,
            NotificationTypeId = (int)NotificationTypeEnum.EventUpdated,
            NotificationType = null!,
            Title = $"Event moderated: {request.EventTitle}",
            Body = "This event is temporarily unavailable while it is under moderation.",
            DeduplicationKey = deduplicationKey,
            NotificationEntityTypeId = (int)NotificationEntityTypeEnum.Event,
            EntityId = request.EventId.ToString(),
            NotificationScopeId = (int)ActorTypeEnum.User,
            NotificationScope = null!,
            SourceActorId = request.SourceActorId,
            RecipientContextActorId = null,
            NotificationReasonId = (int)NotificationReasonEnum.System,
            CreatedAt = DateTime.UtcNow
        };
    }

    private static string BuildLightModerationDeduplicationKey(
        EventLightModeratedNotificationFanoutRequested request,
        Guid userId)
    {
        return $"event-moderated-light:{request.TenantId:N}:{request.ModerationRecordId:N}:{userId:N}";
    }

    private sealed record LightModerationBatchResult(
        bool BlockedByHeavyAuthority,
        int ProcessedCount,
        int CreatedCount,
        int DuplicateSkippedCount,
        Guid? CursorSubscriberTenantUserId)
    {
        public static LightModerationBatchResult Blocked { get; } = new(true, 0, 0, 0, null);
    }

}
