using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.EventRoleAssignments.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.EventRoleAssignments.Handlers.Commands;

public sealed class AssignEventRoleCommandHandler
    : EventRoleAssignmentCommandHandlerBase,
      ICommandHandler<AssignEventRoleCommand, BaseCommandResponse<Guid>>,
      IHumanEventTeamAssignmentOwner
{
    private readonly IEventRoleAssignmentRepository _assignmentRepository;
    private readonly IEventRepository _eventRepository;
    private readonly IUserRepository _userRepository;
    private readonly IHumanEventTeamActionPreparation _preparation;
    private readonly ISerializableAttemptOwner _owner;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly BusinessMetrics _metrics;
    private readonly ILogger<AssignEventRoleCommandHandler> _logger;

    public AssignEventRoleCommandHandler(
        IEventRoleAssignmentRepository assignmentRepository,
        IEventRepository eventRepository,
        IUserRepository userRepository,
        IHumanEventTeamActionPreparation preparation,
        ISerializableAttemptOwner owner,
        IAuditLogRepository auditLogRepository,
        BusinessMetrics metrics,
        ILogger<AssignEventRoleCommandHandler> logger)
    {
        _assignmentRepository = assignmentRepository;
        _eventRepository = eventRepository;
        _userRepository = userRepository;
        _preparation = preparation;
        _owner = owner;
        _auditLogRepository = auditLogRepository;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(AssignEventRoleCommand request, CancellationToken cancellationToken = default)
    {
        var prepared = await _preparation.PrepareAsync(request, cancellationToken);
        return await _owner.ExecuteAsync(async token =>
        {
            await prepared.AcquireAsync(request, token);
            return await ExecuteBodyAsync(request, prepared, token);
        }, cancellationToken);
    }

    public Task<BaseCommandResponse<Guid>> ExecuteHandoffAsync(AssignEventRoleCommand request,
        IPreparedHumanEventTeamAction prepared, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        _preparation.ConsumeHandoff(request, prepared);
        return ExecuteBodyAsync(request, prepared, token);
    }

    private async Task<BaseCommandResponse<Guid>> ExecuteBodyAsync(AssignEventRoleCommand request,
        IPreparedHumanEventTeamAction prepared, CancellationToken cancellationToken = default)
    {
        if (request.RoleId == (int)RoleEnum.EventOwner)
        {
            _metrics.RecordEventRoleAssignmentChanged("assign", "denied", RoleCodeFor(request.RoleId));
            _logger.LogWarning(
                "Event owner assignment denied: {DenyReason}",
                "event_owner_transfer_required");
            await WriteAuditAsync(
                _auditLogRepository,
                request.TenantId,
                request.EventId,
                "EventRoleAssignmentDenied",
                request.ActorUserId,
                oldValues: null,
                newValues: new
                {
                    request.EventId,
                    request.TargetUserId,
                    request.RoleId,
                    request.ActorUserId,
                    Operation = "assign",
                    DecisionEngine = "application_owner_invariant",
                    ResourceKind = ResourceKinds.Event,
                    Action = AuthorizationActions.Events.TransferOwnership,
                    DenyReason = "event_owner_transfer_required"
                });
            return Failure("Event ownership must be transferred with the ownership transfer command.", "event_owner_transfer_required");
        }

        var @event = await GetEventInTenantAsync(_eventRepository, request.TenantId, request.EventId);
        if (@event is null)
        {
            return Failure("Event not found for the requested tenant.", "event_not_found");
        }

        if (!await UserExistsAsync(_userRepository, request.TargetUserId))
        {
            return Failure("Target user not found.", "target_user_not_found");
        }

        var authority = prepared.CanAssignRole(request.RoleId);

        if (!authority.IsAllowed)
        {
            _metrics.RecordEventRoleAssignmentChanged("assign", "denied", RoleCodeFor(request.RoleId));
            _logger.LogWarning(
                "Event role assignment denied DenyReason={DenyReason}",
                authority.FailureCode ?? EventRoleAuthorityFailureCodes.AuthorityMissing);
            await WriteAuditAsync(
                _auditLogRepository,
                request.TenantId,
                request.EventId,
                "EventRoleAssignmentDenied",
                request.ActorUserId,
                oldValues: null,
                newValues: new
                {
                    request.EventId,
                    request.TargetUserId,
                    request.RoleId,
                    request.ActorUserId,
                    Operation = "assign",
                    DecisionEngine = "application_authority_ceiling",
                    ResourceKind = ResourceKinds.Event,
                    Action = AuthorizationActions.Events.ManageTeam,
                    DenyReason = authority.FailureCode ?? EventRoleAuthorityFailureCodes.AuthorityMissing
                });
            return AuthorityFailure(authority);
        }

        var existingOpen = await _assignmentRepository.GetOpenByEventUserRoleAsync(
            request.TenantId,
            request.EventId,
            request.TargetUserId,
            request.RoleId,
            cancellationToken);

        if (existingOpen is not null)
        {
            return Failure("An active or pending assignment for this user, event, and role already exists.", "event_role_assignment_duplicate", existingOpen.Id);
        }

        var assignment = EventRoleAssignment.Create(
            request.TenantId,
            request.EventId,
            request.TargetUserId,
            request.RoleId,
            request.Status,
            request.StartsAtUtc,
            request.ExpiresAtUtc,
            request.ActorUserId);

        assignment = await _assignmentRepository.Create(assignment);
        _metrics.RecordEventRoleAssignmentChanged("assign", "allowed", RoleCodeFor(request.RoleId));
        _logger.LogInformation(
            "Event role assignment created Status={Status}",
            assignment.Status);
        await WriteAuditAsync(
            _auditLogRepository,
            assignment.TenantId,
            assignment.Id,
            "EventRoleAssignmentCreated",
            request.ActorUserId,
            oldValues: null,
            newValues: new
            {
                assignment.EventId,
                assignment.UserId,
                assignment.RoleId,
                assignment.Status,
                assignment.StartsAtUtc,
                assignment.ExpiresAtUtc,
                assignment.Version,
                Operation = "assign"
            },
            affectedColumns: new[] { nameof(EventRoleAssignment.Status), nameof(EventRoleAssignment.StartsAtUtc), nameof(EventRoleAssignment.ExpiresAtUtc) });

        prepared.Complete(assignment.Id);
        return Success(assignment.Id, "Event role assigned successfully.");
    }
}
