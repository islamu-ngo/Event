// ABOUTME: Handles irreversible heavy moderation by redacting event-owned content and triggering image deletion.
// ABOUTME: Writes safe moderation history transactionally and invalidates caches only after commit.

using Explore.Application.Authorization;
using Explore.Application.Caching;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.Events.Moderation;
using Explore.Application.Features.Events.Requests.Commands;
using Explore.Application.Features.Federation.Atproto.Services;
using Explore.Application.Notifications;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Federation;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.Events.Handlers.Commands;

public sealed class HeavyRedactEventCommandHandler(
    IEventHeavyRedactionRepository redactionRepository,
    IEventModerationRecordRepository moderationRecordRepository,
    INotificationFanoutOccurrenceRepository fanoutOccurrenceRepository,
    NotificationFanoutOccurrenceCoordinator fanoutCoordinator,
    IEventLifecycleScheduler eventLifecycleScheduler,
    IStorageObjectDeletionService storageObjectDeletionService,
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    HybridCache cache,
    BusinessMetrics metrics,
    ILogger<HeavyRedactEventCommandHandler> logger,
    AtprotoEventPublicationPlanner atprotoPublicationPlanner,
    TimeProvider timeProvider,
    ISettingMutationLock mutationLock) : IRequestHandler<HeavyRedactEventCommand, BaseCommandResponse<Guid>>
{
    private const int ImmediateDeletionBatchSize = 100;
    private const string ActionKind = "heavy_redacted";
    private const string FanoutSourceType = "event_moderation_record";

    public async Task<BaseCommandResponse<Guid>> Handle(HeavyRedactEventCommand request, CancellationToken cancellationToken)
    {
        // Reason metadata is normalized here rather than at the transport boundary so every caller of this
        // command — HTTP, MCP, or an internal moderation flow — is held to the same audit-code shape.
        if (!EventModerationReasonCodePolicy.TryNormalizeHeavy(
                request.ReasonCode,
                request.CorrelationId,
                out var reasonMetadata,
                out var reasonFailureCode,
                out var reasonError))
        {
            return BaseCommandResponse.Failure<Guid>(
                reasonFailureCode!,
                reasonError,
                [reasonError ?? "Moderation metadata is invalid."],
                request.Id);
        }

        var moderatorUserId = currentUserService.UserId;
        if (moderatorUserId is null && !HasSourceReportDecision(request))
        {
            metrics.RecordEventModerationAction(null, ActionKind, "failed", "user_unresolved", irreversible: true);
            logger.LogWarning(
                "Heavy event moderation rejected because the authenticated moderator user could not be resolved for event {EventId}.",
                request.Id);

            return Failure(
                request.Id,
                "Moderator user could not be resolved.",
                ["Authenticated moderator user id is required."],
                HeavyRedactEventCommand.UserResolutionFailureCode);
        }

        var tenantId = Guid.Empty;
        var eventId = Guid.Empty;
        var wasIdempotent = false;
        EventModerationRecord? moderationRecordForLog = null;
        var moderationRecordId = Guid.CreateVersion7();
        var federationOutboxId = Guid.CreateVersion7();
        DateTimeOffset redactedAt = timeProvider.GetUtcNow();
        var federationCreatedAt = redactedAt.UtcDateTime;
        var occurrenceId = Guid.CreateVersion7();
        var pointerOutboxMessageId = Guid.CreateVersion7();
        var shouldInvalidateCache = false;
        var eventNotFound = false;
        var transactionResponse = await mutationLock.ExecuteOrderedGroupsAsync(
            [[GovernanceSettingKeys.Email.DeliveryEnabled]],
            policyToken => unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var graph = await redactionRepository.GetForUpdateAsync(request.Id, token);
            if (graph is null)
            {
                eventNotFound = true;
                metrics.RecordEventModerationAction(null, ActionKind, "failed", "not_found", irreversible: true);
                return Failure(request.Id, "Event not found.", ["Event not found."]);
            }

            var @event = graph.Event;
            tenantId = @event.TenantId;
            eventId = @event.Id;
            var latestRecord = await moderationRecordRepository.GetLatestByEventAsync(
                @event.TenantId,
                @event.Id,
                token);

            EventModerationRecord moderationRecord;
            if (latestRecord?.ActionKind == EventModerationActionKind.HeavyRedacted)
            {
                wasIdempotent = true;
                moderationRecord = latestRecord;
            }
            else
            {
                moderationRecord = EventModerationRecord.CreateHeavyRedaction(
                    moderationRecordId,
                    @event.TenantId,
                    @event.Id,
                    moderatorUserId,
                    reasonMetadata.ReasonCode,
                    @event.EventStatusId,
                    reasonMetadata.CorrelationId,
                    redactedAt);

                if (!TryLinkSourceReportDecision(moderationRecord, request.SourceReportId, request.SourceReportDecisionId, out var sourceLinkError))
                {
                    return Failure(
                        @event.Id,
                        "Source report decision link is invalid.",
                        [sourceLinkError],
                        "event_heavy_redaction_source_report_decision_invalid");
                }

                EventHeavyRedactionApplicator.Apply(graph, moderatorUserId, redactedAt);

                await redactionRepository.SaveChangesAsync(token);
                await atprotoPublicationPlanner.PlanEventAsync(
                    new AtprotoEventPublicationInput(
                        @event.TenantId,
                        moderatorUserId ?? Guid.Empty,
                        @event.Id,
                        @event.ConcurrencyStamp,
                        PdsSyncOperation.Delete,
                        federationOutboxId,
                        federationCreatedAt),
                    token);
                await moderationRecordRepository.Create(moderationRecord);

                shouldInvalidateCache = true;
            }

            await EnsureHeavyModerationFanoutOccurrenceAsync(
                @event,
                moderationRecord,
                occurrenceId,
                pointerOutboxMessageId,
                token);
            moderationRecordForLog = moderationRecord;

            return Success(
                @event.Id,
                wasIdempotent ? "Event is already heavy-redacted." : "Event heavy-redacted successfully.");
        }, policyToken), cancellationToken);

        if (!transactionResponse.IsSuccess)
        {
            if (eventNotFound)
            {
                logger.LogWarning(
                    "Heavy event moderation rejected because event {EventId} was not found.",
                    request.Id);
            }

            return transactionResponse;
        }

        if (wasIdempotent)
        {
            logger.LogInformation(
                "Heavy event moderation skipped because event {EventId} in tenant {TenantId} is already heavy-redacted; delete-requested image cleanup will still be checked.",
                eventId,
                tenantId);
        }

        if (shouldInvalidateCache)
        {
            await cache.RemoveAsync($"event:detail:{eventId}", cancellationToken);
            await cache.RemoveByTagAsync(CacheTags.EventListByTenant(tenantId), cancellationToken);
        }

        var deletionResult = await storageObjectDeletionService.DeleteRequestedForResourceAsync(
            tenantId,
            ResourceKinds.Event,
            eventId,
            moderatorUserId,
            ImmediateDeletionBatchSize,
            cancellationToken);

        if (!deletionResult.CompletedWithoutFailures)
        {
            metrics.RecordEventModerationAction(tenantId.ToString(), ActionKind, "pending_storage_deletion", "storage_deletion_pending", irreversible: true);
            logger.LogWarning(
                "Heavy event moderation completed with pending image deletion for event {EventId} in tenant {TenantId}; scanned {ScannedCount}, deleted {DeletedCount}, missing-key deleted {MissingKeyDeletedCount}, failed {FailedCount}.",
                eventId,
                tenantId,
                deletionResult.ScannedCount,
                deletionResult.DeletedCount,
                deletionResult.MissingKeyDeletedCount,
                deletionResult.FailedCount);

            return Failure(
                eventId,
                "Event heavy-redacted; image deletion is pending retry.",
                ["One or more image objects could not be deleted immediately and remain queued for retry."],
                HeavyRedactEventCommand.StorageDeletionPendingFailureCode);
        }

        metrics.RecordEventModerationAction(
            tenantId.ToString(),
            ActionKind,
            wasIdempotent ? "idempotent" : "succeeded",
            irreversible: true);
        logger.LogInformation(
            "Heavy event moderation {Outcome} for event {EventId} in tenant {TenantId}; moderation record {ModerationRecordId}, scanned {ScannedCount}, deleted {DeletedCount}, missing-key deleted {MissingKeyDeletedCount}.",
            wasIdempotent ? "idempotent" : "succeeded",
            eventId,
            tenantId,
            moderationRecordForLog?.Id,
            deletionResult.ScannedCount,
            deletionResult.DeletedCount,
            deletionResult.MissingKeyDeletedCount);

        return transactionResponse;
    }

    private static BaseCommandResponse<Guid> Success(Guid id, string message) =>
        BaseCommandResponse.Success(id, message);

    private static BaseCommandResponse<Guid> Failure(
        Guid id,
        string message,
        IEnumerable<string> errors,
        string? failureCode = null) => failureCode is null
            ? BaseCommandResponse.Validation(errors, message, id)
            : BaseCommandResponse.Failure(failureCode, message, errors, id);

    private static bool TryLinkSourceReportDecision(
        EventModerationRecord moderationRecord,
        Guid? sourceReportId,
        Guid? sourceReportDecisionId,
        out string error)
    {
        error = string.Empty;
        if (sourceReportId is null && sourceReportDecisionId is null)
        {
            return true;
        }

        if (sourceReportId is not { } reportId || sourceReportDecisionId is not { } decisionId)
        {
            error = "SourceReportId and SourceReportDecisionId must be provided together.";
            return false;
        }

        if (reportId == Guid.Empty || decisionId == Guid.Empty)
        {
            error = "Source report and decision ids cannot be empty.";
            return false;
        }

        moderationRecord.LinkSourceReportDecision(reportId, decisionId);
        return true;
    }

    private static bool HasSourceReportDecision(HeavyRedactEventCommand request) =>
        request.SourceReportId.HasValue && request.SourceReportDecisionId.HasValue;

    private async Task EnsureHeavyModerationFanoutOccurrenceAsync(
        Explore.Domain.Event @event,
        EventModerationRecord moderationRecord,
        Guid candidateOccurrenceId,
        Guid pointerOutboxMessageId,
        CancellationToken cancellationToken)
    {
        if (moderationRecord.TenantId != @event.TenantId
            || moderationRecord.EventId != @event.Id
            || moderationRecord.ActionKind != EventModerationActionKind.HeavyRedacted
            || !moderationRecord.IsIrreversible)
        {
            throw new InvalidOperationException("Heavy moderation fanout requires the authoritative irreversible moderation record.");
        }

        Guid aggregateVersion = moderationRecord.SourceReportDecisionId ?? moderationRecord.Id;
        NotificationFanoutOccurrence? existingOccurrence = await fanoutOccurrenceRepository
            .GetBySourceIdentityForCoordinationAsync(
                @event.TenantId,
                FanoutSourceType,
                moderationRecord.Id,
                aggregateVersion,
                cancellationToken);
        DateTime occurredAt = moderationRecord.CreatedAt.UtcDateTime;
        await fanoutCoordinator.CoordinateInCurrentTransactionAsync(
            new NotificationFanoutOccurrenceCandidate(
                existingOccurrence?.Id ?? candidateOccurrenceId,
                pointerOutboxMessageId,
                @event.TenantId,
                @event.Id,
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
                FanoutSourceType,
                moderationRecord.Id),
            cancellationToken);
        await eventLifecycleScheduler.SuppressEventRemindersInCurrentTransactionAsync(
            new EventReminderSuppressionInput(
                @event.TenantId,
                @event.Id,
                RegistrationOrderId: null,
                SessionId: null,
                occurredAt,
                "event_heavy_moderation_unavailable"),
            cancellationToken);
    }
}
