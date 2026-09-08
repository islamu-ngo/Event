using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.SupportAccess;
using Explore.Application.Features.SupportAccess.Requests.Commands;
using Explore.Application.Features.SupportAccess.Validators;
using Explore.Application.Responses;
using Explore.Application.Settings;
using Explore.Application.Settings.Groups;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Domain.Enums;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.SupportAccess.Handlers.Commands;

public sealed class StartSupportAccessSessionCommandHandler(
    IAdminContext adminContext,
    IHierarchicalSettingsResolver settingsResolver,
    ITenantRepository tenantRepository,
    ITenantUserRepository tenantUserRepository,
    ISupportAccessSessionRepository sessionRepository,
    ISupportAccessAuditEventRepository auditEventRepository,
    IUnitOfWork unitOfWork,
    BusinessMetrics metrics,
    ILogger<StartSupportAccessSessionCommandHandler> logger)
    : IRequestHandler<StartSupportAccessSessionCommand, SupportAccessSessionCommandResponseDto>
{
    public async Task<SupportAccessSessionCommandResponseDto> Handle(
        StartSupportAccessSessionCommand request,
        CancellationToken cancellationToken)
    {
        var validationResult = await new StartSupportAccessSessionCommandValidator()
            .ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return Failure(
                request.Mode,
                request.TargetTenantId,
                null,
                SupportAccessFailureCodes.ValidationFailed,
                "Support-access start request failed validation.",
                validationResult.Errors.Select(error => error.ErrorMessage));
        }

        var actorUserId = await adminContext.ResolveUserIdAsync(cancellationToken);
        if (!actorUserId.HasValue)
        {
            return Failure(
                request.Mode,
                request.TargetTenantId,
                null,
                SupportAccessFailureCodes.ActorNotResolved,
                "Support-access actor could not be resolved.",
                ["Session expired. Please sign in again."]);
        }

        var settings = await settingsResolver.ResolveGroupAsync<SupportAccessSettingGroup>(
            new SettingContext(),
            cancellationToken);
        if (!settings.Enabled)
        {
            return Failure(
                request.Mode,
                request.TargetTenantId,
                actorUserId,
                SupportAccessFailureCodes.Disabled,
                "Support access is disabled.",
                ["Support access is disabled by instance policy."]);
        }

        var writeMode = request.Mode == SupportAccessModeEnum.Write;
        if (writeMode && !settings.AllowWriteMode)
        {
            return Failure(
                request.Mode,
                request.TargetTenantId,
                actorUserId,
                SupportAccessFailureCodes.WriteModeDisabled,
                "Write-capable support access is disabled.",
                ["Write-capable support access is disabled by instance policy."]);
        }

        var maxDuration = settings.GetMaxDurationMinutes(writeMode);
        if (request.DurationMinutes > maxDuration)
        {
            return Failure(
                request.Mode,
                request.TargetTenantId,
                actorUserId,
                SupportAccessFailureCodes.DurationExceedsPolicy,
                "Support-access duration exceeds instance policy.",
                [$"Duration cannot exceed {maxDuration} minutes for {request.Mode} mode."]);
        }

        if (settings.RequireTicketReference && string.IsNullOrWhiteSpace(request.TicketReference))
        {
            return Failure(
                request.Mode,
                request.TargetTenantId,
                actorUserId,
                SupportAccessFailureCodes.TicketReferenceRequired,
                "Ticket reference is required.",
                ["A ticket or external reference is required before starting support access."]);
        }

        var tenant = await tenantRepository.GetById(request.TargetTenantId);
        if (tenant is null)
        {
            return Failure(
                request.Mode,
                request.TargetTenantId,
                actorUserId,
                SupportAccessFailureCodes.TargetTenantNotFound,
                "Target tenant was not found.",
                ["Target tenant was not found."]);
        }

        if (request.TargetTenantUserId.HasValue)
        {
            var targetTenantUser = await tenantUserRepository.GetById(request.TargetTenantUserId.Value);
            if (targetTenantUser is null || targetTenantUser.TenantId != request.TargetTenantId)
            {
                return Failure(
                    request.Mode,
                    request.TargetTenantId,
                    actorUserId,
                    SupportAccessFailureCodes.TargetTenantUserMismatch,
                    "Target tenant user was not found in the target tenant.",
                    ["Target tenant user was not found in the target tenant."]);
            }
        }

        var nowUtc = DateTimeOffset.UtcNow;
        if (await sessionRepository.HasActiveSessionForActorAsync(actorUserId.Value, nowUtc, cancellationToken))
        {
            return Failure(
                request.Mode,
                request.TargetTenantId,
                actorUserId,
                SupportAccessFailureCodes.ActiveSessionExists,
                "An active support-access session already exists for this actor.",
                ["Stop the existing support-access session before starting another one."]);
        }

        var session = await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var startedAtUtc = DateTimeOffset.UtcNow;
            var supportSession = SupportAccessSession.Start(
                actorUserId.Value,
                request.TargetTenantId,
                request.Mode,
                request.ReasonCode,
                request.ReasonText,
                request.TicketReference ?? string.Empty,
                startedAtUtc,
                startedAtUtc.AddMinutes(request.DurationMinutes),
                request.TargetTenantUserId);

            var persisted = await sessionRepository.CreateAsync(supportSession, ct);
            var auditEvent = SupportAccessAuditEvent.CreateLifecycleEvent(
                persisted,
                SupportAccessAuditEventTypeEnum.Started,
                "started",
                startedAtUtc);
            await auditEventRepository.CreateAsync(auditEvent, ct);

            return persisted;
        }, cancellationToken);

        metrics.RecordSupportAccessLifecycleEvent("started", request.Mode.ToString(), "succeeded");
        if (request.Mode == SupportAccessModeEnum.Write)
        {
            logger.LogWarning(
                "Write-capable support-access session started sessionId={SupportAccessSessionId} actorUserId={ActorUserId} targetTenantId={TargetTenantId} targetTenantUserId={TargetTenantUserId} expiresAtUtc={ExpiresAtUtc}",
                session.Id,
                session.ActorUserId,
                session.TargetTenantId,
                session.TargetTenantUserId,
                session.ExpiresAtUtc);
        }
        else
        {
            logger.LogInformation(
                "Support-access session started sessionId={SupportAccessSessionId} actorUserId={ActorUserId} targetTenantId={TargetTenantId} targetTenantUserId={TargetTenantUserId} mode={Mode} expiresAtUtc={ExpiresAtUtc}",
                session.Id,
                session.ActorUserId,
                session.TargetTenantId,
                session.TargetTenantUserId,
                request.Mode,
                session.ExpiresAtUtc);
        }

        return SupportAccessSessionCommandResponseDto.Success(
            session.Id,
            "Support-access session started.",
            SupportAccessMapper.ToDto(session, DateTimeOffset.UtcNow));
    }

    private SupportAccessSessionCommandResponseDto Failure(
        SupportAccessModeEnum mode,
        Guid targetTenantId,
        Guid? actorUserId,
        string failureCode,
        string message,
        IEnumerable<string> errors)
    {
        metrics.RecordSupportAccessLifecycleEvent("started", mode.ToString(), "failed", failureCode);
        logger.LogWarning(
            "Support-access start denied failureCode={FailureCode} mode={Mode} actorUserId={ActorUserId} targetTenantId={TargetTenantId}",
            failureCode,
            mode,
            actorUserId,
            targetTenantId);

        return SupportAccessSessionCommandResponseDto.Failure(
            BaseCommandResponse.Failure<Guid>(failureCode, message, errors));
    }
}
