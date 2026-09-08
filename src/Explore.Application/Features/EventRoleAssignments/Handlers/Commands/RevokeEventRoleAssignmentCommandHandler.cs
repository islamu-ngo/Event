using Explore.Application.Authorization;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.EventRoleAssignments.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Domain.Enums;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.EventRoleAssignments.Handlers.Commands;

public sealed class RevokeEventRoleAssignmentCommandHandler
    : EventRoleAssignmentCommandHandlerBase,
      IRequestHandler<RevokeEventRoleAssignmentCommand, BaseCommandResponse<Guid>>
{
    private readonly IEventRoleAssignmentRepository _assignmentRepository;
    private readonly IEventRepository _eventRepository;
    private readonly IEventRoleAuthorityCeilingService _authorityCeilingService;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly BusinessMetrics _metrics;
    private readonly ILogger<RevokeEventRoleAssignmentCommandHandler> _logger;

    public RevokeEventRoleAssignmentCommandHandler(
        IEventRoleAssignmentRepository assignmentRepository,
        IEventRepository eventRepository,
        IEventRoleAuthorityCeilingService authorityCeilingService,
        IAuditLogRepository auditLogRepository,
        BusinessMetrics metrics,
        ILogger<RevokeEventRoleAssignmentCommandHandler> logger)
    {
        _assignmentRepository = assignmentRepository;
        _eventRepository = eventRepository;
        _authorityCeilingService = authorityCeilingService;
        _auditLogRepository = auditLogRepository;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(RevokeEventRoleAssignmentCommand request, CancellationToken cancellationToken)
    {
        var assignment = await _assignmentRepository.GetById(request.AssignmentId);
        if (assignment is null || assignment.TenantId != request.TenantId || assignment.EventId != request.EventId)
        {
            return Failure("Event role assignment not found for the requested event.", "event_role_assignment_not_found", request.AssignmentId);
        }

        var @event = await GetEventInTenantAsync(_eventRepository, request.TenantId, request.EventId);
        if (@event is null)
        {
            return Failure("Event not found for the requested tenant.", "event_not_found");
        }

        if (assignment.RoleId == (int)RoleEnum.EventOwner)
        {
            _metrics.RecordEventRoleAssignmentChanged("revoke", "denied", RoleCodeFor(assignment.RoleId));
            _logger.LogWarning(
                "Event role assignment revoke denied TenantId={TenantId} EventId={EventId} AssignmentId={AssignmentId} TargetUserId={TargetUserId} RoleId={RoleId} ActorUserId={ActorUserId} DenyReason={DenyReason}",
                request.TenantId,
                request.EventId,
                assignment.Id,
                assignment.UserId,
                assignment.RoleId,
                request.ActorUserId,
                "event_owner_transfer_required");
            await WriteAuditAsync(
                _auditLogRepository,
                request.TenantId,
                assignment.Id,
                "EventRoleAssignmentRevokeDenied",
                request.ActorUserId,
                oldValues: new
                {
                    assignment.EventId,
                    assignment.UserId,
                    assignment.RoleId,
                    assignment.Status,
                    assignment.Version
                },
                newValues: new
                {
                    Operation = "revoke",
                    DecisionEngine = "application_owner_invariant",
                    ResourceKind = ResourceKinds.Event,
                    Action = AuthorizationActions.Events.ManageOwner,
                    DenyReason = "event_owner_transfer_required"
                });
            return Failure(
                "EventOwner assignments cannot be revoked directly. Transfer ownership first.",
                "event_owner_transfer_required",
                assignment.Id);
        }

        var authority = await _authorityCeilingService.CanAssignRoleAsync(
            request.TenantId, request.EventId, request.ActorUserId, assignment.RoleId, cancellationToken);

        if (!authority.IsAllowed)
        {
            _metrics.RecordEventRoleAssignmentChanged("revoke", "denied", RoleCodeFor(assignment.RoleId));
            _logger.LogWarning(
                "Event role assignment revoke denied TenantId={TenantId} EventId={EventId} AssignmentId={AssignmentId} TargetUserId={TargetUserId} RoleId={RoleId} ActorUserId={ActorUserId} DenyReason={DenyReason}",
                request.TenantId,
                request.EventId,
                assignment.Id,
                assignment.UserId,
                assignment.RoleId,
                request.ActorUserId,
                authority.FailureCode ?? EventRoleAuthorityFailureCodes.AuthorityMissing);
            await WriteAuditAsync(
                _auditLogRepository,
                request.TenantId,
                assignment.Id,
                "EventRoleAssignmentRevokeDenied",
                request.ActorUserId,
                oldValues: new
                {
                    assignment.EventId,
                    assignment.UserId,
                    assignment.RoleId,
                    assignment.Status,
                    assignment.Version
                },
                newValues: new
                {
                    Operation = "revoke",
                    DecisionEngine = "application_authority_ceiling",
                    ResourceKind = ResourceKinds.Event,
                    Action = AuthorizationActions.Events.ManageTeam,
                    DenyReason = authority.FailureCode ?? EventRoleAuthorityFailureCodes.AuthorityMissing
                });
            return AuthorityFailure(authority);
        }

        var previousStatus = assignment.Status;
        var previousVersion = assignment.Version;
        assignment.Revoke(request.ActorUserId, DateTime.UtcNow);
        await _assignmentRepository.Update(assignment);
        _metrics.RecordEventRoleAssignmentChanged("revoke", "allowed", RoleCodeFor(assignment.RoleId));
        _logger.LogInformation(
            "Event role assignment revoked TenantId={TenantId} EventId={EventId} AssignmentId={AssignmentId} TargetUserId={TargetUserId} RoleId={RoleId} ActorUserId={ActorUserId} PreviousStatus={PreviousStatus} NewStatus={NewStatus}",
            assignment.TenantId,
            assignment.EventId,
            assignment.Id,
            assignment.UserId,
            assignment.RoleId,
            request.ActorUserId,
            previousStatus,
            assignment.Status);
        await WriteAuditAsync(
            _auditLogRepository,
            assignment.TenantId,
            assignment.Id,
            "EventRoleAssignmentRevoked",
            request.ActorUserId,
            oldValues: new
            {
                assignment.EventId,
                assignment.UserId,
                assignment.RoleId,
                Status = previousStatus,
                Version = previousVersion
            },
            newValues: new
            {
                assignment.EventId,
                assignment.UserId,
                assignment.RoleId,
                assignment.Status,
                assignment.RevokedAtUtc,
                assignment.RevokedByUserId,
                assignment.Version,
                Operation = "revoke"
            },
            affectedColumns: new[] { nameof(EventRoleAssignment.Status), nameof(EventRoleAssignment.RevokedAtUtc), nameof(EventRoleAssignment.RevokedByUserId) });

        return Success(assignment.Id, "Event role assignment revoked successfully.");
    }
}
