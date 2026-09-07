using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Roles.Requests.Commands;
using Explore.Application.Responses;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.Roles.Handlers.Commands;

public class UpdateRolePermissionsCommandHandler : IRequestHandler<UpdateRolePermissionsCommand, BaseCommandResponse<int>>
{
    private readonly IRoleRepository _roleRepository;
    private readonly ICapabilityCeilingService _capabilityCeiling;
    private readonly IPolicySyncService _policySyncService;
    private readonly IPermissionRegistryService _permissionRegistry;
    private readonly ILogger<UpdateRolePermissionsCommandHandler> _logger;

    public UpdateRolePermissionsCommandHandler(
        IRoleRepository roleRepository,
        ICapabilityCeilingService capabilityCeiling,
        IPolicySyncService policySyncService,
        IPermissionRegistryService permissionRegistry,
        ILogger<UpdateRolePermissionsCommandHandler> logger)
    {
        _roleRepository = roleRepository;
        _capabilityCeiling = capabilityCeiling;
        _policySyncService = policySyncService;
        _permissionRegistry = permissionRegistry;
        _logger = logger;
    }

    public async Task<BaseCommandResponse<int>> Handle(UpdateRolePermissionsCommand request, CancellationToken cancellationToken)
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

        // Capability ceiling validation
        var ceilingErrors = await _capabilityCeiling.ValidatePermissionAssignmentAsync(
            request.CallerRoleIds,
            request.CallerHighestScope,
            request.PermissionIds,
            role.Scope);

        if (ceilingErrors.Count > 0)
        {
            return BaseCommandResponse.Validation<int>(
                ceilingErrors,
                "Permission assignment validation failed.");
        }

        // Replace all permissions atomically
        await _roleRepository.ReplacePermissionsAsync(request.RoleId, request.PermissionIds);

        // Trigger Cerbos policy sync
        try
        {
            await _policySyncService.SyncRolePoliciesAsync(request.RoleId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Policy sync failed for role {RoleId}, will retry on next sync", request.RoleId);
        }

        // Invalidate permission cache
        _permissionRegistry.InvalidateCache();

        return BaseCommandResponse.Success(
            request.RoleId,
            $"Role '{role.FullName}' updated with {request.PermissionIds.Count} permissions.");
    }
}
