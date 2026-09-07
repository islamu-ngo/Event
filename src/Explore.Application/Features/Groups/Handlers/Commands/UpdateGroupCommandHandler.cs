using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Group;
using Explore.Application.DTOs.Group.Validators;
using Explore.Application.Exceptions;
using Explore.Application.Features.Groups;
using Explore.Application.Features.Groups.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.Groups.Handlers.Commands;

public class UpdateGroupCommandHandler : IRequestHandler<UpdateGroupCommand, BaseCommandResponse<Guid>>
{
    private readonly IGroupRepository _groupRepository;
    private readonly IGroupTenantRepository _groupTenantRepository;
    private readonly IOrganizationTenantRepository _organizationTenantRepository;
    private readonly IGroupMemberRepository _groupMemberRepository;
    private readonly IUserContext _userContext;
    private readonly ITenantContext _tenantContext;
    private readonly HybridCache _cache;

    public UpdateGroupCommandHandler(
        IGroupRepository groupRepository,
        IGroupTenantRepository groupTenantRepository,
        IOrganizationTenantRepository organizationTenantRepository,
        IGroupMemberRepository groupMemberRepository,
        IUserContext userContext,
        ITenantContext tenantContext,
        HybridCache cache)
    {
        _groupRepository = groupRepository;
        _groupTenantRepository = groupTenantRepository;
        _organizationTenantRepository = organizationTenantRepository;
        _groupMemberRepository = groupMemberRepository;
        _userContext = userContext;
        _tenantContext = tenantContext;
        _cache = cache;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(UpdateGroupCommand request, CancellationToken cancellationToken)
    {
        var validator = new UpdateGroupDtoValidator();
        var validationResult = await validator.ValidateAsync(request.UpdateGroupDto, cancellationToken);
        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(e => e.ErrorMessage),
                "Group update failed.");
        }

        var currentUserId = _userContext.GetRequiredUserId();
        var hasPermission = await _groupMemberRepository.HasPermissionInGroup(
            request.GroupId, currentUserId, PermissionCodes.GroupManage);
        if (!hasPermission)
        {
            throw new AuthorizationException(ResourceKinds.Group, AuthorizationActions.Update);
        }

        var group = await _groupRepository.GetById(request.GroupId);
        if (group == null)
        {
            return BaseCommandResponse.NotFound<Guid>("Group not found.");
        }

        if (group.ConcurrencyStamp != request.ExpectedConcurrencyStamp)
        {
            throw new ConcurrencyConflictException(
                ConcurrencyConflictException.ConcurrentUpdate,
                "The group was modified by another request. Reload and retry.",
                nameof(Group),
                group.Id.ToString());
        }

