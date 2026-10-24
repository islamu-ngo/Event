using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.OrganizationMember;
using Explore.Application.Features.OrganizationMembers.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.OrganizationMembers.Handlers.Commands;

public class AddOrganizationMemberCommandHandler : ICommandHandler<AddOrganizationMemberCommand, BaseCommandResponse<Guid>>
{
    private readonly IOrganizationMemberRepository _organizationMemberRepository;
    private readonly IOrganizationRepository _organizationRepository;
    private readonly IOrganizationTenantRepository _organizationTenantRepository;
    private readonly IUserRepository _userRepository;
    private readonly IUserContext _userContext;
    private readonly IOrganizationMemberMutationBoundary _mutation;
    private readonly IHumanAccountAdministrativeActionPreparation _preparation;

    public AddOrganizationMemberCommandHandler(
        IOrganizationMemberRepository organizationMemberRepository,
        IOrganizationRepository organizationRepository,
        IOrganizationTenantRepository organizationTenantRepository,
        IUserRepository userRepository,
        IUserContext userContext,
        IOrganizationMemberMutationBoundary mutation,
        IHumanAccountAdministrativeActionPreparation preparation)
    {
        _organizationMemberRepository = organizationMemberRepository;
        _organizationRepository = organizationRepository;
        _organizationTenantRepository = organizationTenantRepository;
        _userRepository = userRepository;
        _userContext = userContext;
        _mutation = mutation;
        _preparation = preparation;
    }

    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(AddOrganizationMemberCommand request, CancellationToken cancellationToken)
    {
        if (!_userContext.IsAuthenticated || _userContext.UserId is not Guid requesterGuid || requesterGuid == Guid.Empty)
        {
            return BaseCommandResponse.Validation<Guid>(["Invalid requester User ID."], "Invalid requester User ID.");
        }

        var prepared = await _preparation.PrepareAsync(request, cancellationToken);
        var memberId = Guid.CreateVersion7();
        var dto = request.AddOrganizationMemberDto;
        return await _mutation.ExecuteAddAsync(request.TenantId, dto.OrganizationId, memberId, dto.Email, (int)dto.Role,
            async (actor, target, token) =>
            {
                await prepared.AcquireAsync(request, token);
                var result = await AddAsync(request, actor, target, memberId, token);
                if (result.IsSuccess) prepared.Complete(result.Id);
                return result;
            }, cancellationToken);
    }

    private async Task<BaseCommandResponse<Guid>> AddAsync(AddOrganizationMemberCommand request, Guid requesterGuid,
        Guid targetUserId, Guid memberId, CancellationToken cancellationToken)
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

        var requesterMember = members.FirstOrDefault(m => m.UserId == requesterGuid);
        if (requesterMember == null || requesterMember.RoleId != (int)RoleEnum.OrgAdmin)
        {
            return BaseCommandResponse.Validation<Guid>(
                ["You do not have permission to invite members."],
                "You do not have permission to invite members.");
        }

        // 3. Find user by email
        var userToAdd = await _userRepository.GetUserByEmail(dto.Email);
        if (userToAdd == null || userToAdd.Id != targetUserId)
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
            Id = memberId,
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
