using Explore.Application.Contracts.Persistence;
using Explore.Application.Services;
using Explore.Domain.Constants;

namespace Explore.Application.Features.EventOrganizerClaims;

internal static class ClaimantActorAccessEvaluator
{
    internal static async Task<bool> CanControlAsync(
        Guid actorId,
        Guid userId,
        Guid tenantId,
        IActorRepository actorRepository,
        ITenantUserRepository tenantUserRepository,
        IOrganizationTenantRepository organizationTenantRepository,
        IGroupTenantRepository groupTenantRepository,
        IOrganizationMemberRepository organizationMemberRepository,
        IGroupMemberRepository groupMemberRepository,
        CancellationToken cancellationToken)
    {
        var actor = await actorRepository.GetActorWithDetails(actorId, cancellationToken);
        if (actor is null || !await EventActorCreationEligibilityEvaluator.IsEligibleAsync(
                actor,
                tenantId,
                tenantUserRepository,
                organizationTenantRepository,
                groupTenantRepository,
                cancellationToken))
        {
            return false;
        }

        return await CanControlOwnershipAsync(
            actor,
            userId,
            organizationMemberRepository,
            groupMemberRepository);
    }

    internal static async Task<bool> CanControlOwnershipAsync(
        Guid actorId,
        Guid userId,
        IActorRepository actorRepository,
        IOrganizationMemberRepository organizationMemberRepository,
        IGroupMemberRepository groupMemberRepository,
        CancellationToken cancellationToken)
    {
        var actor = await actorRepository.GetActorWithDetails(actorId, cancellationToken);
        return actor is not null && await CanControlOwnershipAsync(
            actor,
            userId,
            organizationMemberRepository,
            groupMemberRepository);
    }

    private static async Task<bool> CanControlOwnershipAsync(
        Explore.Domain.Actor actor,
        Guid userId,
        IOrganizationMemberRepository organizationMemberRepository,
        IGroupMemberRepository groupMemberRepository)
    {
        if (actor.UserId == userId)
        {
            return true;
        }

        if (actor.OrganizationId is { } organizationId)
        {
            return await organizationMemberRepository.HasPermissionInOrganization(
                organizationId,
                userId,
                PermissionCodes.EventCreate);
        }

        return actor.GroupId is { } groupId
            && await groupMemberRepository.HasPermissionInGroup(groupId, userId, PermissionCodes.EventCreate);
    }

    internal static async Task<bool> IsEligibleAsync(
        Guid actorId,
        Guid tenantId,
        IActorRepository actorRepository,
        ITenantUserRepository tenantUserRepository,
        IOrganizationTenantRepository organizationTenantRepository,
        IGroupTenantRepository groupTenantRepository,
        CancellationToken cancellationToken)
    {
        var actor = await actorRepository.GetActorWithDetails(actorId, cancellationToken);
        return actor is not null && await EventActorCreationEligibilityEvaluator.IsEligibleAsync(
            actor,
            tenantId,
            tenantUserRepository,
            organizationTenantRepository,
            groupTenantRepository,
            cancellationToken);
    }

}
