using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.SupportAccess;
using Explore.Application.Features.SupportAccess.Requests.Commands;
using Explore.Application.Features.SupportAccess.Validators;
using Explore.Application.Responses;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.SupportAccess.Handlers.Commands;

public sealed class StopSupportAccessSessionCommandHandler(
    IAdminContext adminContext,
    ISupportAccessSessionRepository sessionRepository,
    ISupportAccessAuditEventRepository auditEventRepository,
    IHumanSupportAdministrationPreparation preparation,
    ISerializableAttemptOwner owner,
    BusinessMetrics metrics,
    ILogger<StopSupportAccessSessionCommandHandler> logger)
    : ICommandHandler<StopSupportAccessSessionCommand, SupportAccessSessionCommandResponseDto>
{
    public async Task<SupportAccessSessionCommandResponseDto> ExecuteAsync(
        StopSupportAccessSessionCommand request,
        CancellationToken cancellationToken)
    {
        var validationResult = await new StopSupportAccessSessionCommandValidator()
            .ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return Failure(
                request.SessionId,
                null,
                SupportAccessFailureCodes.ValidationFailed,
                "Support-access stop request failed validation.",
                validationResult.Errors.Select(error => error.ErrorMessage));
        }

        var prepared = await preparation.PrepareAsync(request, cancellationToken);
        var result = await owner.ExecuteAsync(async token =>
        {
            await prepared.AcquireAsync(request, token);
            return await ExecuteBodyAsync(request, prepared, token);
        }, cancellationToken);
        if (result.IsSuccess && result.Session is { } stopped)
        {
            var lifecycleEvent = stopped.StatusId == (int)SupportAccessSessionStatusEnum.Expired
                ? "expired" : "stopped";
            metrics.RecordSupportAccessLifecycleEvent(lifecycleEvent,
                ((SupportAccessModeEnum)stopped.ModeId).ToString(), "succeeded");
            logger.LogInformation("Support-access session {LifecycleEvent} status={Status}",
                lifecycleEvent, (SupportAccessSessionStatusEnum)stopped.StatusId);
        }
        return result;
    }

    private async Task<SupportAccessSessionCommandResponseDto> ExecuteBodyAsync(
        StopSupportAccessSessionCommand request, IPreparedHumanSupportAdministration prepared,
        CancellationToken cancellationToken)
    {
        var actorUserId = prepared.NativeData is not null
            ? prepared.PrincipalId : await adminContext.ResolveUserIdAsync(cancellationToken);
        if (!actorUserId.HasValue)
        {
            return Failure(
                request.SessionId,
                null,
                SupportAccessFailureCodes.ActorNotResolved,
                "Support-access actor could not be resolved.",
                ["Session expired. Please sign in again."]);
        }

        if (actorUserId != prepared.PrincipalId)
            throw new UnauthorizedAccessException("Support stop actor differs from the original human.");

        var session = prepared.NativeData is { } targetData
            ? await targetData.ReadSessionAsync(cancellationToken) : await sessionRepository.GetOwnedSessionAsync(
            request.SessionId,
            actorUserId.Value,
            cancellationToken);
        if (session is null)
        {
            return Failure(
                request.SessionId,
                actorUserId,
                SupportAccessFailureCodes.SessionNotFound,
                "Support-access session was not found.",
                ["Support-access session was not found."]);
        }

        if (prepared.EndSession(session))
        {
            if (prepared.NativeData is null)
                await sessionRepository.UpdateAsync(session, cancellationToken);
            bool expired = session.StatusId == (int)SupportAccessSessionStatusEnum.Expired;
            var auditEvent = SupportAccessAuditEvent.CreateLifecycleEvent(session,
                expired ? SupportAccessAuditEventTypeEnum.Expired : SupportAccessAuditEventTypeEnum.Stopped,
                expired ? "expired" : "stopped", prepared.TransitionAt);
            if (prepared.NativeData is { } data)
                await data.SaveOwnStoppedAsync(session, auditEvent, prepared.TransitionAt, cancellationToken);
            else
                await auditEventRepository.CreateAsync(auditEvent, cancellationToken);
            prepared.Complete(session, auditEvent);
        }

        return SupportAccessSessionCommandResponseDto.Success(
            session.Id,
            "Support-access session stopped.",
            SupportAccessMapper.ToDto(session, prepared.TransitionAt));
    }

    private SupportAccessSessionCommandResponseDto Failure(
        Guid sessionId,
        Guid? actorUserId,
        string failureCode,
        string message,
        IEnumerable<string> errors)
    {
        metrics.RecordSupportAccessLifecycleEvent("stopped", null, "failed", failureCode);
        logger.LogWarning(
            "Support-access stop denied failureCode={FailureCode}",
            failureCode);

        return SupportAccessSessionCommandResponseDto.Failure(
            BaseCommandResponse.Failure<Guid>(failureCode, message, errors));
    }
}
