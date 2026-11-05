using Explore.Application.Contracts.Infrastructure;

namespace Explore.Application.Contracts.Persistence;

public sealed record EmailDispatchPointerCandidate(Guid OutboxId, EmailDispatchPointer Pointer);

public interface IEmailDispatchPointerPublicationStore
{
    Task<IReadOnlyList<EmailDispatchPointerCandidate>> GetPublishBatchAsync(
        int batchSize, DateTime now, DateTime retryAttemptsBefore, CancellationToken cancellationToken);

    Task MarkPublishSucceededAsync(
        EmailDispatchPointerCandidate candidate, DateTime publishedAt, CancellationToken cancellationToken);

    Task MarkPublishFailedAsync(
        EmailDispatchPointerCandidate candidate, string failureCategory, DateTime attemptedAt,
        CancellationToken cancellationToken);
}
