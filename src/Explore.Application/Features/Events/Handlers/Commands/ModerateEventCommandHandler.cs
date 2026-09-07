using Explore.Application.Caching;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Events.Requests.Commands;
using Explore.Application.Features.Federation.Atproto.Services;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Federation;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Explore.Application.Features.Events.Moderation;

namespace Explore.Application.Features.Events.Handlers.Commands;

public sealed class ModerateEventCommandHandler(
    IEventRepository eventRepository,
    IEventSessionRepository eventSessionRepository,
    IEventModerationRecordRepository moderationRecordRepository,
    IOutboxRepository outboxRepository,
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    HybridCache cache,
    BusinessMetrics metrics,
    ILogger<ModerateEventCommandHandler> logger,
    AtprotoEventPublicationPlanner atprotoPublicationPlanner,
    TimeProvider timeProvider) : IRequestHandler<ModerateEventCommand, BaseCommandResponse<Guid>>
{
    private const string InvalidStatusFailureCode = "event_light_moderation_invalid_status";
    private const string UserResolutionFailureCode = "event_light_moderation_user_unresolved";
    private const string ActionKind = "light_moderated";

    public async Task<BaseCommandResponse<Guid>> Handle(ModerateEventCommand request, CancellationToken cancellationToken)
    {
        // Reason metadata is normalized here rather than at the transport boundary so every caller of this
        // command — HTTP, MCP, or an internal moderation flow — is held to the same audit-code shape.
        if (!EventModerationReasonCodePolicy.TryNormalizeLight(
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
            metrics.RecordEventModerationAction(null, ActionKind, "failed", "user_unresolved", irreversible: false);
            logger.LogWarning(
                "Light event moderation rejected because the authenticated moderator user could not be resolved for event {EventId}.",
                request.Id);

            return Failure(
                request.Id,
                "Moderator user could not be resolved.",
                ["Authenticated moderator user id is required."],
                UserResolutionFailureCode);
        }

        Guid moderationRecordId = Guid.CreateVersion7();
        Guid notificationOutboxMessageId = Guid.CreateVersion7();
        Guid federationOutboxId = Guid.CreateVersion7();
        DateTimeOffset moderatedAt = timeProvider.GetUtcNow();
        DateTime federationCreatedAt = moderatedAt.UtcDateTime;
        ModerationCommandResult? postCommitResult = null;
        ModerationCommandResult result = await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var @event = await eventRepository.GetById(request.Id);
            if (@event is null)
            {
                return new ModerationCommandResult(
                    Failure(request.Id, "Event not found.", ["Event not found."]),
                    null,
                    "failed",
                    "not_found",
                    LogOutcome: "not_found");
            }

            if (@event.EventStatusId == (int)EventStatusEnum.Moderated)
            {
                var repairedSessionCount = await CascadeModerationToSessionsAsync(@event.Id, moderatedAt.UtcDateTime);
                var attemptResult = new ModerationCommandResult(
                    Success(@event.Id, "Event is already moderated."),
                    @event.TenantId,
                    repairedSessionCount > 0 ? "idempotent" : null,
                    CacheEventId: repairedSessionCount > 0 ? @event.Id : null,
                    LogOutcome: "idempotent");
                if (repairedSessionCount > 0)
                {
                    postCommitResult ??= attemptResult;
                }

                return attemptResult;
            }

            if (@event.EventStatusId != (int)EventStatusEnum.Published)
            {
                return new ModerationCommandResult(Failure(
                    @event.Id,
                    "Only published events can be light moderated.",
                    ["Only published events can be light moderated."],
                    InvalidStatusFailureCode),
                    @event.TenantId,
                    "failed",
                    "invalid_status",
                    LogOutcome: "invalid_status",
                    CurrentStatusId: @event.EventStatusId);
            }

            var moderationRecord = EventModerationRecord.CreateLightModeration(
                moderationRecordId,
                @event.TenantId,
                @event.Id,
                moderatorUserId,
                reasonMetadata.ReasonCode,
                @event.EventStatusId,
                reasonMetadata.CorrelationId,
                moderatedAt);

            if (!TryLinkSourceReportDecision(moderationRecord, request.SourceReportId, request.SourceReportDecisionId, out var sourceLinkError))
            {
                return new ModerationCommandResult(Failure(
                    @event.Id,
                    "Source report decision link is invalid.",
                    [sourceLinkError],
                    "event_light_moderation_source_report_decision_invalid"),
                    @event.TenantId,
                    "failed",
                    "source_report_decision_invalid");
            }

            @event.ApplyLightModeration(moderatedAt.UtcDateTime);

            await CascadeModerationToSessionsAsync(@event.Id, moderatedAt.UtcDateTime);
            await moderationRecordRepository.Create(moderationRecord);
            await eventRepository.Update(@event);
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
            await outboxRepository.Create(EventModerationOutboxMessageFactory.CreateLightModerationNotificationFanoutMessage(
                notificationOutboxMessageId,
                @event,
                moderationRecord));

            var mutationResult = new ModerationCommandResult(
                Success(@event.Id, "Event moderated successfully."),
                @event.TenantId,
                "succeeded",
                CacheEventId: @event.Id,
                LogOutcome: "succeeded",
                ModerationRecord: moderationRecord);
            postCommitResult ??= mutationResult;
            return mutationResult;
        }, cancellationToken);

        if (result.Response.IsSuccess)
        {
            result = postCommitResult ?? result;
        }
        else if (postCommitResult is not null)
        {
            result = result with { MetricOutcome = null };
        }

        await ApplyPostCommitEffectsAsync(result, request.Id, moderatorUserId, cancellationToken);
        return result.Response;
    }

    private async Task ApplyPostCommitEffectsAsync(
        ModerationCommandResult result,
        Guid requestedEventId,
        Guid? moderatorUserId,
        CancellationToken cancellationToken)
    {
        if (result.CacheEventId is { } eventId && result.TenantId is { } tenantId)
        {
            await cache.RemoveAsync($"event:detail:{eventId}", cancellationToken);
            await cache.RemoveByTagAsync(CacheTags.EventListByTenant(tenantId), cancellationToken);
        }

        if (result.MetricOutcome is not null)
        {
            metrics.RecordEventModerationAction(
                result.TenantId?.ToString(),
                ActionKind,
                result.MetricOutcome,
                result.FailureReason,
                irreversible: false);
        }

        switch (result.LogOutcome)
        {
            case "not_found":
                logger.LogWarning(
                    "Light event moderation rejected because event {EventId} was not found.",
                    requestedEventId);
                break;
            case "idempotent":
                logger.LogInformation(
                    "Light event moderation skipped because event {EventId} in tenant {TenantId} is already moderated.",
                    result.Response.Id,
                    result.TenantId);
                break;
            case "invalid_status":
                logger.LogWarning(
                    "Light event moderation rejected for event {EventId} in tenant {TenantId} because current status {CurrentStatusId} is not Published.",
                    result.Response.Id,
                    result.TenantId,
                    result.CurrentStatusId);
                break;
            case "succeeded":
                EventModerationRecord moderationRecord = result.ModerationRecord!;
                logger.LogInformation(
                    "Light event moderation succeeded for event {EventId} in tenant {TenantId}; moderation record {ModerationRecordId}, moderator {ModeratorUserId}, reason {ReasonCode}, correlation {CorrelationId}.",
                    result.Response.Id,
                    result.TenantId,
                    moderationRecord.Id,
                    moderatorUserId,
                    moderationRecord.ReasonCode,
                    moderationRecord.CorrelationId);
                break;
        }
    }

    private async Task<int> CascadeModerationToSessionsAsync(Guid eventId, DateTime moderatedAtUtc)
    {
        var sessions = await eventSessionRepository.GetSessionsByEvent(eventId);
        var updatedCount = 0;

        foreach (var session in sessions)
        {
            if (!session.ApplyParentModeration(moderatedAtUtc))
            {
                continue;
            }

            await eventSessionRepository.Update(session);
            updatedCount++;
        }

        return updatedCount;
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

    private static bool HasSourceReportDecision(ModerateEventCommand request) =>
        request.SourceReportId.HasValue && request.SourceReportDecisionId.HasValue;

    private sealed record ModerationCommandResult(
        BaseCommandResponse<Guid> Response,
        Guid? TenantId,
        string? MetricOutcome,
        string? FailureReason = null,
        Guid? CacheEventId = null,
        string? LogOutcome = null,
        int? CurrentStatusId = null,
        EventModerationRecord? ModerationRecord = null);
}
