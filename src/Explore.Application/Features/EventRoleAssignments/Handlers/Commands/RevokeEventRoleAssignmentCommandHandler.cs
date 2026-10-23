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

public sealed class RevokeEventRoleAssignmentCommandHandler
    : EventRoleAssignmentCommandHandlerBase,
      ICommandHandler<RevokeEventRoleAssignmentCommand, BaseCommandResponse<Guid>>
{
    private readonly IEventRoleAssignmentRepository _assignmentRepository;
    private readonly IEventRepository _eventRepository;
    private readonly IEventRoleAuthorityCeilingService _authorityCeilingService;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly BusinessMetrics _metrics;
    private readonly ILogger<RevokeEventRoleAssignmentCommandHandler> _logger;
    private readonly IEventRoleAssignmentRevocationMutationBoundary _mutation;

    public RevokeEventRoleAssignmentCommandHandler(
        IEventRoleAssignmentRepository assignmentRepository,
        IEventRepository eventRepository,
        IEventRoleAuthorityCeilingService authorityCeilingService,
        IAuditLogRepository auditLogRepository,
        BusinessMetrics metrics,
        ILogger<RevokeEventRoleAssignmentCommandHandler> logger,
        IAdminContext identity,
        IMachinePrincipalAccessor machine,
        ITenantContext tenant,
        IAuthorizationProvider authorization,
        IEventRoleAssignmentRevocationMutationBoundary mutation) : base(identity, machine, tenant, authorization)
    {
        _assignmentRepository = assignmentRepository;
        _eventRepository = eventRepository;
        _authorityCeilingService = authorityCeilingService;
        _auditLogRepository = auditLogRepository;
        _metrics = metrics;
        _logger = logger;
        _mutation = mutation;
    }

    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(RevokeEventRoleAssignmentCommand request, CancellationToken cancellationToken = default)
    {
        var actorUserId = await RequireActorAsync(request.TenantId, request.EventId, AuthorizationActions.Events.ManageTeam, cancellationToken);
        var auditId = Guid.CreateVersion7();
        int roleId = 0;
        string? denialReason = null;
        Guid outcomeActor = actorUserId;
        var result = await _mutation.ExecuteAsync(request.TenantId, request.EventId, request.AssignmentId, auditId,
            (actor, token) =>
            {
                denialReason = null;
                outcomeActor = actor;
                return RevokeAsync(request, actor, auditId, role => roleId = role,
                    (role, reason) => { roleId = role; denialReason = reason; }, token);
            }, cancellationToken);
        if (result.IsSuccess)
        {
            _metrics.RecordEventRoleAssignmentChanged("revoke", "allowed", RoleCodeFor(roleId));
            _logger.LogInformation(
                "Event role assignment revocation committed TenantId={TenantId} EventId={EventId} AssignmentId={AssignmentId} RoleId={RoleId} ActorUserId={ActorUserId}",
                request.TenantId, request.EventId, request.AssignmentId, roleId, outcomeActor);
        }
        else if (denialReason is not null && result.FailureCode == denialReason)
        {
            _metrics.RecordEventRoleAssignmentChanged("revoke", "denied", RoleCodeFor(roleId));
            _logger.LogWarning(
                "Event role assignment revocation denied TenantId={TenantId} EventId={EventId} AssignmentId={AssignmentId} RoleId={RoleId} ActorUserId={ActorUserId} DenyReason={DenyReason}",
                request.TenantId, request.EventId, request.AssignmentId, roleId, outcomeActor, denialReason);
        }
        return result;
    }

    private async Task<BaseCommandResponse<Guid>> RevokeAsync(RevokeEventRoleAssignmentCommand request,
        Guid actorUserId, Guid auditId, Action<int> observeRole, Action<int, string> observeDenial, CancellationToken cancellationToken)
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
            observeDenial(assignment.RoleId, "event_owner_transfer_required");
            await WriteAuditAsync(
                _auditLogRepository,
                request.TenantId,
                assignment.Id,
                "EventRoleAssignmentRevokeDenied",
                actorUserId,
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
                }, auditId: auditId);
            return Failure(
                "EventOwner assignments cannot be revoked directly. Transfer ownership first.",
                "event_owner_transfer_required",
                assignment.Id);
        }

        var authority = await _authorityCeilingService.CanAssignRoleAsync(
            request.TenantId, request.EventId, actorUserId, assignment.RoleId, cancellationToken);

        if (!authority.IsAllowed)
        {
            observeDenial(assignment.RoleId, authority.FailureCode ?? EventRoleAuthorityFailureCodes.AuthorityMissing);
            await WriteAuditAsync(
                _auditLogRepository,
                request.TenantId,
                assignment.Id,
                "EventRoleAssignmentRevokeDenied",
                actorUserId,
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
                }, auditId: auditId);
            return AuthorityFailure(authority);
        }

        var previousStatus = assignment.Status;
        var previousVersion = assignment.Version;
        observeRole(assignment.RoleId);
        assignment.Revoke(actorUserId, DateTime.UtcNow);
        await _assignmentRepository.Update(assignment);
        await WriteAuditAsync(
            _auditLogRepository,
            assignment.TenantId,
            assignment.Id,
            "EventRoleAssignmentRevoked",
            actorUserId,
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
            affectedColumns: new[] { nameof(EventRoleAssignment.Status), nameof(EventRoleAssignment.RevokedAtUtc), nameof(EventRoleAssignment.RevokedByUserId) },
            auditId: auditId);

        return Success(assignment.Id, "Event role assignment revoked successfully.");
    }
}
