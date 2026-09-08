using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Permission;
using Explore.Application.Features.Permissions.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.Permissions.Handlers.Queries;

public class GetRolePermissionsRequestHandler : IRequestHandler<GetRolePermissionsRequest, List<RolePermissionDto>>
{
    private readonly IRoleRepository _roleRepository;

    public GetRolePermissionsRequestHandler(IRoleRepository roleRepository)
    {
        _roleRepository = roleRepository;
    }

    public async Task<List<RolePermissionDto>> Handle(GetRolePermissionsRequest request, CancellationToken cancellationToken)
    {
        var role = await _roleRepository.GetByIdAsync(request.RoleId);
        if (role == null)
            return [];

        var permissions = await _roleRepository.GetPermissionsForRoleAsync(request.RoleId);

        return permissions.Select(p => new RolePermissionDto
        {
            RoleId = request.RoleId,
            RoleName = role.FullName,
            PermissionId = p.Id,
            PermissionMasterCode = p.MasterCode,
            PermissionFullName = p.FullName,
            ResourceKind = p.ResourceKind,
            Action = p.Action
        }).ToList();
    }
}
