using Explore.Application.Responses;

namespace Explore.Application.Contracts.Services;

/// <summary>Owns cooperating moderation fences through the actual upload mutation commit, not provider I/O.</summary>
public interface IEventResourceUploadMutationBoundary
{
    Task<BaseCommandResponse<TPayload>> ExecuteAsync<TPayload>(
        EventResourceAuthorityRequest request, EventResourceModerationFacts original,
        Func<CancellationToken, Task<BaseCommandResponse<TPayload>>> operation,
        CancellationToken cancellationToken);
}
