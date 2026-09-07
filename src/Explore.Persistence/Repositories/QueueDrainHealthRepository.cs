using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Federation;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class QueueDrainHealthRepository(ExploreDbContext dbContext) : IQueueDrainHealthRepository
{
    public async Task<QueueDrainHealthSnapshot> GetSnapshotAsync(
        DateTime observedAt,
        DateTime integrationStaleBefore,
        CancellationToken cancellationToken)
    {
        IQueryable<IntegrationSyncOutbox> integration = dbContext.IntegrationSyncOutbox
            .IgnoreTenantFilter(TenantFilterBypassReasons.IntegrationSyncWorkerCrossTenantQueue);
        int integrationDue = await integration.CountAsync(outbox =>
            (outbox.Status == IntegrationSyncStatus.Pending || outbox.Status == IntegrationSyncStatus.RetryScheduled) &&
            (outbox.NextAttemptAt == null || outbox.NextAttemptAt <= observedAt), cancellationToken);
        int integrationStale = await integration.CountAsync(outbox =>
            outbox.Status == IntegrationSyncStatus.Processing &&
            (outbox.ProcessingStartedAt == null || outbox.ProcessingStartedAt <= integrationStaleBefore), cancellationToken);
        int integrationAmbiguous = await integration.CountAsync(outbox =>
            outbox.Status == IntegrationSyncStatus.DeadLettered &&
            outbox.LastError == IntegrationSyncFailureCodes.ProviderOutcomeAmbiguous, cancellationToken);

        IQueryable<IncomingWebhookMessage> incoming = dbContext.IncomingWebhookMessages
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookWorkerCrossTenantQueue);
        int incomingDue = await incoming.CountAsync(message =>
            (message.StatusId == (int)IncomingWebhookMessageStatus.Verified ||
             message.StatusId == (int)IncomingWebhookMessageStatus.RetryDue) &&
            (message.NextAttemptAt == null || message.NextAttemptAt <= observedAt), cancellationToken);
        int incomingStale = await incoming.CountAsync(message =>
            message.StatusId == (int)IncomingWebhookMessageStatus.Processing &&
            (message.ProcessingLeaseExpiresAt == null || message.ProcessingLeaseExpiresAt <= observedAt), cancellationToken);

        IQueryable<WebhookBulkReplayOperation> replay = dbContext.WebhookBulkReplayOperations
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookWorkerCrossTenantQueue);
        int replayQueued = await replay.CountAsync(operation =>
            operation.StatusId == (int)WebhookBulkReplayStatus.Queued, cancellationToken);
        int replayExecuting = await replay.CountAsync(operation =>
            operation.StatusId == (int)WebhookBulkReplayStatus.Executing, cancellationToken);

        IQueryable<WebhookProviderPublication> publication = dbContext.WebhookProviderPublications
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookWorkerCrossTenantQueue);
        int publicationDue = await publication.CountAsync(item =>
            (item.StatusId == (int)WebhookProviderPublicationStatus.Prepared ||
             item.StatusId == (int)WebhookProviderPublicationStatus.RetryDue) &&
            (item.NextActionAt == null || item.NextActionAt <= observedAt), cancellationToken);
        int publicationStale = await publication.CountAsync(item =>
            item.StatusId == (int)WebhookProviderPublicationStatus.Publishing &&
            (item.ProcessingLeaseExpiresAt == null || item.ProcessingLeaseExpiresAt <= observedAt), cancellationToken);
        int publicationUnknown = await publication.CountAsync(item =>
            item.StatusId == (int)WebhookProviderPublicationStatus.PublicationUnknown, cancellationToken);

        IQueryable<PdsSyncOutbox> pds = dbContext.PdsSyncOutbox
            .IgnoreTenantFilter(TenantFilterBypassReasons.AtprotoPdsWorkerCrossTenantQueue);
        int pdsDue = await pds.CountAsync(outbox =>
            outbox.SupersededById == null &&
            (outbox.Status == PdsSyncStatus.Pending || outbox.Status == PdsSyncStatus.Failed) &&
            (outbox.NextRetryAt == null || outbox.NextRetryAt <= observedAt), cancellationToken);
        int pdsStale = await pds.CountAsync(outbox =>
            outbox.Status == PdsSyncStatus.Processing &&
            (outbox.LeaseExpiresAt == null || outbox.LeaseExpiresAt <= observedAt), cancellationToken);
        int pdsDeadLettered = await pds.CountAsync(outbox =>
            outbox.Status == PdsSyncStatus.DeadLettered, cancellationToken);

        return new QueueDrainHealthSnapshot(
            integrationDue,
            integrationStale,
            integrationAmbiguous,
            incomingDue,
            incomingStale,
            replayQueued,
            replayExecuting,
            publicationDue,
            publicationStale,
            publicationUnknown,
            pdsDue,
            pdsStale,
            pdsDeadLettered);
    }
}
