using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Features.EventReporting.Models;

namespace Explore.Infrastructure.Services.Moderation;

public sealed class NoopReviewQueueProvider : IReviewQueueProvider
{
    public Task<ReviewCaseSyncResult> MirrorCaseAsync(
        ReviewCaseEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ReviewCaseSyncResult.Disabled("External review queue provider is disabled."));
    }
}
