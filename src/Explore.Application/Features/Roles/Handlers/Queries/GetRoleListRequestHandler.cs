using Explore.Application.Contracts.Operations;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Role;
using Explore.Application.Features.Roles.Requests.Queries;
using Explore.Application.Lookups;

namespace Explore.Application.Features.Roles.Handlers.Queries;

public class GetRoleListRequestHandler : IQueryHandler<GetRoleListRequest, List<RoleListDto>>
{
    private readonly IRoleRepository _roleRepository;

    public GetRoleListRequestHandler(IRoleRepository roleRepository)
    {
        _roleRepository = roleRepository;
    }

    public async Task<List<RoleListDto>> QueryAsync(GetRoleListRequest request, CancellationToken cancellationToken)
    {
        var roles = request.RoleScopeId.HasValue && NormalizedLookupMetadata.IsRoleScopeId(request.RoleScopeId.Value)
            ? await _roleRepository.GetByScopeIdAsync(request.RoleScopeId.Value)
            : await _roleRepository.GetAllAsync();

        return roles.Select(RoleMapper.ToListItem).ToList();
    }
}
