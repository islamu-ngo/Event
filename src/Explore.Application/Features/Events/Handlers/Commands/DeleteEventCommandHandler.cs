using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Caching;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Events.Requests.Commands;
using Explore.Application.Features.Federation.Atproto.Services;
using Explore.Domain.Enums;
using Explore.Domain.Federation;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.Events.Handlers.Commands;

/// <summary>
/// Handles event deletion with role-based authorization and cascading soft delete.
///
/// Authorization hierarchy:
/// 1. System Admin (PlatformUserRole = "Admin") - Can delete any event
/// 2. Organization Creator/CoOwner/Admin (RoleId 1,2,3) - Can delete organization events
/// 3. Event Owner (Actor.UserId == current user) - Can delete personal events
///
/// Cascading behavior:
/// When an event is deleted, all associated EventSessions are also soft deleted.
/// This ensures referential integrity and proper audit trail with DeletedAt/DeletedBy fields.
/// </summary>
public class DeleteEventCommandHandler : IRequestHandler<DeleteEventCommand, bool>
{
    private readonly IEventRepository _eventRepository;
    private readonly IEventSessionRepository _eventSessionRepository;
    private readonly IRegistrationInventoryRepository _registrationInventoryRepository;
    private readonly IActorRepository _actorRepository;
    private readonly IOrganizationMemberRepository _organizationMemberRepository;
    private readonly ITenantUserRoleGrantRepository _tenantUserRoleGrantRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<DeleteEventCommandHandler> _logger;
    private readonly HybridCache _cache;
    private readonly IUnitOfWork _unitOfWork;
    private readonly AtprotoEventPublicationPlanner _atprotoPublicationPlanner;

    public DeleteEventCommandHandler(
        IEventRepository eventRepository,
        IEventSessionRepository eventSessionRepository,
        IRegistrationInventoryRepository registrationInventoryRepository,
        IActorRepository actorRepository,
        IOrganizationMemberRepository organizationMemberRepository,
        ITenantUserRoleGrantRepository tenantUserRoleGrantRepository,
        IRoleRepository roleRepository,
        ICurrentUserService currentUserService,
        ILogger<DeleteEventCommandHandler> logger,
        HybridCache cache,
        IUnitOfWork unitOfWork,
        AtprotoEventPublicationPlanner atprotoPublicationPlanner)
    {
        _eventRepository = eventRepository;
        _eventSessionRepository = eventSessionRepository;
        _registrationInventoryRepository = registrationInventoryRepository;
        _actorRepository = actorRepository;
        _organizationMemberRepository = organizationMemberRepository;
        _tenantUserRoleGrantRepository = tenantUserRoleGrantRepository;
        _roleRepository = roleRepository;
        _currentUserService = currentUserService;
        _logger = logger;
        _cache = cache;
        _unitOfWork = unitOfWork;
        _atprotoPublicationPlanner = atprotoPublicationPlanner;
    }

