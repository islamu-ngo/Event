using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Federation;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Database.ProviderPrimitives;

internal static class QueueDrainHealthQuery
{
    internal static async Task<QueueDrainHealthSnapshot> ReadAsync(
        ExploreDbContext dbContext,
        DateTime observedAt,
        DateTime integrationStaleBefore,
        CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsNpgsql())
        {
            string command = $$"""
                SELECT "IntegrationDue" AS integration_due, "IntegrationStale" AS integration_stale,
                    "IntegrationAmbiguous" AS integration_ambiguous, "IncomingDue" AS incoming_due,
                    "IncomingStale" AS incoming_stale, "BulkReplayQueued" AS bulk_replay_queued,
                    "BulkReplayExecuting" AS bulk_replay_executing,
                    "ProviderPublicationDue" AS provider_publication_due,
                    "ProviderPublicationStale" AS provider_publication_stale,
                    "ProviderPublicationUnknown" AS provider_publication_unknown,
                    "PdsDue" AS pds_due, "PdsStale" AS pds_stale,
                    "PdsDeadLettered" AS pds_dead_lettered
                FROM {{PostgresQueueDrainHealthContract.FunctionSql(dbContext)}}({0}, {1})
                """;
            return await dbContext.Database.SqlQueryRaw<QueueDrainHealthSnapshot>(
                command, observedAt, integrationStaleBefore).SingleAsync(cancellationToken);
        }

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
            outbox.EvidenceKind == IntegrationSyncEvidenceKind.ProviderOutcomeAmbiguous, cancellationToken);

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
