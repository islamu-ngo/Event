using AutoMapper;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Group.Validators;
using Explore.Application.Features.Groups.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Domain.Enums;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.Groups.Handlers.Commands;

public class CreateGroupCommandHandler : IRequestHandler<CreateGroupCommand, BaseCommandResponse<Guid>>
{
    private readonly IGroupRepository _groupRepository;
    private readonly IGroupTenantRepository _groupTenantRepository;
    private readonly IOrganizationTenantRepository _organizationTenantRepository;
    private readonly IGroupMemberRepository _groupMemberRepository;
    private readonly IActorRepository _actorRepository;
    private readonly IStorageObjectRepository _storageObjectRepository;
    private readonly IAdminCacheInvalidator _adminCacheInvalidator;
    private readonly IMapper _mapper;
    private readonly ITenantContext _tenantContext;
    private readonly HybridCache _cache;
    private readonly BusinessMetrics _metrics;

    public CreateGroupCommandHandler(
        IGroupRepository groupRepository,
        IGroupTenantRepository groupTenantRepository,
        IOrganizationTenantRepository organizationTenantRepository,
        IGroupMemberRepository groupMemberRepository,
        IActorRepository actorRepository,
        IStorageObjectRepository storageObjectRepository,
        IAdminCacheInvalidator adminCacheInvalidator,
        IMapper mapper,
        ITenantContext tenantContext,
        HybridCache cache,
        BusinessMetrics metrics)
    {
        _groupRepository = groupRepository;
        _groupTenantRepository = groupTenantRepository;
        _organizationTenantRepository = organizationTenantRepository;
        _groupMemberRepository = groupMemberRepository;
        _actorRepository = actorRepository;
        _storageObjectRepository = storageObjectRepository;
        _adminCacheInvalidator = adminCacheInvalidator;
        _mapper = mapper;
        _tenantContext = tenantContext;
        _cache = cache;
        _metrics = metrics;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(CreateGroupCommand request, CancellationToken cancellationToken)
    {
        var validator = new CreateGroupDtoValidator();
        var validationResult = await validator.ValidateAsync(request.GroupDto, cancellationToken);
        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(e => e.ErrorMessage),
                "Validation failed.");
        }

        if (!await ImageReferenceEligibility.AreEligibleAsync(
                _storageObjectRepository,
                _tenantContext.TenantId,
                request.GroupDto.ProfilePictureId))
        {
            return BaseCommandResponse.Validation<Guid>(
                ["Profile picture must be an active public safe-raster object in the current tenant."],
                "Validation failed.");
        }

        var result = await _groupRepository.ExecuteWithHierarchyMutationLock(
            _tenantContext.TenantId,
            async lockedCancellationToken =>
            {
                var hierarchyErrors = await ValidateHierarchy(request.GroupDto.ParentOrganizationId, request.GroupDto.ParentGroupId, lockedCancellationToken);
                if (hierarchyErrors.Count > 0)
                {
                    return BaseCommandResponse.Validation<Guid>(hierarchyErrors, "Validation failed.");
                }

                var currentUserId = request.CreatorUserId;

                var group = _mapper.Map<Group>(request.GroupDto);

                group.CreatedAt = DateTime.UtcNow;

                group = await _groupRepository.Create(group);

                var groupActor = new Actor
                {
                    ActorTypeId = (int)ActorTypeEnum.Group,
                    ActorType = null!,
                    Pii = new ActorPii { DisplayName = group.FullName },
                    Description = null,
                    GroupId = group.Id,
                    Group = group
                };

                await _actorRepository.Create(groupActor);

                var parentOrganization = request.GroupDto.ParentOrganizationId.HasValue
                    ? await _organizationTenantRepository.GetByOrganizationAndTenant(
                        request.GroupDto.ParentOrganizationId.Value,
                        _tenantContext.TenantId,
                        lockedCancellationToken)
                    : null;
                var parentGroup = request.GroupDto.ParentGroupId.HasValue
                    ? await _groupTenantRepository.GetByGroupAndTenant(
                        request.GroupDto.ParentGroupId.Value,
                        _tenantContext.TenantId,
                        lockedCancellationToken)
                    : null;

                var participation = new GroupTenant
                {
                    TenantId = _tenantContext.TenantId,
                    Tenant = null!,
                    GroupId = group.Id,
                    Group = group,
                    ApprovalStatusId = (int)ApprovalStatusEnum.Pending,
                    ApprovalStatus = null!,
                    ProfilePictureId = request.GroupDto.ProfilePictureId,
                    ParentOrganizationTenantId = parentOrganization?.Id,
                    ParentGroupTenantId = parentGroup?.Id,
                    CreatedAt = group.CreatedAt
                };

                participation = await _groupTenantRepository.Create(participation);

                var groupMember = new GroupMember
                {
                    GroupTenantId = participation.Id,
                    GroupTenant = participation,
                    UserId = currentUserId,
                    User = null!,
                    RoleId = (int)RoleEnum.GroupAdmin,
                    Role = null!,
                    TenantId = _tenantContext.TenantId,
                    Tenant = null!
                };

                await _groupMemberRepository.Create(groupMember);

                _metrics.RecordOrganizationCreated(_tenantContext.TenantId.ToString());

                await _cache.RemoveAsync($"group:detail:{group.Id}", lockedCancellationToken);

                return BaseCommandResponse.Success(
                    group.Id,
                    "Group created successfully. You are now the creator and admin of this group.");
            },
            cancellationToken);

        if (result.IsSuccess)
        {
            _adminCacheInvalidator.InvalidateUser(request.CreatorUserId);
        }

        return result;
    }

    private async Task<List<string>> ValidateHierarchy(Guid? parentOrganizationId, Guid? parentGroupId, CancellationToken cancellationToken)
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
            var exists = await _groupRepository.GroupExistsInTenant(parentGroupId.Value, tenantId, cancellationToken);
            if (!exists)
            {
                errors.Add("Parent group does not exist in the current tenant.");
            }

            if (await _groupRepository.WouldExceedHierarchyDepth(parentGroupId, tenantId, GroupHierarchyRules.MaxDepth, cancellationToken))
            {
                errors.Add("Parent group hierarchy exceeds the maximum supported depth.");
            }
        }

        return errors;
    }
}
