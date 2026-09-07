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

public sealed class ForceStopSupportAccessSessionCommandHandler(
    IAdminContext adminContext,
    ISupportAccessSessionRepository sessionRepository,
    ISupportAccessAuditEventRepository auditEventRepository,
    IUnitOfWork unitOfWork,
    BusinessMetrics metrics,
    ILogger<ForceStopSupportAccessSessionCommandHandler> logger)
    : IRequestHandler<ForceStopSupportAccessSessionCommand, SupportAccessSessionCommandResponseDto>
{
    public async Task<SupportAccessSessionCommandResponseDto> Handle(
        ForceStopSupportAccessSessionCommand request,
        CancellationToken cancellationToken)
    {
        var validationResult = await new ForceStopSupportAccessSessionCommandValidator()
            .ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return Failure(
                request.SessionId,
                null,
                null,
                SupportAccessFailureCodes.ValidationFailed,
                "Support-access force-stop request failed validation.",
                validationResult.Errors.Select(error => error.ErrorMessage));
        }

        var operatorUserId = await adminContext.ResolveUserIdAsync(cancellationToken);
        if (!operatorUserId.HasValue)
        {
            return Failure(
                request.SessionId,
                null,
                null,
                SupportAccessFailureCodes.ActorNotResolved,
                "Support-access operator could not be resolved.",
                ["Session expired. Please sign in again."]);
        }

        var session = await sessionRepository.GetByIdAsync(request.SessionId, cancellationToken);
        if (session is null)
        {
            return Failure(
                request.SessionId,
                operatorUserId,
                null,
                SupportAccessFailureCodes.SessionNotFound,
                "Support-access session was not found.",
                ["Support-access session was not found."]);
        }

        var nowUtc = DateTimeOffset.UtcNow;
        if (!session.IsActiveAt(nowUtc))
        {
            return Failure(
                request.SessionId,
                operatorUserId,
                ((SupportAccessModeEnum)session.ModeId).ToString(),
                SupportAccessFailureCodes.SessionNotActive,
                "Support-access session is not active.",
                ["Only active support-access sessions can be force-stopped."]);
        }

        var revoked = await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var revokedAtUtc = DateTimeOffset.UtcNow;
            session.Revoke(revokedAtUtc, SupportAccessEndReasonEnum.ForceStopped, request.EndReasonText);
            await sessionRepository.UpdateAsync(session, ct);
            var auditEvent = SupportAccessAuditEvent.Create(
                session.Id,
                SupportAccessAuditEventTypeEnum.Revoked,
                operatorUserId.Value,
                session.TargetTenantId,
                "force_stopped",
                revokedAtUtc,
                session.TargetTenantUserId);
            await auditEventRepository.CreateAsync(auditEvent, ct);

            return session;
        }, cancellationToken);

        metrics.RecordSupportAccessLifecycleEvent(
            "force_stopped",
            ((SupportAccessModeEnum)revoked.ModeId).ToString(),
            "succeeded");
        logger.LogWarning(
            "Support-access session force-stopped sessionId={SupportAccessSessionId} actorUserId={ActorUserId} operatorUserId={OperatorUserId} targetTenantId={TargetTenantId} mode={Mode} endedAtUtc={EndedAtUtc}",
            revoked.Id,
            revoked.ActorUserId,
            operatorUserId,
            revoked.TargetTenantId,
            (SupportAccessModeEnum)revoked.ModeId,
            revoked.EndedAtUtc);

        return SupportAccessSessionCommandResponseDto.Success(
            revoked.Id,
            "Support-access session force-stopped.",
            SupportAccessMapper.ToDto(revoked, DateTimeOffset.UtcNow));
    }

    private SupportAccessSessionCommandResponseDto Failure(
        Guid sessionId,
        Guid? operatorUserId,
        string? mode,
        string failureCode,
        string message,
        IEnumerable<string> errors)
    {
        metrics.RecordSupportAccessLifecycleEvent("force_stopped", mode, "failed", failureCode);
        logger.LogWarning(
            "Support-access force-stop denied failureCode={FailureCode} sessionId={SupportAccessSessionId} operatorUserId={OperatorUserId}",
            failureCode,
            sessionId,
            operatorUserId);

        return SupportAccessSessionCommandResponseDto.Failure(
            BaseCommandResponse.Failure<Guid>(failureCode, message, errors));
    }
}