    public async Task<bool> Handle(DeleteEventCommand request, CancellationToken cancellationToken)
    {
        // Get current user ID from authentication context
        var userId = _currentUserService.UserId;
        if (userId == null)
        {
            _logger.LogWarning("Delete event failed: User ID not found in authentication context");
            return false;
        }

        // Get the event
        var @event = await _eventRepository.GetById(request.Id);
        if (@event == null)
        {
            _logger.LogWarning("Delete event failed: Event {EventId} not found", request.Id);
            return false;
        }

        // Check authorization
        var isAuthorized = await IsUserAuthorizedToDelete(@event, userId.Value, cancellationToken);
        if (!isAuthorized)
        {
            _logger.LogWarning(
                "Delete event failed: User {UserId} not authorized to delete event {EventId}",
                userId.Value,
                request.Id);
            return false;
        }

        if (await _registrationInventoryRepository.HasPaidEvidenceAsync(
                @event.Id, @event.TenantId, cancellationToken))
        {
            _logger.LogWarning("Delete event rejected because paid evidence exists for event {EventId}", request.Id);
            return false;
        }

        var sessions = await _eventSessionRepository.GetSessionsByEvent(request.Id);
        _logger.LogInformation(
            "Cascading soft delete: Found {SessionCount} event session(s) for event {EventId}",
            sessions.Count,
            request.Id);

        var federationOutboxId = Guid.CreateVersion7();
        var federationCreatedAt = DateTime.UtcNow;
        await _unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            foreach (var session in sessions)
            {
                _logger.LogDebug(
                    "Soft deleting event session {SessionId} (Title: {SessionTitle}) for event {EventId}",
                    session.Id,
                    session.Title ?? "Untitled",
                    request.Id);
                await _eventSessionRepository.Delete(session);
            }

            await _eventRepository.Delete(@event);
            await _atprotoPublicationPlanner.PlanEventAsync(
                new AtprotoEventPublicationInput(
                    @event.TenantId,
                    userId.Value,
                    @event.Id,
                    @event.ConcurrencyStamp,
                    PdsSyncOperation.Delete,
                    federationOutboxId,
                    federationCreatedAt),
                token);
        }, cancellationToken);

        _logger.LogInformation(
            "Event {EventId} successfully deleted by user {UserId}",
            request.Id,
            userId.Value);

        try
        {
            await _cache.RemoveAsync($"event:detail:{request.Id}", cancellationToken);
            await _cache.RemoveByTagAsync(CacheTags.EventListByTenant(@event.TenantId), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache invalidation failed after deleting event {EventId}", request.Id);
        }

        return true;
    }

    /// <summary>
    /// Determines if the user is authorized to delete the event.
    /// Checks: 1) System Admin role, 2) Organization membership, 3) Personal ownership
    /// </summary>
    private async Task<bool> IsUserAuthorizedToDelete(Domain.Event @event, Guid userId, CancellationToken cancellationToken)
    {
        // 1. Check if user has system Admin role (full access)
        if (await IsSystemAdmin(userId, cancellationToken))
        {
            _logger.LogDebug("User authorized as system Admin");
            return true;
        }

        // 2. Get the actor associated with the event
        var actor = await _actorRepository.GetById(@event.ActorId);
        if (actor == null)
        {
            _logger.LogWarning("Actor {ActorId} not found for event {EventId}", @event.ActorId, @event.Id);
            return false;
        }

        // 3. Check if this is an organization event
        if (actor.OrganizationId.HasValue)
        {
            var isOrgAuthorized = await IsOrganizationMemberAuthorized(
                actor.OrganizationId.Value,
                userId,
                cancellationToken);

            if (isOrgAuthorized)
            {
                _logger.LogDebug(
                    "User {UserId} authorized as organization member for org {OrgId}",
                    userId,
                    actor.OrganizationId.Value);
                return true;
            }
        }

        // 4. Check if this is a personal event owned by the user
        if (actor.UserId.HasValue && actor.UserId.Value == userId)
        {
            _logger.LogDebug("User authorized as event owner");
            return true;
        }

        // No authorization path matched
        return false;
    }

    /// <summary>
    /// Checks if the user has the system-wide "Admin" role.
    /// Admin users can delete any event regardless of ownership.
    /// Performance: Uses targeted repository queries instead of loading all records into memory.
    /// </summary>
    private async Task<bool> IsSystemAdmin(Guid userId, CancellationToken cancellationToken)
    {
        try
        {
            // Use targeted query: Check if user has admin tenant assignment
            // This avoids loading ALL PlatformUserRoles and ALL tenant role grants into memory (O(1) vs O(n))
            var tenantUserRoleGrants = await _tenantUserRoleGrantRepository.GetByUserId(userId);
            return tenantUserRoleGrants.Any(grant => grant.Role?.MasterCode != null &&
                grant.Role.MasterCode.Equals("Admin", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking system admin status for user");
            return false;
        }
    }

    /// <summary>
    /// Checks if the user is a member of the organization with Creator, CoOwner, or Admin role.
    /// Only these roles (IDs 1, 2, 3) can delete organization events.
    /// Performance: Uses targeted repository query instead of loading all members into memory.
    /// </summary>
    private async Task<bool> IsOrganizationMemberAuthorized(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        try
        {
            // Use targeted query: Check if user is an authorized member of this specific organization
            // This avoids loading ALL organization members into memory (O(1) vs O(n))
            var member = await _organizationMemberRepository.GetByOrganizationAndUser(organizationId, userId);

            if (member == null || member.IsDeleted)
            {
                return false;
            }

            // Check if role is Admin
            return member.RoleId == (int)RoleEnum.OrgAdmin;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error checking organization membership for user {UserId} in org {OrgId}",
                userId,
                organizationId);
            return false;
        }
    }
}
