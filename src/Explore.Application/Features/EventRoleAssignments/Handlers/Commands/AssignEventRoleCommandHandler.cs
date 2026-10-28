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

public sealed class AssignEventRoleCommandHandler
    : EventRoleAssignmentCommandHandlerBase,
      ICommandHandler<AssignEventRoleCommand, BaseCommandResponse<Guid>>,
      IHumanEventTeamAssignmentOwner
{
    private readonly IEventRoleAssignmentRepository _assignmentRepository;
    private readonly IEventRepository _eventRepository;
    private readonly IUserRepository _userRepository;
    private readonly IHumanEventTeamActionPreparation _preparation;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly BusinessMetrics _metrics;
    private readonly ILogger<AssignEventRoleCommandHandler> _logger;
    private readonly IEventRoleAssignmentCreationMutationBoundary _boundary;

    public AssignEventRoleCommandHandler(
        IEventRoleAssignmentRepository assignmentRepository,
        IEventRepository eventRepository,
        IUserRepository userRepository,
        IHumanEventTeamActionPreparation preparation,
        IAuditLogRepository auditLogRepository,
        BusinessMetrics metrics,
        ILogger<AssignEventRoleCommandHandler> logger,
        IAdminContext identity,
        IMachinePrincipalAccessor machine,
        ITenantContext tenant,
        IAuthorizationProvider authorization,
        IEventRoleAssignmentCreationMutationBoundary boundary) : base(identity, machine, tenant, authorization)
    {
        _assignmentRepository = assignmentRepository;
        _eventRepository = eventRepository;
        _userRepository = userRepository;
        _preparation = preparation;
        _auditLogRepository = auditLogRepository;
        _metrics = metrics;
        _logger = logger;
        _boundary = boundary;
    }

    public Task<BaseCommandResponse<Guid>> ExecuteAsync(AssignEventRoleCommand request, CancellationToken cancellationToken = default) =>
        ExecuteQualifiedAsync(request, null, cancellationToken);

    internal async Task<BaseCommandResponse<Guid>> ExecuteQualifiedAsync(AssignEventRoleCommand request, string? expectedEmail,
        CancellationToken cancellationToken)
    {
        var outcomeActor = await RequireActorAsync(request.TenantId, request.EventId, AuthorizationActions.Events.ManageTeam, cancellationToken);
        var prepared = await _preparation.PrepareAsync(request, cancellationToken);
        Guid assignmentId = Guid.CreateVersion7();
        Guid auditId = Guid.CreateVersion7();
        string? denialReason = null;
        var result = await _boundary.ExecuteAsync(request.TenantId, request.EventId, assignmentId, request.TargetUserId,
            request.RoleId, request.Status, request.StartsAtUtc, request.ExpiresAtUtc, auditId, expectedEmail,
            async (actor, token) =>
            {
                await prepared.AcquireAsync(request, token);
                outcomeActor = actor;
                denialReason = null;
                return await AssignAsync(request, actor, assignmentId, auditId, prepared, reason => denialReason = reason, token);
            }, cancellationToken);
        if (result.IsSuccess)
        {
            _metrics.RecordEventRoleAssignmentChanged("assign", "allowed", RoleCodeFor(request.RoleId));
            _logger.LogInformation("Event role assignment committed TenantId={TenantId} EventId={EventId} AssignmentId={AssignmentId} RoleId={RoleId} ActorUserId={ActorUserId}",
                request.TenantId, request.EventId, result.Id, request.RoleId, outcomeActor);
        }
        else if (denialReason is not null && result.FailureCode == denialReason)
        {
            _metrics.RecordEventRoleAssignmentChanged("assign", "denied", RoleCodeFor(request.RoleId));
            _logger.LogWarning("Event role assignment denied TenantId={TenantId} EventId={EventId} RoleId={RoleId} ActorUserId={ActorUserId} DenyReason={DenyReason}",
                request.TenantId, request.EventId, request.RoleId, outcomeActor, denialReason);
        }
        return result;
    }

    private async Task<BaseCommandResponse<Guid>> AssignAsync(AssignEventRoleCommand request, Guid actorUserId, Guid assignmentId,
        Guid auditId, IPreparedHumanEventTeamAction prepared, Action<string> observeDenial, CancellationToken cancellationToken)
    {
        if (request.RoleId == (int)RoleEnum.EventOwner)
        {
            observeDenial("event_owner_transfer_required");
            await WriteAuditAsync(
                _auditLogRepository,
                request.TenantId,
                request.EventId,
                "EventRoleAssignmentDenied",
                actorUserId,
                oldValues: null,
                newValues: new
                {
                    request.EventId,
                    request.TargetUserId,
                    request.RoleId,
                    ActorUserId = actorUserId,
                    Operation = "assign",
                    DecisionEngine = "application_owner_invariant",
                    ResourceKind = ResourceKinds.Event,
                    Action = AuthorizationActions.Events.TransferOwnership,
                    DenyReason = "event_owner_transfer_required"
                }, auditId: auditId);
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
            observeDenial(authority.FailureCode ?? EventRoleAuthorityFailureCodes.AuthorityMissing);
            await WriteAuditAsync(
                _auditLogRepository,
                request.TenantId,
                request.EventId,
                "EventRoleAssignmentDenied",
                actorUserId,
                oldValues: null,
                newValues: new
                {
                    request.EventId,
                    request.TargetUserId,
                    request.RoleId,
                    ActorUserId = actorUserId,
                    Operation = "assign",
                    DecisionEngine = "application_authority_ceiling",
                    ResourceKind = ResourceKinds.Event,
                    Action = AuthorizationActions.Events.ManageTeam,
                    DenyReason = authority.FailureCode ?? EventRoleAuthorityFailureCodes.AuthorityMissing
                }, auditId: auditId);
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
            actorUserId);

        assignment.Id = assignmentId;
        assignment = await _assignmentRepository.Create(assignment);
        await WriteAuditAsync(
            _auditLogRepository,
            assignment.TenantId,
            assignment.Id,
            "EventRoleAssignmentCreated",
            actorUserId,
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
            affectedColumns: new[] { nameof(EventRoleAssignment.Status), nameof(EventRoleAssignment.StartsAtUtc), nameof(EventRoleAssignment.ExpiresAtUtc) }, auditId: auditId);

        prepared.Complete(assignment.Id);
        return Success(assignment.Id, "Event role assigned successfully.");
    }

    public async Task<BaseCommandResponse<Guid>> ExecuteHandoffAsync(AssignEventRoleCommand request,
        IPreparedHumanEventTeamAction prepared, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        _preparation.ConsumeHandoff(request, prepared);
        await RequireActorAsync(request.TenantId, request.EventId, AuthorizationActions.Events.ManageTeam, token);
        Guid assignmentId = Guid.CreateVersion7();
        Guid auditId = Guid.CreateVersion7();
        var result = await _boundary.ExecuteInCurrentTransactionAsync(request.TenantId, request.EventId, assignmentId,
            request.TargetUserId, request.RoleId, request.Status, request.StartsAtUtc, request.ExpiresAtUtc,
            auditId, null, (actor, inner) => AssignAsync(request, actor, assignmentId, auditId, prepared, _ => { }, inner), token);
        return result;
    }
}
