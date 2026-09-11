using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Role;
using Explore.Application.Features.Roles.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.Roles.Handlers.Queries;

public class GetRoleDetailsRequestHandler : IRequestHandler<GetRoleDetailsRequest, RoleDto?>
{
    private readonly IRoleRepository _roleRepository;

    public GetRoleDetailsRequestHandler(IRoleRepository roleRepository)
    {
        _roleRepository = roleRepository;
    }

    public async Task<RoleDto?> Handle(GetRoleDetailsRequest request, CancellationToken cancellationToken)
    {
        var role = await _roleRepository.GetByIdAsync(request.Id);

        if (role == null)
            return null;

        return RoleMapper.ToDetail(role);
    }
}
