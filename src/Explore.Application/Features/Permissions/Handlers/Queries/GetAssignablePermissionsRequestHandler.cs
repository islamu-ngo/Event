using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Permission;
using Explore.Application.Features.Permissions.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.Permissions.Handlers.Queries;

public class GetAssignablePermissionsRequestHandler : IRequestHandler<GetAssignablePermissionsRequest, List<PermissionListDto>>
{
    private readonly IPermissionRepository _permissionRepository;
    private readonly IMapper _mapper;

    public GetAssignablePermissionsRequestHandler(
        IPermissionRepository permissionRepository,
        IMapper mapper)
    {
        _permissionRepository = permissionRepository;
        _mapper = mapper;
    }

    public async Task<List<PermissionListDto>> Handle(GetAssignablePermissionsRequest request, CancellationToken cancellationToken)
    {
        var permissions = await _permissionRepository.GetAssignablePermissionsAsync(
            request.CallerRoleIds,
            request.TargetScope);

        return _mapper.Map<List<PermissionListDto>>(permissions);
    }
}
