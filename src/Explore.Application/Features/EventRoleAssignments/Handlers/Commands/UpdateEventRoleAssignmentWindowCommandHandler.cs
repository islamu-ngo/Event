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
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.EventRoleAssignments.Handlers.Commands;

public sealed class UpdateEventRoleAssignmentWindowCommandHandler
    : EventRoleAssignmentCommandHandlerBase,
      ICommandHandler<UpdateEventRoleAssignmentWindowCommand, BaseCommandResponse<Guid>>
{
    private readonly IEventRoleAssignmentRepository _assignmentRepository;
    private readonly IEventRepository _eventRepository;
    private readonly IEventRoleAuthorityCeilingService _authorityCeilingService;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly BusinessMetrics _metrics;
    private readonly ILogger<UpdateEventRoleAssignmentWindowCommandHandler> _logger;
    private readonly IEventRoleAssignmentWindowMutationBoundary _mutation;

    public UpdateEventRoleAssignmentWindowCommandHandler(
        IEventRoleAssignmentRepository assignmentRepository,
        IEventRepository eventRepository,
        IEventRoleAuthorityCeilingService authorityCeilingService,
        IAuditLogRepository auditLogRepository,
        BusinessMetrics metrics,
        ILogger<UpdateEventRoleAssignmentWindowCommandHandler> logger,
        IAdminContext identity,
        IMachinePrincipalAccessor machine,
        ITenantContext tenant,
        IAuthorizationProvider authorization,
        IEventRoleAssignmentWindowMutationBoundary mutation) : base(identity, machine, tenant, authorization)
    {
        _assignmentRepository = assignmentRepository;
        _eventRepository = eventRepository;
        _authorityCeilingService = authorityCeilingService;
        _auditLogRepository = auditLogRepository;
        _metrics = metrics;
        _logger = logger;
        _mutation = mutation;
    }

    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(UpdateEventRoleAssignmentWindowCommand request, CancellationToken cancellationToken = default)
    {
        var actorUserId = await RequireActorAsync(request.TenantId, request.EventId, AuthorizationActions.Events.ManageTeam, cancellationToken);
        var auditId = Guid.CreateVersion7();
        int roleId = 0;
        var result = await _mutation.ExecuteAsync(request.TenantId, request.EventId, request.AssignmentId,
            request.StartsAtUtc, request.ExpiresAtUtc, auditId,
            (actor, token) => UpdateWindowAsync(request, actor, auditId, role => roleId = role, token), cancellationToken);
        if (result.IsSuccess)
        {
            _metrics.RecordEventRoleAssignmentChanged("update-window", "allowed", RoleCodeFor(roleId));
            _logger.LogInformation(
                "Event role assignment window committed TenantId={TenantId} EventId={EventId} AssignmentId={AssignmentId} RoleId={RoleId} ActorUserId={ActorUserId} StartsAtUtc={StartsAtUtc} ExpiresAtUtc={ExpiresAtUtc}",
                request.TenantId, request.EventId, request.AssignmentId, roleId, actorUserId, request.StartsAtUtc, request.ExpiresAtUtc);
        }
        return result;
    }

    private async Task<BaseCommandResponse<Guid>> UpdateWindowAsync(UpdateEventRoleAssignmentWindowCommand request,
        Guid actorUserId, Guid auditId, Action<int> observeRole, CancellationToken cancellationToken)
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
            request.TenantId, request.EventId, actorUserId, assignment.RoleId, cancellationToken);

        if (!authority.IsAllowed)
        {
            _metrics.RecordEventRoleAssignmentChanged("update-window", "denied", RoleCodeFor(assignment.RoleId));
            _logger.LogWarning(
                "Event role assignment window update denied: {DenyReason}",
                authority.FailureCode ?? EventRoleAuthorityFailureCodes.AuthorityMissing);
            await WriteAuditAsync(
                _auditLogRepository,
                request.TenantId,
                assignment.Id,
                "EventRoleAssignmentWindowUpdateDenied",
                actorUserId,
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
                }, auditId: auditId);
            return AuthorityFailure(authority);
        }

        var previousStartsAtUtc = assignment.StartsAtUtc;
        var previousExpiresAtUtc = assignment.ExpiresAtUtc;
        var previousVersion = assignment.Version;
        observeRole(assignment.RoleId);
        assignment.UpdateValidityWindow(request.StartsAtUtc, request.ExpiresAtUtc, DateTime.UtcNow);
        await _assignmentRepository.Update(assignment);
        await WriteAuditAsync(
            _auditLogRepository,
            assignment.TenantId,
            assignment.Id,
            "EventRoleAssignmentWindowUpdated",
            actorUserId,
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
            affectedColumns: new[] { nameof(EventRoleAssignment.StartsAtUtc), nameof(EventRoleAssignment.ExpiresAtUtc) }, auditId: auditId);

        return Success(assignment.Id, "Event role assignment updated successfully.");
    }
}
