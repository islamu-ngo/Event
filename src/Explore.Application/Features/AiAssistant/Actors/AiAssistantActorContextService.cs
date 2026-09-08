using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Ai;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;

namespace Explore.Application.Features.AiAssistant.Actors;

public sealed class AiAssistantActorContextService(
    IActorRepository actorRepository,
    ITenantUserRepository tenantUserRepository,
    IOrganizationMemberRepository organizationMemberRepository,
    IGroupMemberRepository groupMemberRepository)
    : IAiAssistantActorContextService
{
    public async Task<IReadOnlyList<AiAssistantActorContextDto>> ListAuthorizedActorContextsAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty || userId == Guid.Empty)
        {
            return [];
        }

        var contexts = new List<AiAssistantActorContextDto>();
        var seenActorIds = new HashSet<Guid>();

        var tenantUser = await tenantUserRepository.GetByTenantAndUserAsync(tenantId, userId, cancellationToken);
        var userActor = tenantUser?.ActorId is Guid tenantUserActorId
            ? await actorRepository.GetActorWithDetails(tenantUserActorId, cancellationToken)
            : await actorRepository.GetActorByUserIdAndTenantId(userId, tenantId, cancellationToken);

        AddActorContext(contexts, seenActorIds, userActor, nameof(ActorTypeEnum.User));

        var allowedOrganizationIds = (await organizationMemberRepository.GetOrganizationIdsWhereUserHasPermission(
            userId,
            PermissionCodes.EventCreate,
            cancellationToken)).ToHashSet();
        var organizationMemberships = await organizationMemberRepository.GetMembershipsByUser(userId, cancellationToken);
        foreach (var membership in organizationMemberships
                     .Where(membership => membership.TenantId == tenantId
                         && membership.OrganizationTenant.Organization.Actor is not null
                         && allowedOrganizationIds.Contains(membership.OrganizationTenant.OrganizationId))
                     .OrderBy(membership => membership.OrganizationTenant.Organization.FullName, StringComparer.OrdinalIgnoreCase))
        {
            AddActorContext(
                contexts,
                seenActorIds,
                membership.OrganizationTenant.Organization.Actor!.Id,
                nameof(ActorTypeEnum.Organization),
                membership.OrganizationTenant.Organization.FullName,
                membership.OrganizationTenant.OrganizationId);
        }

        var allowedGroupIds = (await groupMemberRepository.GetGroupIdsWhereUserHasPermission(
            userId,
            PermissionCodes.EventCreate,
            cancellationToken)).ToHashSet();
        var groupMemberships = await groupMemberRepository.GetMembershipsByUser(userId, cancellationToken);
        foreach (var membership in groupMemberships
                     .Where(membership => membership.TenantId == tenantId
                         && membership.GroupTenant.Group.Actor is not null
                         && allowedGroupIds.Contains(membership.GroupTenant.GroupId))
                     .OrderBy(membership => membership.GroupTenant.Group.FullName, StringComparer.OrdinalIgnoreCase))
        {
            AddActorContext(
                contexts,
                seenActorIds,
                membership.GroupTenant.Group.Actor!.Id,
                nameof(ActorTypeEnum.Group),
                membership.GroupTenant.Group.FullName,
                membership.GroupTenant.GroupId);
        }

        return contexts;
    }

    public async Task<AiAssistantActorContextResolution> ResolveAuthorizedActorAsync(
        Guid tenantId,
        Guid userId,
        Guid? requestedActorId,
        CancellationToken cancellationToken)
    {
        var contexts = await ListAuthorizedActorContextsAsync(tenantId, userId, cancellationToken);
        if (requestedActorId is null)
        {
            return AiAssistantActorContextResolution.Success(contexts.FirstOrDefault()?.ActorId, contexts);
        }

        if (requestedActorId == Guid.Empty)
        {
            return AiAssistantActorContextResolution.Failure(
                "invalid_actor_context",
                "AI acting actor id must be a non-empty actor identifier.",
                contexts);
        }

        return contexts.Any(context => context.ActorId == requestedActorId.Value)
            ? AiAssistantActorContextResolution.Success(requestedActorId, contexts)
            : AiAssistantActorContextResolution.Failure(
                "actor_context_not_authorized",
                "AI acting actor is not available to the authenticated user.",
                contexts);
    }

    private static void AddActorContext(
        ICollection<AiAssistantActorContextDto> contexts,
        ISet<Guid> seenActorIds,
        Actor? actor,
        string fallbackActorType)
    {
        if (actor is null)
        {
            return;
        }

        AddActorContext(contexts, seenActorIds, actor.Id, fallbackActorType, actor.DisplayName);
    }

    private static void AddActorContext(
        ICollection<AiAssistantActorContextDto> contexts,
        ISet<Guid> seenActorIds,
        Guid actorId,
        string actorType,
        string? actorDisplayName,
        Guid? scopeId = null)
    {
        if (actorId == Guid.Empty || !seenActorIds.Add(actorId))
        {
            return;
        }

        contexts.Add(new AiAssistantActorContextDto
        {
            ActorId = actorId,
            ScopeId = scopeId,
            ActorType = actorType,
            ActorDisplayName = string.IsNullOrWhiteSpace(actorDisplayName) ? actorType : actorDisplayName.Trim()
        });
    }
}
