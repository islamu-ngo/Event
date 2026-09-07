using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Services;

internal static class EventActorCreationEligibilityEvaluator
{
    internal static async Task<bool> IsEligibleAsync(
        Actor actor,
        Guid tenantId,
        ITenantUserRepository tenantUserRepository,
        IOrganizationTenantRepository organizationTenantRepository,
        IGroupTenantRepository groupTenantRepository,
        CancellationToken cancellationToken)
    {
        if (actor.IsDeleted || actor.IsSuspended)
        {
            return false;
        }

        if (actor.ActorTypeId == (int)ActorTypeEnum.User
            && actor.UserId is { } userId
            && actor.OrganizationId is null
            && actor.GroupId is null)
        {
            return await tenantUserRepository.IsActiveTenantUserAsync(tenantId, userId, cancellationToken);
        }

        if (actor.ActorTypeId == (int)ActorTypeEnum.Organization
            && actor.UserId is null
            && actor.OrganizationId is { } organizationId
            && actor.GroupId is null)
        {
            var participation = await organizationTenantRepository.GetByOrganizationAndTenant(
                organizationId,
                tenantId,
                cancellationToken);
            return participation is
            {
                ApprovalStatusId: (int)ApprovalStatusEnum.Approved,
                IsOrganizerEligible: true,
                IsSuspended: false,
                IsDeleted: false
            };
        }

        if (actor.ActorTypeId != (int)ActorTypeEnum.Group
            || actor.UserId is not null
            || actor.OrganizationId is not null
            || actor.GroupId is not { } groupId)
        {
            return false;
        }

        var groupParticipation = await groupTenantRepository.GetByGroupAndTenant(
            groupId,
            tenantId,
            cancellationToken);
        return groupParticipation is
        {
            ApprovalStatusId: (int)ApprovalStatusEnum.Approved,
            IsOrganizerEligible: true,
            IsSuspended: false,
            IsDeleted: false
        };
    }
}
