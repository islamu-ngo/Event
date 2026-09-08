using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Role;
using Explore.Application.Features.Roles.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.Roles.Handlers.Queries;

public class GetRoleDetailsRequestHandler : IRequestHandler<GetRoleDetailsRequest, RoleDto?>
{
    private readonly IRoleRepository _roleRepository;
    private readonly IMapper _mapper;

    public GetRoleDetailsRequestHandler(IRoleRepository roleRepository, IMapper mapper)
    {
        _roleRepository = roleRepository;
        _mapper = mapper;
    }

    public async Task<RoleDto?> Handle(GetRoleDetailsRequest request, CancellationToken cancellationToken)
    {
        var role = await _roleRepository.GetByIdAsync(request.Id);

        if (role == null)
            return null;

        return _mapper.Map<RoleDto>(role);
    }
}
