using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Permission;
using Explore.Application.Features.Permissions.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.Permissions.Handlers.Queries;

public class GetAssignablePermissionsRequestHandler : IRequestHandler<GetAssignablePermissionsRequest, List<PermissionListDto>>
{
    private readonly IPermissionRepository _permissionRepository;

    public GetAssignablePermissionsRequestHandler(
        IPermissionRepository permissionRepository)
    {
        _permissionRepository = permissionRepository;
    }

    public async Task<List<PermissionListDto>> Handle(GetAssignablePermissionsRequest request, CancellationToken cancellationToken)
    {
        var permissions = await _permissionRepository.GetAssignablePermissionsAsync(
            request.CallerRoleIds,
            request.TargetScope);

        return permissions.Select(PermissionMapper.ToListItem).ToList();
    }
}
