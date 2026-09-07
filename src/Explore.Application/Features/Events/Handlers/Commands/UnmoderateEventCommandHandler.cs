using Explore.Application.Caching;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Events.Requests.Commands;
using Explore.Application.Features.Federation.Atproto.Services;
using Explore.Application.Responses;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Federation;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Explore.Application.Features.Events.Moderation;

namespace Explore.Application.Features.Events.Handlers.Commands;

public sealed class UnmoderateEventCommandHandler(
    IEventRepository eventRepository,
    IEventModerationRecordRepository moderationRecordRepository,
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    HybridCache cache,
    BusinessMetrics metrics,
    ILogger<UnmoderateEventCommandHandler> logger,
    AtprotoEventPublicationPlanner atprotoPublicationPlanner,
    TimeProvider timeProvider) : IRequestHandler<UnmoderateEventCommand, BaseCommandResponse<Guid>>
{
    private const string InvalidStatusFailureCode = "event_unmoderation_invalid_status";
    private const string NotReversibleFailureCode = "event_unmoderation_not_reversible";
    private const string UserResolutionFailureCode = "event_unmoderation_user_unresolved";
    private const string ActionKind = "unmoderated";

    public async Task<BaseCommandResponse<Guid>> Handle(UnmoderateEventCommand request, CancellationToken cancellationToken)
    {
        // Reason metadata is normalized here rather than at the transport boundary so every caller of this
        // command — HTTP, MCP, or an internal moderation flow — is held to the same audit-code shape.
        if (!EventModerationReasonCodePolicy.TryNormalizeUnmoderation(
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

        if (currentUserService.UserId is not { } moderatorUserId)
        {
            metrics.RecordEventModerationAction(null, ActionKind, "failed", "user_unresolved", irreversible: false);
            logger.LogWarning(
                "Event unmoderation rejected because the authenticated moderator user could not be resolved for event {EventId}.",
                request.Id);

            return Failure(
                request.Id,
                "Moderator user could not be resolved.",
                ["Authenticated moderator user id is required."],
                UserResolutionFailureCode);
        }

        Guid unmoderationRecordId = Guid.CreateVersion7();
        Guid federationOutboxId = Guid.CreateVersion7();
        DateTimeOffset unmoderatedAt = timeProvider.GetUtcNow();
        DateTime federationCreatedAt = unmoderatedAt.UtcDateTime;
        UnmoderationCommandResult? postCommitResult = null;
        UnmoderationCommandResult result = await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var @event = await eventRepository.GetById(request.Id);
            if (@event is null)
            {
                return new UnmoderationCommandResult(
                    Failure(request.Id, "Event not found.", ["Event not found."]),
                    null,
                    "failed",
                    "not_found",
                    LogOutcome: "not_found");
            }

            if (@event.EventStatusId == (int)EventStatusEnum.Published)
            {
                return new UnmoderationCommandResult(
                    Success(@event.Id, "Event is already published."),
                    @event.TenantId,
                    null,
                    LogOutcome: "idempotent");
            }

            if (@event.EventStatusId != (int)EventStatusEnum.Moderated)
            {
                return new UnmoderationCommandResult(Failure(
                    @event.Id,
                    "Only moderated events can be unmoderated.",
                    ["Only moderated events can be unmoderated."],
                    InvalidStatusFailureCode),
                    @event.TenantId,
                    "failed",
                    "invalid_status",
                    LogOutcome: "invalid_status",
                    CurrentStatusId: @event.EventStatusId);
            }

            var latestModerationRecord = await moderationRecordRepository.GetLatestByEventAsync(
                @event.TenantId,
                @event.Id,
                token);

            if (latestModerationRecord?.AllowsUnmoderation != true)
            {
                return new UnmoderationCommandResult(Failure(
                    @event.Id,
                    "Only reversibly light-moderated events can be unmoderated.",
                    ["Only reversibly light-moderated events can be unmoderated."],
                    NotReversibleFailureCode),
                    @event.TenantId,
                    "failed",
                    "not_reversible",
                    LogOutcome: "not_reversible");
            }

            var unmoderationRecord = EventModerationRecord.CreateUnmoderation(
                unmoderationRecordId,
                latestModerationRecord,
                moderatorUserId,
                reasonMetadata.ReasonCode,
                reasonMetadata.CorrelationId,
                unmoderatedAt);

            if (!@event.RestoreAfterLightModeration(unmoderatedAt.UtcDateTime))
            {
                return new UnmoderationCommandResult(
                    Success(@event.Id, "Event is already published."),
                    @event.TenantId,
                    null,
                    LogOutcome: "idempotent");
            }

            await moderationRecordRepository.Create(unmoderationRecord);
            await eventRepository.Update(@event);
            await atprotoPublicationPlanner.PlanEventAsync(
                new AtprotoEventPublicationInput(
                    @event.TenantId,
                    moderatorUserId,
                    @event.Id,
                    @event.ConcurrencyStamp,
                    PdsSyncOperation.Create,
                    federationOutboxId,
                    federationCreatedAt,
                    RestoreOnly: true),
                token);

            var mutationResult = new UnmoderationCommandResult(
                Success(@event.Id, "Event unmoderated successfully."),
                @event.TenantId,
                "succeeded",
                CacheEventId: @event.Id,
                LogOutcome: "succeeded",
                ModerationRecord: unmoderationRecord);
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
        UnmoderationCommandResult result,
        Guid requestedEventId,
        Guid moderatorUserId,
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
                    "Event unmoderation rejected because event {EventId} was not found.",
                    requestedEventId);
                break;
            case "idempotent":
                logger.LogInformation(
                    "Event unmoderation skipped because event {EventId} in tenant {TenantId} is already published.",
                    result.Response.Id,
                    result.TenantId);
                break;
            case "invalid_status":
                logger.LogWarning(
                    "Event unmoderation rejected for event {EventId} in tenant {TenantId} because current status {CurrentStatusId} is not Moderated.",
                    result.Response.Id,
                    result.TenantId,
                    result.CurrentStatusId);
                break;
            case "not_reversible":
                logger.LogWarning(
                    "Event unmoderation rejected for event {EventId} in tenant {TenantId} because the latest moderation record is not reversible.",
                    result.Response.Id,
                    result.TenantId);
                break;
            case "succeeded":
                EventModerationRecord unmoderationRecord = result.ModerationRecord!;
                logger.LogInformation(
                    "Event unmoderation succeeded for event {EventId} in tenant {TenantId}; moderation record {ModerationRecordId}, source record {SourceModerationRecordId}, moderator {ModeratorUserId}, reason {ReasonCode}, correlation {CorrelationId}.",
                    result.Response.Id,
                    result.TenantId,
                    unmoderationRecord.Id,
                    unmoderationRecord.SourceModerationRecordId,
                    moderatorUserId,
                    unmoderationRecord.ReasonCode,
                    unmoderationRecord.CorrelationId);
                break;
        }
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

    private sealed record UnmoderationCommandResult(
        BaseCommandResponse<Guid> Response,
        Guid? TenantId,
        string? MetricOutcome,
        string? FailureReason = null,
        Guid? CacheEventId = null,
        string? LogOutcome = null,
        int? CurrentStatusId = null,
        EventModerationRecord? ModerationRecord = null);
}
