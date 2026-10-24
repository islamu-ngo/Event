using Explore.Application.DTOs.StorageObject;
using Explore.Application.Responses;

namespace Explore.Application.Contracts.Services;

/// <summary>Owns cooperating moderation fences through the actual upload mutation commit, not provider I/O.</summary>
public interface IEventResourceUploadMutationBoundary
{
    Task<BaseCommandResponse<StorageUploadSessionDto>> ExecuteAsync(
        EventResourceAuthorityRequest request, EventResourceModerationFacts original,
        Func<CancellationToken, Task<BaseCommandResponse<StorageUploadSessionDto>>> operation,
        CancellationToken cancellationToken);
}
