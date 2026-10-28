using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.EventRoleAssignments.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.EventRoleAssignments.Handlers.Commands;

public sealed class TransferEventOwnershipCommandHandler
    : EventRoleAssignmentCommandHandlerBase,
      ICommandHandler<TransferEventOwnershipCommand, BaseCommandResponse<Guid>>
{
    private readonly IEventRoleAssignmentRepository _assignmentRepository;
    private readonly IEventRepository _eventRepository;
    private readonly IUserRepository _userRepository;
    private readonly IEventAuthoritySnapshotService _snapshotService;
    private readonly IEventOwnershipTransferMutationBoundary _mutation;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly BusinessMetrics _metrics;
    private readonly ILogger<TransferEventOwnershipCommandHandler> _logger;

    public TransferEventOwnershipCommandHandler(
        IEventRoleAssignmentRepository assignmentRepository,
        IEventRepository eventRepository,
        IUserRepository userRepository,
        IEventAuthoritySnapshotService snapshotService,
        IAuditLogRepository auditLogRepository,
        BusinessMetrics metrics,
        ILogger<TransferEventOwnershipCommandHandler> logger,
        IAdminContext identity,
        IMachinePrincipalAccessor machine,
        ITenantContext tenant,
        IAuthorizationProvider authorization,
        IEventOwnershipTransferMutationBoundary mutation) : base(identity, machine, tenant, authorization)
    {
        _assignmentRepository = assignmentRepository;
        _eventRepository = eventRepository;
        _userRepository = userRepository;
        _snapshotService = snapshotService;
        _mutation = mutation;
        _auditLogRepository = auditLogRepository;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(TransferEventOwnershipCommand request, CancellationToken cancellationToken = default)
    {
        var actorUserId = await RequireActorAsync(request.TenantId, request.EventId, AuthorizationActions.Events.TransferOwnership, cancellationToken);
        var replacementId = Guid.CreateVersion7();
        var auditId = Guid.CreateVersion7();
        try
        {
            var result = await _mutation.ExecuteAsync(request.TenantId, request.EventId, request.CurrentOwnerAssignmentId,
                request.NewOwnerUserId, auditId,
                (actor, token) => TransferAsync(request, actor, replacementId, auditId, token), cancellationToken);
            if (result.IsSuccess)
            {
                _metrics.RecordEventRoleAssignmentChanged("transfer-ownership", "allowed", RoleCodeFor((int)RoleEnum.EventOwner));
                _logger.LogInformation(
                    "Event ownership transferred TenantId={TenantId} EventId={EventId} CurrentOwnerAssignmentId={CurrentOwnerAssignmentId} NewOwnerAssignmentId={NewOwnerAssignmentId} NewOwnerUserId={NewOwnerUserId} ActorUserId={ActorUserId}",
                    request.TenantId, request.EventId, request.CurrentOwnerAssignmentId, result.Id, request.NewOwnerUserId, actorUserId);
            }
            return result;
        }
        catch (InvalidOperationException ex)
        {
            // The native owner has rolled back both assignments before this denial is recorded.
            _metrics.RecordEventRoleAssignmentChanged("transfer-ownership", "denied", RoleCodeFor((int)RoleEnum.EventOwner));
            _logger.LogWarning(
                "Event ownership transfer invalid TenantId={TenantId} EventId={EventId} CurrentOwnerAssignmentId={CurrentOwnerAssignmentId} NewOwnerUserId={NewOwnerUserId} ActorUserId={ActorUserId} DenyReason={DenyReason}",
                request.TenantId, request.EventId, request.CurrentOwnerAssignmentId, request.NewOwnerUserId, actorUserId,
                "event_ownership_transfer_invalid");
            await WriteAuditAsync(_auditLogRepository, request.TenantId, request.CurrentOwnerAssignmentId,
                "EventOwnershipTransferDenied", actorUserId, oldValues: null,
                newValues: new
                {
                    request.EventId,
                    request.CurrentOwnerAssignmentId,
                    request.NewOwnerUserId,
                    ActorUserId = actorUserId,
                    Operation = "transfer-ownership",
                    DecisionEngine = "application_owner_invariant",
                    ResourceKind = ResourceKinds.Event,
                    Action = AuthorizationActions.Events.TransferOwnership,
                    DenyReason = "event_ownership_transfer_invalid"
                });
            return Failure(ex.Message, "event_ownership_transfer_invalid");
        }
    }

    private async Task<BaseCommandResponse<Guid>> TransferAsync(TransferEventOwnershipCommand request, Guid actorUserId,
        Guid replacementId, Guid auditId, CancellationToken cancellationToken)
    {
        var @event = await GetEventInTenantAsync(_eventRepository, request.TenantId, request.EventId);
        if (@event is null)
        {
            return Failure("Event not found for the requested tenant.", "event_not_found");
        }

        if (!await UserExistsAsync(_userRepository, request.NewOwnerUserId))
        {
            return Failure("New owner user not found.", "target_user_not_found");
        }

        if (!await HasOwnershipTransferAuthorityAsync(
                _snapshotService, request.TenantId, request.EventId, actorUserId, cancellationToken))
        {
            _metrics.RecordEventRoleAssignmentChanged("transfer-ownership", "denied", RoleCodeFor((int)RoleEnum.EventOwner));
            _logger.LogWarning(
                "Event ownership transfer denied: {DenyReason}",
                "event_ownership_transfer_forbidden");
            await WriteAuditAsync(
                _auditLogRepository,
                request.TenantId,
                request.CurrentOwnerAssignmentId,
                "EventOwnershipTransferDenied",
                actorUserId,
                oldValues: null,
                newValues: new
                {
                    request.EventId,
                    request.CurrentOwnerAssignmentId,
                    request.NewOwnerUserId,
                    ActorUserId = actorUserId,
                    Operation = "transfer-ownership",
                    DecisionEngine = "application_snapshot_authority",
                    ResourceKind = ResourceKinds.Event,
                    Action = AuthorizationActions.Events.TransferOwnership,
                    DenyReason = "event_ownership_transfer_forbidden"
                });
            return Failure("You do not have ownership transfer authority for this event.", "event_ownership_transfer_forbidden");
        }

        var utcNow = DateTime.UtcNow;
        var currentOwner = await _assignmentRepository.GetById(request.CurrentOwnerAssignmentId);
        if (currentOwner is null ||
            currentOwner.TenantId != request.TenantId ||
            currentOwner.EventId != request.EventId ||
            currentOwner.RoleId != (int)RoleEnum.EventOwner ||
            !currentOwner.IsEffectiveAt(utcNow))
        {
            throw new InvalidOperationException("Current owner assignment is not effective for this event.");
        }

        if (currentOwner.UserId == request.NewOwnerUserId)
        {
            throw new InvalidOperationException("New owner must be different from the current owner.");
        }

        var existingNewOwner = await _assignmentRepository.GetOpenByEventUserRoleAsync(
            request.TenantId,
            request.EventId,
            request.NewOwnerUserId,
            (int)RoleEnum.EventOwner,
            cancellationToken);

        EventRoleAssignment newOwner;
        if (existingNewOwner is null)
        {
            newOwner = EventRoleAssignment.Create(
                request.TenantId,
                request.EventId,
                request.NewOwnerUserId,
                (int)RoleEnum.EventOwner,
                EventRoleAssignmentStatus.Active,
                request.StartsAtUtc,
                request.ExpiresAtUtc,
                actorUserId);

            newOwner.Id = replacementId;
            newOwner = await _assignmentRepository.Create(newOwner);
        }
        else
        {
            if (existingNewOwner.Status == EventRoleAssignmentStatus.Pending)
            {
                existingNewOwner.Activate(utcNow);
            }

            existingNewOwner.UpdateValidityWindow(request.StartsAtUtc, request.ExpiresAtUtc, utcNow);
            await _assignmentRepository.Update(existingNewOwner);
            newOwner = existingNewOwner;
        }

        if (!newOwner.IsEffectiveAt(utcNow))
        {
            throw new InvalidOperationException("Replacement owner assignment must be effective at transfer time.");
        }

        currentOwner.Revoke(actorUserId, utcNow);
        await _assignmentRepository.Update(currentOwner);
        await WriteAuditAsync(
            _auditLogRepository,
            request.TenantId,
            newOwner.Id,
            "EventOwnershipTransferred",
            actorUserId,
            oldValues: new
            {
                CurrentOwnerAssignmentId = currentOwner.Id,
                CurrentOwnerUserId = currentOwner.UserId,
                CurrentOwnerStatus = EventRoleAssignmentStatus.Active,
                CurrentOwnerVersion = currentOwner.Version - 1
            },
            newValues: new
            {
                request.EventId,
                NewOwnerAssignmentId = newOwner.Id,
                NewOwnerUserId = newOwner.UserId,
                NewOwnerStatus = newOwner.Status,
                RevokedOwnerAssignmentId = currentOwner.Id,
                RevokedOwnerStatus = currentOwner.Status,
                Operation = "transfer-ownership"
            },
            affectedColumns: new[] { nameof(EventRoleAssignment.Status), nameof(EventRoleAssignment.RevokedAtUtc), nameof(EventRoleAssignment.RevokedByUserId) },
            auditId: auditId);
        return Success(newOwner.Id, "Event ownership transferred successfully.");
    }
}
