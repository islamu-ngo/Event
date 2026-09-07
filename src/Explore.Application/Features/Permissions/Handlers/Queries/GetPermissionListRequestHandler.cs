using AutoMapper;
using Explore.Application.Authorization;
using Explore.Application.DTOs.Permission;
using Explore.Application.Features.Permissions.Requests.Queries;
using Explore.Application.Lookups;
using MediatR;

namespace Explore.Application.Features.Permissions.Handlers.Queries;

public class GetPermissionListRequestHandler : IRequestHandler<GetPermissionListRequest, List<PermissionListDto>>
{
    private readonly IPermissionRegistryService _permissionRegistry;
    private readonly IMapper _mapper;

    public GetPermissionListRequestHandler(
        IPermissionRegistryService permissionRegistry,
        IMapper mapper)
    {
        _permissionRegistry = permissionRegistry;
        _mapper = mapper;
    }

    public async Task<List<PermissionListDto>> Handle(GetPermissionListRequest request, CancellationToken cancellationToken)
    {
        var roleScopeId = request.RoleScopeId.HasValue && NormalizedLookupMetadata.IsRoleScopeId(request.RoleScopeId.Value)
            ? request.RoleScopeId
            : null;

        var permissions = await _permissionRegistry.GetFilteredPermissionsByScopeIdAsync(
            roleScopeId,
            request.ExcludeFiltered);

        if (!string.IsNullOrEmpty(request.GroupName))
        {
            permissions = permissions
                .Where(p => p.GroupName == request.GroupName)
                .ToList()
                .AsReadOnly();
        }

        return _mapper.Map<List<PermissionListDto>>(permissions);
    }
}
