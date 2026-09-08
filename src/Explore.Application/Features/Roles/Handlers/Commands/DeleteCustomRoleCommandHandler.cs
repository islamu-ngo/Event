using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Roles.Requests.Commands;
using Explore.Application.Responses;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.Roles.Handlers.Commands;

public class DeleteCustomRoleCommandHandler : IRequestHandler<DeleteCustomRoleCommand, BaseCommandResponse<int>>
{
    private readonly IRoleRepository _roleRepository;
    private readonly ICapabilityCeilingService _capabilityCeiling;
    private readonly IPolicySyncService _policySyncService;
    private readonly IPermissionRegistryService _permissionRegistry;
    private readonly ILogger<DeleteCustomRoleCommandHandler> _logger;

    public DeleteCustomRoleCommandHandler(
        IRoleRepository roleRepository,
        ICapabilityCeilingService capabilityCeiling,
        IPolicySyncService policySyncService,
        IPermissionRegistryService permissionRegistry,
        ILogger<DeleteCustomRoleCommandHandler> logger)
    {
        _roleRepository = roleRepository;
        _capabilityCeiling = capabilityCeiling;
        _policySyncService = policySyncService;
        _permissionRegistry = permissionRegistry;
        _logger = logger;
    }

    public async Task<BaseCommandResponse<int>> Handle(DeleteCustomRoleCommand request, CancellationToken cancellationToken)
    {
        // Rule 4: System immutability check
        var modCheck = await _capabilityCeiling.CanModifyRoleAsync(request.RoleId);
        if (!modCheck.IsAllowed)
        {
            return BaseCommandResponse.Validation<int>([modCheck.Error!], modCheck.Error);
        }

        var role = await _roleRepository.GetByIdAsync(request.RoleId);
        if (role == null)
        {
            return BaseCommandResponse.Validation<int>(["Role not found."], "Role not found.");
        }

        // Check for active members
        var hasMembers = await _roleRepository.HasActiveMembersAsync(request.RoleId);
        if (hasMembers)
        {
            string message = $"Cannot delete role '{role.FullName}'. It still has active members assigned.";
            return BaseCommandResponse.Validation<int>([message], message);
        }

        // Remove all permissions first (RolePermission entries)
        await _roleRepository.RemoveAllPermissionsAsync(request.RoleId);

        await _roleRepository.Delete(role);

        // Trigger Cerbos policy sync to remove policies for this role
        try
        {
            await _policySyncService.SyncRolePoliciesAsync(request.RoleId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Policy sync failed after deleting role {RoleId}, will retry on next sync", request.RoleId);
        }

        // Invalidate permission cache
        _permissionRegistry.InvalidateCache();

        return BaseCommandResponse.Success(request.RoleId, $"Custom role '{role.FullName}' deleted.");
    }
}
