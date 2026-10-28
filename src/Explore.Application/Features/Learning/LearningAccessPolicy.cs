using System.Security.Cryptography;
using System.Text;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Features.Learning;

/// <summary>
/// Projects learning eligibility from the current internal human's fenced facts.
/// Eligibility is independent of publishing, event creation and administrator roles.
/// </summary>
public static class LearningAccessPolicy
{
    public static bool IsActiveLearner(HumanPrincipalSourceFacts facts, Guid tenantId)
    {
        if (tenantId == Guid.Empty || facts.PrincipalId == Guid.Empty
            || facts.SelectedTenantId != tenantId || facts.Users.Length != 1
            || facts.Users[0].Id != facts.PrincipalId || facts.Users[0].IsDeleted
            || facts.Tenants.Count(value => value.Id == tenantId && value.IsActiveState) != 1)
            return false;

        var memberships = facts.TenantMemberships
            .Where(value => value.TenantId == tenantId && !value.IsDeleted).ToArray();
        return memberships.Length == 1
            && memberships[0].StatusId == (int)TenantUserStatusEnum.Active;
    }

    public static bool IsActorMember(HumanPrincipalSourceFacts facts, Actor actor)
    {
        if (facts.SelectedTenantId is not Guid tenantId
            || !IsActiveLearner(facts, tenantId) || actor.IsSuspended)
            return false;

        return (ActorTypeEnum)actor.ActorTypeId switch
        {
            ActorTypeEnum.User => actor.UserId == facts.PrincipalId,
            ActorTypeEnum.Organization when actor.OrganizationId is Guid organizationId =>
                facts.Organizations.Any(value => value.OwnerId == organizationId && Eligible(value, tenantId)),
            ActorTypeEnum.Group when actor.GroupId is Guid groupId =>
                facts.Groups.Any(value => value.OwnerId == groupId && Eligible(value, tenantId)),
            _ => false
        };
    }

    public static string ScopeStamp(Guid tenantId, Guid userId) =>
        Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes($"learning-scope:v1:{tenantId:N}:{userId:N}")));

    private static bool Eligible(HumanPrincipalOwnerMembershipFact membership, Guid tenantId) =>
        membership.TenantId == tenantId && !membership.IsDeleted
        && !membership.PlacementIsDeleted && !membership.PlacementIsSuspended && !membership.OwnerIsDeleted
        && membership.ApprovalStatusId == (int)ApprovalStatusEnum.Approved;
}
