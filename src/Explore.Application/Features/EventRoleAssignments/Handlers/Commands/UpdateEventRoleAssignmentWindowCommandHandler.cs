using Explore.Application.Authorization;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.EventRoleAssignments.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Telemetry;
using Explore.Domain;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.EventRoleAssignments.Handlers.Commands;

public sealed class UpdateEventRoleAssignmentWindowCommandHandler
    : EventRoleAssignmentCommandHandlerBase,
      IRequestHandler<UpdateEventRoleAssignmentWindowCommand, BaseCommandResponse<Guid>>
{
    private readonly IEventRoleAssignmentRepository _assignmentRepository;
    private readonly IEventRepository _eventRepository;
    private readonly IEventRoleAuthorityCeilingService _authorityCeilingService;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly BusinessMetrics _metrics;
    private readonly ILogger<UpdateEventRoleAssignmentWindowCommandHandler> _logger;

    public UpdateEventRoleAssignmentWindowCommandHandler(
        IEventRoleAssignmentRepository assignmentRepository,
        IEventRepository eventRepository,
        IEventRoleAuthorityCeilingService authorityCeilingService,
        IAuditLogRepository auditLogRepository,
        BusinessMetrics metrics,
        ILogger<UpdateEventRoleAssignmentWindowCommandHandler> logger)
    {
        _assignmentRepository = assignmentRepository;
        _eventRepository = eventRepository;
        _authorityCeilingService = authorityCeilingService;
        _auditLogRepository = auditLogRepository;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(UpdateEventRoleAssignmentWindowCommand request, CancellationToken cancellationToken)
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

        var authority = await _authorityCeilingService.CanAssignRoleAsync(
            request.TenantId, request.EventId, request.ActorUserId, assignment.RoleId, cancellationToken);

        if (!authority.IsAllowed)
        {
            _metrics.RecordEventRoleAssignmentChanged("update-window", "denied", RoleCodeFor(assignment.RoleId));
            _logger.LogWarning(
                "Event role assignment window update denied TenantId={TenantId} EventId={EventId} AssignmentId={AssignmentId} TargetUserId={TargetUserId} RoleId={RoleId} ActorUserId={ActorUserId} DenyReason={DenyReason}",
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
                "EventRoleAssignmentWindowUpdateDenied",
                request.ActorUserId,
                oldValues: new
                {
                    assignment.EventId,
                    assignment.UserId,
                    assignment.RoleId,
                    assignment.StartsAtUtc,
                    assignment.ExpiresAtUtc,
                    assignment.Status,
                    assignment.Version
                },
                newValues: new
                {
                    Operation = "update-window",
                    DecisionEngine = "application_authority_ceiling",
                    ResourceKind = ResourceKinds.Event,
                    Action = AuthorizationActions.Events.ManageTeam,
                    DenyReason = authority.FailureCode ?? EventRoleAuthorityFailureCodes.AuthorityMissing
                });
            return AuthorityFailure(authority);
        }

        var previousStartsAtUtc = assignment.StartsAtUtc;
        var previousExpiresAtUtc = assignment.ExpiresAtUtc;
        var previousVersion = assignment.Version;
        assignment.UpdateValidityWindow(request.StartsAtUtc, request.ExpiresAtUtc, DateTime.UtcNow);
        await _assignmentRepository.Update(assignment);
        _metrics.RecordEventRoleAssignmentChanged("update-window", "allowed", RoleCodeFor(assignment.RoleId));
        _logger.LogInformation(
            "Event role assignment window updated TenantId={TenantId} EventId={EventId} AssignmentId={AssignmentId} TargetUserId={TargetUserId} RoleId={RoleId} ActorUserId={ActorUserId} PreviousStartsAtUtc={PreviousStartsAtUtc} NewStartsAtUtc={NewStartsAtUtc} PreviousExpiresAtUtc={PreviousExpiresAtUtc} NewExpiresAtUtc={NewExpiresAtUtc}",
            assignment.TenantId,
            assignment.EventId,
            assignment.Id,
            assignment.UserId,
            assignment.RoleId,
            request.ActorUserId,
            previousStartsAtUtc,
            assignment.StartsAtUtc,
            previousExpiresAtUtc,
            assignment.ExpiresAtUtc);
        await WriteAuditAsync(
            _auditLogRepository,
            assignment.TenantId,
            assignment.Id,
            "EventRoleAssignmentWindowUpdated",
            request.ActorUserId,
            oldValues: new
            {
                assignment.EventId,
                assignment.UserId,
                assignment.RoleId,
                StartsAtUtc = previousStartsAtUtc,
                ExpiresAtUtc = previousExpiresAtUtc,
                Version = previousVersion
            },
            newValues: new
            {
                assignment.EventId,
                assignment.UserId,
                assignment.RoleId,
                assignment.StartsAtUtc,
                assignment.ExpiresAtUtc,
                assignment.Version,
                Operation = "update-window"
            },
            affectedColumns: new[] { nameof(EventRoleAssignment.StartsAtUtc), nameof(EventRoleAssignment.ExpiresAtUtc) });

        return Success(assignment.Id, "Event role assignment updated successfully.");
    }
}
