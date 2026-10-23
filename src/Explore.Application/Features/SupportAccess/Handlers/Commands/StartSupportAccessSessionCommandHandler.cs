using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
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
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.SupportAccess.Handlers.Commands;

public sealed class StartSupportAccessSessionCommandHandler(
    IAdminContext adminContext,
    IHierarchicalSettingsResolver settingsResolver,
    ITenantRepository tenantRepository,
    ITenantUserRepository tenantUserRepository,
    ISupportAccessSessionRepository sessionRepository,
    ISupportAccessAuditEventRepository auditEventRepository,
    IHumanSupportAdministrationPreparation preparation,
    ISerializableAttemptOwner owner,
    BusinessMetrics metrics,
    ILogger<StartSupportAccessSessionCommandHandler> logger)
    : ICommandHandler<StartSupportAccessSessionCommand, SupportAccessSessionCommandResponseDto>
{
    public async Task<SupportAccessSessionCommandResponseDto> ExecuteAsync(
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

        var prepared = await preparation.PrepareAsync(request, cancellationToken);
        var result = await owner.ExecuteAsync(async token =>
        {
            await prepared.AcquireAsync(request, token);
            return await ExecuteBodyAsync(request, prepared, token);
        }, cancellationToken);
        if (result.IsSuccess)
        {
            metrics.RecordSupportAccessLifecycleEvent("started", request.Mode.ToString(), "succeeded");
            if (request.Mode == SupportAccessModeEnum.Write)
                logger.LogWarning("Write-capable support-access session started.");
            else
                logger.LogInformation("Support-access session started mode={Mode}", request.Mode);
        }
        return result;
    }

    private async Task<SupportAccessSessionCommandResponseDto> ExecuteBodyAsync(
        StartSupportAccessSessionCommand request, IPreparedHumanSupportAdministration prepared,
        CancellationToken cancellationToken)
    {
        var actorUserId = prepared.NativeData is not null
            ? prepared.PrincipalId : await adminContext.ResolveUserIdAsync(cancellationToken);
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

        if (actorUserId != prepared.PrincipalId)
            throw new UnauthorizedAccessException("Support start actor differs from the original human.");

        var settings = prepared.NativeData is not null ? prepared.Settings : await settingsResolver.ResolveGroupAsync<SupportAccessSettingGroup>(
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

        var nativeTargets = prepared.NativeData is { } targetData
            ? await targetData.ReadStartTargetsAsync(prepared.TransitionAt, cancellationToken) : null;
        bool tenantExists = nativeTargets?.TenantExists
            ?? await tenantRepository.GetById(request.TargetTenantId) is not null;
        if (!tenantExists)
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
            var targetTenantUser = nativeTargets is null
                ? await tenantUserRepository.GetById(request.TargetTenantUserId.Value) : null;
            bool matches = nativeTargets?.TenantUserMatches
                ?? (targetTenantUser is not null && targetTenantUser.TenantId == request.TargetTenantId);
            if (!matches)
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

        var nowUtc = prepared.TransitionAt;
        if (nativeTargets?.ActorHasActiveSession
            ?? await sessionRepository.HasActiveSessionForActorAsync(actorUserId.Value, nowUtc, cancellationToken))
        {
            return Failure(
                request.Mode,
                request.TargetTenantId,
                actorUserId,
                SupportAccessFailureCodes.ActiveSessionExists,
                "An active support-access session already exists for this actor.",
                ["Stop the existing support-access session before starting another one."]);
        }

        var session = prepared.StartSession();
        if (prepared.NativeData is null)
            session = await sessionRepository.CreateAsync(session, cancellationToken);
        var auditEvent = SupportAccessAuditEvent.CreateLifecycleEvent(
            session, SupportAccessAuditEventTypeEnum.Started, "started", prepared.TransitionAt);
        if (prepared.NativeData is { } data)
            await data.SaveStartedAsync(session, auditEvent, prepared.TransitionAt, cancellationToken);
        else
            await auditEventRepository.CreateAsync(auditEvent, cancellationToken);
        prepared.Complete(session, auditEvent);

        return SupportAccessSessionCommandResponseDto.Success(
            session.Id,
            "Support-access session started.",
            SupportAccessMapper.ToDto(session, prepared.TransitionAt));
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
            "Support-access start denied failureCode={FailureCode} mode={Mode}",
            failureCode,
            Enum.IsDefined(mode) ? mode.ToString() : "unknown");

        return SupportAccessSessionCommandResponseDto.Failure(
            BaseCommandResponse.Failure<Guid>(failureCode, message, errors));
    }
}
