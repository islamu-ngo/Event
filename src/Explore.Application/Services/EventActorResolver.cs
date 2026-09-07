using System;
using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Settings;
using Explore.Domain.Constants;
using Explore.Domain.Enums;

namespace Explore.Application.Services;

public class EventActorResolver : IEventActorResolver
{
    private readonly IActorRepository _actorRepository;
    private readonly IOrganizationMemberRepository _organizationMemberRepository;
    private readonly IGroupMemberRepository _groupMemberRepository;
    private readonly IHierarchicalSettingsResolver _settingsResolver;
    private readonly ITenantContext _tenantContext;
    private readonly ITenantUserRepository _tenantUserRepository;
    private readonly IOrganizationTenantRepository _organizationTenantRepository;
    private readonly IGroupTenantRepository _groupTenantRepository;

    public EventActorResolver(
        IActorRepository actorRepository,
        IOrganizationMemberRepository organizationMemberRepository,
        IGroupMemberRepository groupMemberRepository,
        IHierarchicalSettingsResolver settingsResolver,
        ITenantContext tenantContext,
        ITenantUserRepository tenantUserRepository,
        IOrganizationTenantRepository organizationTenantRepository,
        IGroupTenantRepository groupTenantRepository)
    {
        _actorRepository = actorRepository;
        _organizationMemberRepository = organizationMemberRepository;
        _groupMemberRepository = groupMemberRepository;
        _settingsResolver = settingsResolver;
        _tenantContext = tenantContext;
        _tenantUserRepository = tenantUserRepository;
        _organizationTenantRepository = organizationTenantRepository;
        _groupTenantRepository = groupTenantRepository;
    }

    public async Task<EventActorResult> ResolveAsync(
        Guid currentUserId,
        Guid? organizationId,
        Guid? groupId,
        CancellationToken cancellationToken)
    {
        var settingContext = new SettingContext(TenantId: _tenantContext.TenantId);

        if (organizationId.HasValue)
        {
            var organizationSubmissionEnabled = await _settingsResolver.ResolveAsync<bool>(
                GovernanceSettingKeys.Events.OrganizationSubmissionEnabled,
                settingContext,
                cancellationToken);

            if (!organizationSubmissionEnabled)
                return EventActorResult.Failure(
                    "Organization event publishing is disabled for this tenant.",
                    "Select a group or personal actor to publish this event.");

            return await ResolveOrganizationActorAsync(organizationId.Value, currentUserId, cancellationToken);
        }

        if (groupId.HasValue)
        {
            var groupSubmissionEnabled = await _settingsResolver.ResolveAsync<bool>(
                GovernanceSettingKeys.Events.GroupSubmissionEnabled,
                settingContext,
                cancellationToken);

            if (!groupSubmissionEnabled)
                return EventActorResult.Failure(
                    "Group event publishing is disabled for this tenant.",
                    "Select an organization or personal actor to publish this event.");

            return await ResolveGroupActorAsync(groupId.Value, currentUserId, cancellationToken);
        }

        var userSubmissionEnabled = await _settingsResolver.ResolveAsync<bool>(
            GovernanceSettingKeys.Events.UserSubmissionEnabled,
            settingContext,
            cancellationToken);
        var publishingPolicy = userSubmissionEnabled
            ? EventPublishingPolicyEnum.OrganizationGroupAndUserReported
            : EventPublishingPolicyEnum.OrganizationAndGroupOnly;

        return await ResolvePersonalActorAsync(currentUserId, publishingPolicy, cancellationToken);
    }

    private async Task<EventActorResult> ResolveOrganizationActorAsync(
        Guid organizationId, Guid currentUserId, CancellationToken cancellationToken)
    {
        var hasPermission = await _organizationMemberRepository.HasPermissionInOrganization(
            organizationId, currentUserId, PermissionCodes.EventCreate);

        if (!hasPermission)
            return EventActorResult.Failure(
                "You do not have permission to create events for this organization.",
                "Your role in the organization does not include event creation permission.");

        var actor = await _actorRepository.GetActorByOrganizationId(organizationId);
        if (actor == null)
            return EventActorResult.Failure(
                "Organization does not have an associated actor.",
                "The organization is not properly configured. Please contact support.");

        if (!await IsCreationEligibleAsync(actor, cancellationToken))
            return EventActorResult.Failure(
                "The selected actor is not eligible to create events in this tenant.",
                "The actor is suspended, deleted, or does not have eligible current-tenant participation.");

        return EventActorResult.Success(actor.Id, isCommunitySubmission: false);
    }

    private async Task<EventActorResult> ResolveGroupActorAsync(
        Guid groupId, Guid currentUserId, CancellationToken cancellationToken)
    {
        var hasPermission = await _groupMemberRepository.HasPermissionInGroup(
            groupId, currentUserId, PermissionCodes.EventCreate);

        if (!hasPermission)
            return EventActorResult.Failure(
                "You do not have permission to create events for this group.",
                "Your role in the group does not include event creation permission.");

        var actor = await _actorRepository.GetActorByGroupId(groupId);
        if (actor == null)
            return EventActorResult.Failure(
                "Group does not have an associated actor.",
                "The group is not properly configured. Please contact support.");

        if (!await IsCreationEligibleAsync(actor, cancellationToken))
            return EventActorResult.Failure(
                "The selected actor is not eligible to create events in this tenant.",
                "The actor is suspended, deleted, or does not have eligible current-tenant participation.");

        return EventActorResult.Success(actor.Id, isCommunitySubmission: false);
    }

    private async Task<EventActorResult> ResolvePersonalActorAsync(
        Guid currentUserId, EventPublishingPolicyEnum publishingPolicy, CancellationToken cancellationToken)
    {
        if (publishingPolicy == EventPublishingPolicyEnum.OrganizationAndGroupOnly)
            return EventActorResult.Failure(
                "Personal event publishing is disabled for this tenant.",
                "Select an organization or group to publish this event.");

        var actor = await _actorRepository.GetActorByUserId(currentUserId);
        if (actor == null)
            return EventActorResult.Failure(
                "Your personal actor was not found.",
                "Your account is not properly set up. Please sync your profile first.");

        if (!await IsCreationEligibleAsync(actor, cancellationToken))
            return EventActorResult.Failure(
                "The selected actor is not eligible to create events in this tenant.",
                "The actor is suspended, deleted, or does not have eligible current-tenant participation.");

        return EventActorResult.Success(actor.Id, isCommunitySubmission: true);
    }

    private Task<bool> IsCreationEligibleAsync(Explore.Domain.Actor actor, CancellationToken cancellationToken) =>
        EventActorCreationEligibilityEvaluator.IsEligibleAsync(
            actor,
            _tenantContext.TenantId,
            _tenantUserRepository,
            _organizationTenantRepository,
            _groupTenantRepository,
            cancellationToken);
}
