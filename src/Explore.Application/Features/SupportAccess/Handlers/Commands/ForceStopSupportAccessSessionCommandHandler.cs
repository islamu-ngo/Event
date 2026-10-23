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

public sealed class ForceStopSupportAccessSessionCommandHandler(
    IAdminContext adminContext,
    ISupportAccessSessionRepository sessionRepository,
    ISupportAccessAuditEventRepository auditEventRepository,
    IHumanSupportAdministrationPreparation preparation,
    ISerializableAttemptOwner owner,
    BusinessMetrics metrics,
    ILogger<ForceStopSupportAccessSessionCommandHandler> logger)
    : ICommandHandler<ForceStopSupportAccessSessionCommand, SupportAccessSessionCommandResponseDto>
{
    public async Task<SupportAccessSessionCommandResponseDto> ExecuteAsync(
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

        var prepared = await preparation.PrepareAsync(request, cancellationToken);
        var result = await owner.ExecuteAsync(async token =>
        {
            await prepared.AcquireAsync(request, token);
            return await ExecuteBodyAsync(request, prepared, token);
        }, cancellationToken);
        if (result.IsSuccess && result.Session is { } revoked)
        {
            metrics.RecordSupportAccessLifecycleEvent("force_stopped",
                ((SupportAccessModeEnum)revoked.ModeId).ToString(), "succeeded");
            logger.LogWarning("Support-access session force-stopped mode={Mode}", (SupportAccessModeEnum)revoked.ModeId);
        }
        return result;
    }

    private async Task<SupportAccessSessionCommandResponseDto> ExecuteBodyAsync(
        ForceStopSupportAccessSessionCommand request, IPreparedHumanSupportAdministration prepared,
        CancellationToken cancellationToken)
    {
        var operatorUserId = prepared.NativeData is not null
            ? prepared.PrincipalId : await adminContext.ResolveUserIdAsync(cancellationToken);
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

        if (operatorUserId != prepared.PrincipalId)
            throw new UnauthorizedAccessException("Support operator differs from the original human.");

        var session = prepared.NativeData is { } targetData
            ? await targetData.ReadSessionAsync(cancellationToken)
            : await sessionRepository.GetByIdAsync(request.SessionId, cancellationToken);
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

        var nowUtc = prepared.TransitionAt;
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

        prepared.EndSession(session);
        if (prepared.NativeData is null)
            await sessionRepository.UpdateAsync(session, cancellationToken);
        var auditEvent = SupportAccessAuditEvent.Create(session.Id, SupportAccessAuditEventTypeEnum.Revoked,
            operatorUserId.Value, session.TargetTenantId, "force_stopped", prepared.TransitionAt, session.TargetTenantUserId);
        if (prepared.NativeData is { } data)
            await data.SaveForceStoppedAsync(session, auditEvent, prepared.TransitionAt, cancellationToken);
        else
            await auditEventRepository.CreateAsync(auditEvent, cancellationToken);
        prepared.Complete(session, auditEvent);

        return SupportAccessSessionCommandResponseDto.Success(
            session.Id,
            "Support-access session force-stopped.",
            SupportAccessMapper.ToDto(session, prepared.TransitionAt));
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
            "Support-access force-stop denied failureCode={FailureCode}",
            failureCode);

        return SupportAccessSessionCommandResponseDto.Failure(
            BaseCommandResponse.Failure<Guid>(failureCode, message, errors));
    }
}
