using System.Text.Json;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Admissions;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;

namespace Explore.Application.Services.Registration;

public sealed class AdmissionCheckInOperationsService(
    IAuthorizationProvider authorization,
    IAdmissionTargetOperationsRepository targets,
    IAdmissionCheckInHealthProbe healthProbe,
    IAuditLogRepository auditLogs,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public async Task<AdmissionCheckInHealthResult?> GetHealthAsync(
        AdmissionCheckInHealthRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await GetHealthCoreAsync(request, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AdmissionCheckInUnavailableException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new AdmissionCheckInUnavailableException();
        }
    }

    private async Task<AdmissionCheckInHealthResult?> GetHealthCoreAsync(
        AdmissionCheckInHealthRequest request,
        CancellationToken cancellationToken)
    {
        if (!Valid(request) ||
            !await IsAuthorizedAsync(
                request.TenantId,
                request.EventId,
                request.StaffActorId,
                AuthorizationActions.Events.EventCheckInView,
                cancellationToken))
        {
            return null;
        }

        bool available = await healthProbe.IsAvailableAsync(cancellationToken);
        if (!available)
        {
            return new AdmissionCheckInHealthResult(
                request.TargetId,
                AdmissionCheckInOperationalStatus.Unavailable,
                AdmissionCheckInDependencyStatus.Unavailable);
        }

        AdmissionTarget? target = await targets.GetAsync(
            request.TenantId,
            request.EventId,
            request.TargetId,
            cancellationToken);
        return target is null
            ? null
            : new AdmissionCheckInHealthResult(
                target.Id,
                Status(target),
                AdmissionCheckInDependencyStatus.Available);
    }

    public async Task<AdmissionCheckInOperationalResult?> ExecuteAsync(
        AdmissionCheckInOperationalRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ExecuteCoreAsync(request, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AdmissionCheckInUnavailableException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new AdmissionCheckInUnavailableException();
        }
    }

    private async Task<AdmissionCheckInOperationalResult?> ExecuteCoreAsync(
        AdmissionCheckInOperationalRequest request,
        CancellationToken cancellationToken)
    {
        if (!Valid(request) ||
            !await IsAuthorizedAsync(
                request.TenantId,
                request.EventId,
                request.StaffActorId,
                AuthorizationActions.Events.EventCheckInManage,
                cancellationToken))
        {
            return null;
        }

        return await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            AdmissionTarget? target = await targets.GetAsync(
                request.TenantId,
                request.EventId,
                request.TargetId,
                token);
            if (target is null ||
                target.TenantId != request.TenantId ||
                target.EventId != request.EventId ||
                target.Id != request.TargetId)
            {
                return null;
            }

            switch (request.Action)
            {
                case AdmissionCheckInOperationalAction.Stop:
                    target.Stop();
                    await targets.UpdateAsync(target, token);
                    break;
                case AdmissionCheckInOperationalAction.Restore:
                    target.Restore();
                    await targets.UpdateAsync(target, token);
                    break;
                case AdmissionCheckInOperationalAction.Reconcile:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(request));
            }

            DateTimeOffset occurredAtUtc = timeProvider.GetUtcNow();
            await auditLogs.Create(new AuditLog
            {
                Id = Guid.CreateVersion7(),
                TenantId = request.TenantId,
                Tenant = null!,
                EntityType = nameof(AdmissionTarget),
                EntityId = target.Id.ToString("D"),
                Action = $"admission_check_in.{request.Action.ToString().ToLowerInvariant()}",
                NewValues = JsonSerializer.Serialize(new
                {
                    Action = request.Action.ToString(),
                    ReasonCode = request.ReasonCode.ToString(),
                    Status = Status(target).ToString()
                }),
                AffectedColumns = "[]",
                ActorId = request.StaffActorId,
                Timestamp = occurredAtUtc.UtcDateTime
            });
            return new AdmissionCheckInOperationalResult(
                target.Id,
                request.Action,
                Status(target),
                request.ReasonCode,
                occurredAtUtc);
        }, cancellationToken);
    }

    private async Task<bool> IsAuthorizedAsync(
        Guid tenantId,
        Guid eventId,
        Guid staffActorId,
        string action,
        CancellationToken cancellationToken)
    {
        AuthorizationDecision decision = await authorization.AuthorizeAsync(
            new AuthorizationRequest(
                ResourceKinds.Event,
                eventId.ToString("D"),
                action,
                new AuthorizationScope(TenantId: tenantId.ToString("D")),
                new EventScopedAuthorizationFacts(tenantId, eventId),
                new AuthorizationSubject(staffActorId)),
            cancellationToken);
        return decision.IsAllowed;
    }

    private static AdmissionCheckInOperationalStatus Status(AdmissionTarget target) =>
        target.IsOperational
            ? AdmissionCheckInOperationalStatus.Active
            : AdmissionCheckInOperationalStatus.Stopped;

    private static bool Valid(AdmissionCheckInHealthRequest? request) =>
        request is not null &&
        ValidIdentities(request.TenantId, request.EventId, request.TargetId, request.StaffActorId);

    private static bool Valid(AdmissionCheckInOperationalRequest? request) =>
        request is not null &&
        ValidIdentities(request.TenantId, request.EventId, request.TargetId, request.StaffActorId) &&
        Enum.IsDefined(request.Action) &&
        Enum.IsDefined(request.ReasonCode);

    private static bool ValidIdentities(params Guid[] values) =>
        values.All(value => value != Guid.Empty && value.Version == 7);
}
