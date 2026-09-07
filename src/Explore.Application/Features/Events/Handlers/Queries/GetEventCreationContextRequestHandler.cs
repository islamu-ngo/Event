using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Event;
using Explore.Application.Features.Events.Requests.Queries;
using Explore.Domain;
using Explore.Domain.Constants;
using MediatR;

namespace Explore.Application.Features.Events.Handlers.Queries;

public class GetEventCreationContextRequestHandler : IRequestHandler<GetEventCreationContextRequest, EventCreationContextDto>
{
    private const string PersonalPublisherMode = "personal";
    private const string OrganizationPublisherMode = "organization";
    private const string GroupPublisherMode = "group";

    private readonly IUserContext _userContext;
    private readonly ITenantContext _tenantContext;
    private readonly ITenantPolicySettingService _tenantPolicySettingService;
    private readonly IOrganizationMemberRepository _organizationMemberRepository;
    private readonly IGroupMemberRepository _groupMemberRepository;

    public GetEventCreationContextRequestHandler(
        IUserContext userContext,
        ITenantContext tenantContext,
        ITenantPolicySettingService tenantPolicySettingService,
        IOrganizationMemberRepository organizationMemberRepository,
        IGroupMemberRepository groupMemberRepository)
    {
        _userContext = userContext;
        _tenantContext = tenantContext;
        _tenantPolicySettingService = tenantPolicySettingService;
        _organizationMemberRepository = organizationMemberRepository;
        _groupMemberRepository = groupMemberRepository;
    }

    public async Task<EventCreationContextDto> Handle(GetEventCreationContextRequest request, CancellationToken cancellationToken)
    {
        var currentUserId = _userContext.GetRequiredUserId();
        var tenantPolicy = await _tenantPolicySettingService.ReadEffectiveTenantSettingsAsync(_tenantContext.TenantId);

        var publisherOptions = new List<EventCreationPublisherOptionDto>();

        if (tenantPolicy.AllowUserSubmittedEvents)
        {
            publisherOptions.Add(new EventCreationPublisherOptionDto
            {
                PublisherMode = PersonalPublisherMode,
                DisplayName = "Personal profile",
                CanPublish = true
            });
        }

        if (tenantPolicy.AllowOrganizationSubmittedEvents)
        {
            publisherOptions.AddRange(await GetOrganizationPublisherOptionsAsync(currentUserId));
        }

        if (tenantPolicy.AllowGroupSubmittedEvents)
        {
            publisherOptions.AddRange(await GetGroupPublisherOptionsAsync(currentUserId));
        }

        var defaultPublisherMode = publisherOptions.FirstOrDefault(option => option.CanPublish)?.PublisherMode;

        return new EventCreationContextDto
        {
            CanCreate = defaultPublisherMode is not null,
            AllowPersonalPublishing = tenantPolicy.AllowUserSubmittedEvents,
            AllowOrganizationPublishing = tenantPolicy.AllowOrganizationSubmittedEvents,
            AllowGroupPublishing = tenantPolicy.AllowGroupSubmittedEvents,
            RequiresApproval = tenantPolicy.RequireEventApproval,
            DefaultPublisherMode = defaultPublisherMode,
            UnavailableReason = defaultPublisherMode is null
                ? "No available publisher can create events for the current user."
                : null,
            PublisherOptions = publisherOptions
        };
    }

    private async Task<List<EventCreationPublisherOptionDto>> GetOrganizationPublisherOptionsAsync(Guid currentUserId)
    {
        var memberships = await _organizationMemberRepository.GetMembershipsByUser(currentUserId);
        if (memberships.Count == 0)
            return [];

        var allowedOrganizationIds = await _organizationMemberRepository.GetOrganizationIdsWhereUserHasPermission(
            currentUserId,
            PermissionCodes.EventCreate);
        var allowedOrganizations = allowedOrganizationIds.ToHashSet();

        return memberships
            .Select(membership => CreateOrganizationOption(membership, allowedOrganizations.Contains(membership.OrganizationTenant.OrganizationId)))
            .OrderByDescending(option => option.CanPublish)
            .ThenBy(option => option.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<List<EventCreationPublisherOptionDto>> GetGroupPublisherOptionsAsync(Guid currentUserId)
    {
        var memberships = await _groupMemberRepository.GetMembershipsByUser(currentUserId);
        if (memberships.Count == 0)
            return [];

        var allowedGroupIds = await _groupMemberRepository.GetGroupIdsWhereUserHasPermission(
            currentUserId,
            PermissionCodes.EventCreate);
        var allowedGroups = allowedGroupIds.ToHashSet();

        return memberships
            .Select(membership => CreateGroupOption(membership, allowedGroups.Contains(membership.GroupTenant.GroupId)))
            .OrderByDescending(option => option.CanPublish)
            .ThenBy(option => option.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static EventCreationPublisherOptionDto CreateOrganizationOption(
        OrganizationMember membership,
        bool canPublish)
    {
        return new EventCreationPublisherOptionDto
        {
            PublisherMode = OrganizationPublisherMode,
            PublisherId = membership.OrganizationTenant.OrganizationId,
            DisplayName = membership.OrganizationTenant.Organization.FullName,
            RoleId = membership.RoleId,
            CanPublish = canPublish,
            Reason = canPublish ? null : "Your organization role cannot create events."
        };
    }

    private static EventCreationPublisherOptionDto CreateGroupOption(GroupMember membership, bool canPublish)
    {
        return new EventCreationPublisherOptionDto
        {
            PublisherMode = GroupPublisherMode,
            PublisherId = membership.GroupTenant.GroupId,
            DisplayName = membership.GroupTenant.Group.FullName,
            RoleId = membership.RoleId,
            CanPublish = canPublish,
            Reason = canPublish ? null : "Your group role cannot create events."
        };
    }
}
