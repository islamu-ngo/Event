using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.SupportAccess;
using Explore.Application.Features.SupportAccess.Requests.Commands;
using Explore.Application.Features.SupportAccess.Validators;
using Explore.Application.Responses;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Domain.Enums;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.SupportAccess.Handlers.Commands;

public sealed class StopSupportAccessSessionCommandHandler(
    IAdminContext adminContext,
    ISupportAccessSessionRepository sessionRepository,
    ISupportAccessAuditEventRepository auditEventRepository,
    IUnitOfWork unitOfWork,
    BusinessMetrics metrics,
    ILogger<StopSupportAccessSessionCommandHandler> logger)
    : IRequestHandler<StopSupportAccessSessionCommand, SupportAccessSessionCommandResponseDto>
{
    public async Task<SupportAccessSessionCommandResponseDto> Handle(
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

        var actorUserId = await adminContext.ResolveUserIdAsync(cancellationToken);
        if (!actorUserId.HasValue)
        {
            return Failure(
                request.SessionId,
                null,
                SupportAccessFailureCodes.ActorNotResolved,
                "Support-access actor could not be resolved.",
                ["Session expired. Please sign in again."]);
        }

        var session = await sessionRepository.GetOwnedSessionAsync(
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

        var stopped = await StopSessionAsync(session, request.EndReasonText, cancellationToken);
        var lifecycleEvent = stopped.StatusId == (int)SupportAccessSessionStatusEnum.Expired
            ? "expired"
            : "stopped";
        metrics.RecordSupportAccessLifecycleEvent(
            lifecycleEvent,
            ((SupportAccessModeEnum)stopped.ModeId).ToString(),
            "succeeded");
        logger.LogInformation(
            "Support-access session {LifecycleEvent} sessionId={SupportAccessSessionId} actorUserId={ActorUserId} targetTenantId={TargetTenantId} status={Status} endedAtUtc={EndedAtUtc}",
            lifecycleEvent,
            stopped.Id,
            stopped.ActorUserId,
            stopped.TargetTenantId,
            (SupportAccessSessionStatusEnum)stopped.StatusId,
            stopped.EndedAtUtc);

        return SupportAccessSessionCommandResponseDto.Success(
            stopped.Id,
            "Support-access session stopped.",
            SupportAccessMapper.ToDto(stopped, DateTimeOffset.UtcNow));
    }

    private async Task<SupportAccessSession> StopSessionAsync(
        SupportAccessSession session,
        string? endReasonText,
        CancellationToken cancellationToken)
    {
        return await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var stoppedAtUtc = DateTimeOffset.UtcNow;
            if (!session.IsActiveAt(stoppedAtUtc))
            {
                if (session.StatusId == (int)SupportAccessSessionStatusEnum.Active)
                {
                    session.Expire(stoppedAtUtc, "Expired before stop request.");
                    await sessionRepository.UpdateAsync(session, ct);
                    var expiredAuditEvent = SupportAccessAuditEvent.CreateLifecycleEvent(
                        session,
                        SupportAccessAuditEventTypeEnum.Expired,
                        "expired",
                        stoppedAtUtc);
                    await auditEventRepository.CreateAsync(expiredAuditEvent, ct);
                }

                return session;
            }

            session.Stop(stoppedAtUtc, endReasonText);
            await sessionRepository.UpdateAsync(session, ct);
            var auditEvent = SupportAccessAuditEvent.CreateLifecycleEvent(
                session,
                SupportAccessAuditEventTypeEnum.Stopped,
                "stopped",
                stoppedAtUtc);
            await auditEventRepository.CreateAsync(auditEvent, ct);

            return session;
        }, cancellationToken);
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
            "Support-access stop denied failureCode={FailureCode} sessionId={SupportAccessSessionId} actorUserId={ActorUserId}",
            failureCode,
            sessionId,
            actorUserId);

        return SupportAccessSessionCommandResponseDto.Failure(
            BaseCommandResponse.Failure<Guid>(failureCode, message, errors));
    }
}
