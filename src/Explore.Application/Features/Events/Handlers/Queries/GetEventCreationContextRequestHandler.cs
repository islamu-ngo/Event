using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Event;
using Explore.Application.Features.Events.Requests.Queries;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Application.Settings;

namespace Explore.Application.Features.Events.Handlers.Queries;

public class GetEventCreationContextRequestHandler : IQueryHandler<GetEventCreationContextRequest, EventCreationContextDto>
{
    private const string PersonalPublisherMode = "personal";
    private const string OrganizationPublisherMode = "organization";
    private const string GroupPublisherMode = "group";

    private readonly IUserContext _userContext;
    private readonly ITenantContext _tenantContext;
    private readonly IHierarchicalSettingsReader _settings;
    private readonly IHumanEventCreationPublisherReader _publishers;

    public GetEventCreationContextRequestHandler(
        IUserContext userContext,
        ITenantContext tenantContext,
        IHierarchicalSettingsReader settings,
        IHumanEventCreationPublisherReader publishers)
    {
        _userContext = userContext;
        _tenantContext = tenantContext;
        _settings = settings;
        _publishers = publishers;
    }

    public async Task<EventCreationContextDto> QueryAsync(GetEventCreationContextRequest request, CancellationToken cancellationToken)
    {
        var currentUserId = _userContext.GetRequiredUserId();
        var tenantId = _tenantContext.TenantId;
        var settingContext = new SettingContext(tenantId, RequireAuthoritativeReads: true);
        var personalAllowed = await _settings.ResolveAsync<bool>(
            GovernanceSettingKeys.Events.UserSubmissionEnabled, settingContext, cancellationToken);
        var organizationAllowed = await _settings.ResolveAsync<bool>(
            GovernanceSettingKeys.Events.OrganizationSubmissionEnabled, settingContext, cancellationToken);
        var groupAllowed = await _settings.ResolveAsync<bool>(
            GovernanceSettingKeys.Events.GroupSubmissionEnabled, settingContext, cancellationToken);
        var requiresApproval = await _settings.ResolveAsync<bool>(
            GovernanceSettingKeys.Events.RequireApproval, settingContext, cancellationToken);

        var publisherOptions = new List<EventCreationPublisherOptionDto>();

        if (personalAllowed)
        {
            publisherOptions.Add(new EventCreationPublisherOptionDto
            {
                PublisherMode = PersonalPublisherMode,
                DisplayName = "Personal profile",
                CanPublish = true
            });
        }

        if (organizationAllowed || groupAllowed)
        {
            var memberships = await _publishers.ReadAsync(tenantId, currentUserId, cancellationToken);
            var allowedRoles = memberships.Permissions
                .Where(mapping => mapping.Permission.IsActive && mapping.Permission.MasterCode == PermissionCodes.EventCreate)
                .Select(mapping => mapping.RoleId)
                .ToHashSet();
            if (organizationAllowed)
                publisherOptions.AddRange(memberships.Organizations
                    .Select(membership => CreateOrganizationOption(membership, allowedRoles.Contains(membership.RoleId)))
                    .OrderByDescending(option => option.CanPublish)
                    .ThenBy(option => option.DisplayName, StringComparer.OrdinalIgnoreCase));
            if (groupAllowed)
                publisherOptions.AddRange(memberships.Groups
                    .Select(membership => CreateGroupOption(membership, allowedRoles.Contains(membership.RoleId)))
                    .OrderByDescending(option => option.CanPublish)
                    .ThenBy(option => option.DisplayName, StringComparer.OrdinalIgnoreCase));
        }

        var defaultPublisherMode = publisherOptions.FirstOrDefault(option => option.CanPublish)?.PublisherMode;

        return new EventCreationContextDto
        {
            CanCreate = defaultPublisherMode is not null,
            AllowPersonalPublishing = personalAllowed,
            AllowOrganizationPublishing = organizationAllowed,
            AllowGroupPublishing = groupAllowed,
            RequiresApproval = requiresApproval,
            DefaultPublisherMode = defaultPublisherMode,
            UnavailableReason = defaultPublisherMode is null
                ? "No available publisher can create events for the current user."
                : null,
            PublisherOptions = publisherOptions
        };
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
