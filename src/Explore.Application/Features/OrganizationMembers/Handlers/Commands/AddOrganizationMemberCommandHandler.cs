using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.OrganizationMember;
using Explore.Application.Features.OrganizationMembers.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using MediatR;

namespace Explore.Application.Features.OrganizationMembers.Handlers.Commands;

public class AddOrganizationMemberCommandHandler : IRequestHandler<AddOrganizationMemberCommand, BaseCommandResponse<Guid>>
{
    private readonly IOrganizationMemberRepository _organizationMemberRepository;
    private readonly IOrganizationRepository _organizationRepository;
    private readonly IOrganizationTenantRepository _organizationTenantRepository;
    private readonly IUserRepository _userRepository;
    private readonly IMapper _mapper;

    public AddOrganizationMemberCommandHandler(
        IOrganizationMemberRepository organizationMemberRepository,
        IOrganizationRepository organizationRepository,
        IOrganizationTenantRepository organizationTenantRepository,
        IUserRepository userRepository,
        IMapper mapper)
    {
        _organizationMemberRepository = organizationMemberRepository;
        _organizationRepository = organizationRepository;
        _organizationTenantRepository = organizationTenantRepository;
        _userRepository = userRepository;
        _mapper = mapper;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(AddOrganizationMemberCommand request, CancellationToken cancellationToken)
    {
        var dto = request.AddOrganizationMemberDto;

        // 1. Check if organization exists
        var organization = await _organizationRepository.GetById(dto.OrganizationId);
        if (organization == null)
        {
            return BaseCommandResponse.Validation<Guid>(["Organization not found"], "Organization not found");
        }

        var participation = request.TenantId == Guid.Empty
            ? null
            : await _organizationTenantRepository.GetByOrganizationAndTenant(
                dto.OrganizationId,
                request.TenantId,
                cancellationToken);
        if (participation is null)
        {
            return BaseCommandResponse.Validation<Guid>(
                ["Organization does not belong to the current tenant."],
                "Organization does not belong to the current tenant.");
        }

        // 2. Check permissions (Requester must be an Admin member)
        var members = await _organizationMemberRepository.GetMembersByOrganizationId(dto.OrganizationId);

        if (Guid.TryParse(request.RequesterUserId, out Guid requesterGuid))
        {
            var requesterMember = members.FirstOrDefault(m => m.UserId == requesterGuid);
            // Only OrgAdmin role can invite members
            if (requesterMember == null || requesterMember.RoleId != (int)RoleEnum.OrgAdmin)
            {
                return BaseCommandResponse.Validation<Guid>(
                    ["You do not have permission to invite members."],
                    "You do not have permission to invite members.");
            }
        }
        else
        {
            return BaseCommandResponse.Validation<Guid>(
                ["Invalid requester User ID."],
                "Invalid requester User ID.");
        }

        // 3. Find user by email
        var userToAdd = await _userRepository.GetUserByEmail(dto.Email);
        if (userToAdd == null)
        {
            return BaseCommandResponse.Validation<Guid>(
                ["User with this email not found."],
                "User with this email not found.");
        }

        // 4. Check if user is already a member
        if (members.Any(m => m.UserId == userToAdd.Id))
        {
            return BaseCommandResponse.Validation<Guid>(
                ["User is already a member of this organization."],
                "User is already a member of this organization.");
        }

        // 5. Create Member
        var organizationMember = new OrganizationMember
        {
            OrganizationTenantId = participation.Id,
            OrganizationTenant = participation,
            UserId = userToAdd.Id,
            User = null!,
            RoleId = (int)dto.Role,
            Role = null!,
            TenantId = participation.TenantId,
            Tenant = null!
        };

        organizationMember = await _organizationMemberRepository.Create(organizationMember);

        return BaseCommandResponse.Success(organizationMember.Id, "Member added successfully");
    }
}
