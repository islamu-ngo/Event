using Explore.Application.DTOs.Ai;

namespace Explore.Application.Features.AiAssistant.Actors;

public interface IAiAssistantActorContextService
{
    Task<IReadOnlyList<AiAssistantActorContextDto>> ListAuthorizedActorContextsAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken cancellationToken);

    Task<AiAssistantActorContextResolution> ResolveAuthorizedActorAsync(
        Guid tenantId,
        Guid userId,
        Guid? requestedActorId,
        CancellationToken cancellationToken);
}

public sealed record AiAssistantActorContextResolution(
    bool Succeeded,
    Guid? ActorId,
    IReadOnlyList<AiAssistantActorContextDto> AuthorizedContexts,
    string? FailureCode,
    string? FailureMessage)
{
    public static AiAssistantActorContextResolution Success(
        Guid? actorId,
        IReadOnlyList<AiAssistantActorContextDto> authorizedContexts)
        => new(true, actorId, authorizedContexts, null, null);

    public static AiAssistantActorContextResolution Failure(
        string failureCode,
        string failureMessage,
        IReadOnlyList<AiAssistantActorContextDto> authorizedContexts)
        => new(false, null, authorizedContexts, failureCode, failureMessage);
}