        return await _groupRepository.ExecuteWithHierarchyMutationLock(
            _tenantContext.TenantId,
            async lockedCancellationToken =>
            {
                var participation = await _groupTenantRepository.GetByGroupAndTenant(
                    group.Id,
                    _tenantContext.TenantId,
                    lockedCancellationToken);
                if (participation is null)
                {
                    return BaseCommandResponse.NotFound<Guid>("Group not found.");
                }

                var targetParent = await ResolveTargetParent(participation, request.UpdateGroupDto);
                if (targetParent.ParentOrganizationId.HasValue && targetParent.ParentGroupId.HasValue)
                {
                    return BaseCommandResponse.Validation<Guid>(
                        ["A group can have either a parent organization or a parent group, not both."],
                        "Validation failed.");
                }

                var hierarchyErrors = await ValidateHierarchy(
                    request.GroupId,
                    targetParent.ParentOrganizationId,
                    targetParent.ParentGroupId,
                    lockedCancellationToken);
                if (hierarchyErrors.Count > 0)
                {
                    return BaseCommandResponse.Validation<Guid>(hierarchyErrors, "Validation failed.");
                }

                ApplyFullName(group, request.UpdateGroupDto.FullName);
                ApplyDescription(group, request.UpdateGroupDto.Description);
                await ApplyParent(participation, targetParent, lockedCancellationToken);
                group.UpdatedAt = DateTime.UtcNow;
                group.UpdatedBy = currentUserId;
                participation.UpdatedAt = group.UpdatedAt;
                participation.UpdatedBy = currentUserId;

                await _groupRepository.Update(group);
                await _groupTenantRepository.Update(participation);

                await _cache.RemoveAsync($"group:detail:{group.Id}", lockedCancellationToken);

                return BaseCommandResponse.Success(group.Id, "Group updated successfully.");
            },
            cancellationToken);
    }

    private async Task<GroupParentTarget> ResolveTargetParent(GroupTenant participation, UpdateGroupDto dto)
    {
        var parentOrganizationId = participation.ParentOrganizationTenantId.HasValue
            ? (await _organizationTenantRepository.GetById(participation.ParentOrganizationTenantId.Value))?.OrganizationId
            : null;
        var parentGroupId = participation.ParentGroupTenantId.HasValue
            ? (await _groupTenantRepository.GetById(participation.ParentGroupTenantId.Value))?.GroupId
            : null;

        if (dto.ParentOrganization?.Value.HasValue == true)
        {
            parentOrganizationId = dto.ParentOrganization.Value.Value;
            if (parentOrganizationId.HasValue)
            {
                parentGroupId = null;
            }
        }

        if (dto.ParentGroup?.Value.HasValue == true)
        {
            parentGroupId = dto.ParentGroup.Value.Value;
            if (parentGroupId.HasValue)
            {
                parentOrganizationId = null;
            }
        }

        return new GroupParentTarget(parentOrganizationId, parentGroupId);
    }

    private static void ApplyFullName(Group group, UpdateGroupFullNameDto? update)
    {
        if (update is not null)
        {
            group.FullName = update.Value;
        }
    }

    private static void ApplyDescription(Group group, UpdateGroupDescriptionDto? update)
    {
        if (update?.Value.HasValue == true)
        {
            group.Description = update.Value.Value;
        }
    }

    private async Task ApplyParent(
        GroupTenant participation,
        GroupParentTarget target,
        CancellationToken cancellationToken)
    {
        participation.ParentOrganizationTenantId = target.ParentOrganizationId.HasValue
            ? (await _organizationTenantRepository.GetByOrganizationAndTenant(
                target.ParentOrganizationId.Value,
                _tenantContext.TenantId,
                cancellationToken))?.Id
            : null;
        participation.ParentGroupTenantId = target.ParentGroupId.HasValue
            ? (await _groupTenantRepository.GetByGroupAndTenant(
                target.ParentGroupId.Value,
                _tenantContext.TenantId,
                cancellationToken))?.Id
            : null;
    }

    private async Task<List<string>> ValidateHierarchy(Guid groupId, Guid? parentOrganizationId, Guid? parentGroupId, CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        var tenantId = _tenantContext.TenantId;

        if (parentOrganizationId.HasValue)
        {
            var exists = await _groupRepository.OrganizationExistsInTenant(parentOrganizationId.Value, tenantId, cancellationToken);
            if (!exists)
            {
                errors.Add("Parent organization does not exist in the current tenant.");
            }
        }

        if (parentGroupId.HasValue)
        {
            if (parentGroupId.Value == groupId)
            {
                errors.Add("A group cannot be its own parent.");
                return errors;
            }

            var exists = await _groupRepository.GroupExistsInTenant(parentGroupId.Value, tenantId, cancellationToken);
            if (!exists)
            {
                errors.Add("Parent group does not exist in the current tenant.");
            }
            else
            {
                if (await _groupRepository.WouldCreateHierarchyCycle(groupId, parentGroupId.Value, tenantId, cancellationToken))
                {
                    errors.Add("Parent group would create a hierarchy cycle.");
                }

                if (await _groupRepository.WouldExceedHierarchyDepthForMove(groupId, parentGroupId, tenantId, GroupHierarchyRules.MaxDepth, cancellationToken))
                {
                    errors.Add("Parent group hierarchy exceeds the maximum supported depth.");
                }
            }
        }

        return errors;
    }
}
