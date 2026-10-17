using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Ai;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Features.AiAssistant.Actors;

public sealed class AiAssistantActorContextService(
    IHumanActorContextReader reader)
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

        var actors = await reader.ReadAsync(tenantId, userId, cancellationToken);
        var userActor = actors.FirstOrDefault(actor => actor.ActorTypeId == (int)ActorTypeEnum.User);

        AddActorContext(contexts, seenActorIds, userActor, nameof(ActorTypeEnum.User));

        foreach (var actor in actors.Where(actor => actor.ActorTypeId == (int)ActorTypeEnum.Organization)
                     .OrderBy(actor => actor.Organization?.FullName, StringComparer.OrdinalIgnoreCase))
        {
            AddActorContext(
                contexts,
                seenActorIds,
                actor.Id,
                nameof(ActorTypeEnum.Organization),
                actor.Organization?.FullName,
                actor.OrganizationId);
        }

        foreach (var actor in actors.Where(actor => actor.ActorTypeId == (int)ActorTypeEnum.Group)
                     .OrderBy(actor => actor.Group?.FullName, StringComparer.OrdinalIgnoreCase))
        {
            AddActorContext(
                contexts,
                seenActorIds,
                actor.Id,
                nameof(ActorTypeEnum.Group),
                actor.Group?.FullName,
                actor.GroupId);
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
