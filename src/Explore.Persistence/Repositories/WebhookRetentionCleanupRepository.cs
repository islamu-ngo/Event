using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class WebhookRetentionCleanupRepository(ExploreDbContext dbContext)
    : IWebhookRetentionCleanupRepository
{
    public async Task<WebhookRetentionCleanupResult> CleanupTenantAsync(
        Guid tenantId,
        DateTime utcNow,
        int batchSize,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(tenantId, Guid.Empty);
        if (utcNow.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Cleanup time must use UTC kind.", nameof(utcNow));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);

        var outboundPayloadIds = await SelectOutboundPayloadIdsAsync(
            tenantId,
            utcNow,
            batchSize,
            cancellationToken);
        var inboundPayloadIds = await SelectInboundPayloadIdsAsync(
            tenantId,
            utcNow,
            batchSize,
            cancellationToken);
        var deliveryAttemptIds = await SelectDeliveryAttemptIdsAsync(
            tenantId,
            utcNow,
            batchSize,
            cancellationToken);
        var incomingAttemptIds = await SelectIncomingAttemptIdsAsync(
            tenantId,
            utcNow,
            batchSize,
            cancellationToken);
        var incomingRedriveIds = await SelectIncomingRedriveIdsAsync(
            tenantId,
            utcNow,
            batchSize,
            cancellationToken);
        var providerAttemptIds = await SelectProviderAttemptIdsAsync(
            tenantId,
            utcNow,
            batchSize,
            cancellationToken);

        if (dryRun)
        {
            var providerPublicationIds = await SelectProviderPublicationIdsAsync(
                tenantId,
                utcNow,
                batchSize,
                cancellationToken);
            var dryRunAuditIds = await SelectAdministrativeAuditIdsAsync(
                tenantId,
                utcNow,
                batchSize,
                cancellationToken);
            return CreateResult(
                outboundPayloadIds.Count,
                inboundPayloadIds.Count,
                deliveryAttemptIds.Count,
                incomingAttemptIds.Count,
                incomingRedriveIds.Count,
                providerAttemptIds.Count,
                providerPublicationIds.Count,
                dryRunAuditIds.Count,
                dryRun: true);
        }

        await ClearOutboundPayloadsAsync(tenantId, outboundPayloadIds, utcNow, cancellationToken);
        await ClearInboundPayloadsAsync(tenantId, inboundPayloadIds, utcNow, cancellationToken);
        await DeleteByIdsAsync(dbContext.WebhookDeliveryAttempts, tenantId, deliveryAttemptIds, cancellationToken);
        await DeleteByIdsAsync(dbContext.IncomingWebhookProcessingAttempts, tenantId, incomingAttemptIds, cancellationToken);
        await DeleteByIdsAsync(dbContext.IncomingWebhookRedriveRecords, tenantId, incomingRedriveIds, cancellationToken);
        await DeleteByIdsAsync(dbContext.WebhookProviderPublicationAttempts, tenantId, providerAttemptIds, cancellationToken);

        var providerPublicationIdsAfterAttemptCleanup = await SelectProviderPublicationIdsAsync(
            tenantId,
            utcNow,
            batchSize,
            cancellationToken);
        await DeleteByIdsAsync(
            dbContext.WebhookProviderPublications,
            tenantId,
            providerPublicationIdsAfterAttemptCleanup,
            cancellationToken);

        var auditIds = await SelectAdministrativeAuditIdsAsync(
            tenantId,
            utcNow,
            batchSize,
            cancellationToken);
        await DeleteByIdsAsync(dbContext.WebhookAuditEvents, tenantId, auditIds, cancellationToken);

        return CreateResult(
            outboundPayloadIds.Count,
            inboundPayloadIds.Count,
            deliveryAttemptIds.Count,
            incomingAttemptIds.Count,
            incomingRedriveIds.Count,
            providerAttemptIds.Count,
            providerPublicationIdsAfterAttemptCleanup.Count,
            auditIds.Count,
            dryRun: false);
    }

    private async Task<IReadOnlyList<Guid>> SelectOutboundPayloadIdsAsync(
        Guid tenantId,
        DateTime utcNow,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var messages = TenantRows(dbContext.WebhookMessages, tenantId);
        var localTargets = TenantRows(dbContext.WebhookLocalTargetSnapshots, tenantId);
        var publications = TenantRows(dbContext.WebhookProviderPublications, tenantId);
        var holds = ActiveHolds(tenantId, utcNow);
        var unsafeLocalStatuses = new[]
        {
            (int)WebhookLocalDeliveryStatus.Pending,
            (int)WebhookLocalDeliveryStatus.Delivering,
            (int)WebhookLocalDeliveryStatus.RetryDue
        };
        var unsafePublicationStatuses = new[]
        {
            (int)WebhookProviderPublicationStatus.Prepared,
            (int)WebhookProviderPublicationStatus.Publishing,
            (int)WebhookProviderPublicationStatus.RetryDue,
            (int)WebhookProviderPublicationStatus.PublicationUnknown,
            (int)WebhookProviderPublicationStatus.ManualReconciliation
        };

        return await messages
            .Where(message =>
                EF.Property<byte[]?>(message, "_payloadBytes") != null &&
                message.PayloadRetentionUntil <= utcNow &&
                !localTargets.Any(target =>
                    target.WebhookMessageId == message.Id &&
                    unsafeLocalStatuses.Contains(target.DeliveryStatusId)) &&
                !publications.Any(publication =>
                    publication.WebhookMessageId == message.Id &&
                    (unsafePublicationStatuses.Contains(publication.StatusId) ||
                     publication.IdempotencyValidUntil > utcNow)) &&
                !holds.Any(hold =>
                    hold.SubjectKindId == (int)WebhookRetentionSubjectKind.OutgoingMessage &&
                    hold.SubjectId == message.Id))
            .OrderBy(message => message.PayloadRetentionUntil)
            .ThenBy(message => message.Id)
            .Take(batchSize)
            .Select(message => message.Id)
            .ToListAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<Guid>> SelectInboundPayloadIdsAsync(
        Guid tenantId,
        DateTime utcNow,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var messages = TenantRows(dbContext.IncomingWebhookMessages, tenantId);
        var effectPointers = TenantRows(dbContext.IncomingWebhookEffectOutboxes, tenantId);
        var holds = ActiveHolds(tenantId, utcNow);
        var terminalStatuses = IncomingTerminalStatuses();
        return await messages
            .Where(message =>
                EF.Property<byte[]?>(message, "_payloadBytes") != null &&
                terminalStatuses.Contains(message.StatusId) &&
                message.PayloadRetentionUntil <= utcNow &&
                message.ReplayWindowUntil <= utcNow &&
                !effectPointers.Any(pointer =>
                    pointer.IncomingWebhookMessageId == message.Id &&
                    (pointer.Status == OutboxMessageStatus.Pending ||
                     pointer.Status == OutboxMessageStatus.Failed ||
                     pointer.Status == OutboxMessageStatus.Processing)) &&
                !holds.Any(hold =>
                    hold.SubjectKindId == (int)WebhookRetentionSubjectKind.IncomingMessage &&
                    hold.SubjectId == message.Id))
            .OrderBy(message => message.PayloadRetentionUntil)
            .ThenBy(message => message.Id)
            .Take(batchSize)
            .Select(message => message.Id)
            .ToListAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<Guid>> SelectDeliveryAttemptIdsAsync(
        Guid tenantId,
        DateTime utcNow,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var attempts = TenantRows(dbContext.WebhookDeliveryAttempts, tenantId);
        var plans = TenantRows(dbContext.WebhookDeliveryPlanSnapshots, tenantId);
        var targets = TenantRows(dbContext.WebhookLocalTargetSnapshots, tenantId);
        var holds = ActiveHolds(tenantId, utcNow);
        var terminalOutcomes = new[]
        {
            (int)WebhookDeliveryAttemptOutcome.Succeeded,
            (int)WebhookDeliveryAttemptOutcome.Failed,
            (int)WebhookDeliveryAttemptOutcome.Abandoned
        };

        return await (
                from attempt in attempts
                join plan in plans
                    on new { attempt.TenantId, attempt.MessageId }
                    equals new { plan.TenantId, MessageId = plan.WebhookMessageId }
                join target in targets
                    on new { attempt.TenantId, attempt.MessageId, attempt.EndpointId }
                    equals new
                    {
                        target.TenantId,
                        MessageId = target.WebhookMessageId,
                        EndpointId = target.WebhookEndpointId
                    }
                where terminalOutcomes.Contains(attempt.OutcomeId)
                where target.DeliveryStatusId == (int)WebhookLocalDeliveryStatus.Succeeded
                    ? plan.AttemptRetentionUntilUtc <= utcNow
                    : (target.DeliveryStatusId == (int)WebhookLocalDeliveryStatus.DeadLettered ||
                       target.DeliveryStatusId == (int)WebhookLocalDeliveryStatus.Abandoned) &&
                      plan.DeadLetterEvidenceRetentionUntilUtc <= utcNow
                where !holds.Any(hold =>
                    (hold.SubjectKindId == (int)WebhookRetentionSubjectKind.DeliveryAttempt &&
                     hold.SubjectId == attempt.Id) ||
                    (hold.SubjectKindId == (int)WebhookRetentionSubjectKind.OutgoingMessage &&
                     hold.SubjectId == attempt.MessageId))
                orderby attempt.CompletedAt ?? attempt.ScheduledAt, attempt.Id
                select attempt.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<Guid>> SelectIncomingAttemptIdsAsync(
        Guid tenantId,
        DateTime utcNow,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var attempts = TenantRows(dbContext.IncomingWebhookProcessingAttempts, tenantId);
        var messages = TenantRows(dbContext.IncomingWebhookMessages, tenantId);
        var holds = ActiveHolds(tenantId, utcNow);
        var terminalStatuses = IncomingTerminalStatuses();

        return await (
                from attempt in attempts
                join message in messages
                    on new { attempt.TenantId, attempt.IncomingWebhookMessageId }
                    equals new { message.TenantId, IncomingWebhookMessageId = message.Id }
                where terminalStatuses.Contains(message.StatusId)
                where message.StatusId == (int)IncomingWebhookMessageStatus.DeadLettered ||
                      message.StatusId == (int)IncomingWebhookMessageStatus.PayloadConflict
                    ? message.DeadLetterEvidenceRetentionUntil <= utcNow
                    : message.ProcessingAttemptRetentionUntil <= utcNow
                where !holds.Any(hold =>
                    hold.SubjectKindId == (int)WebhookRetentionSubjectKind.IncomingMessage &&
                    hold.SubjectId == message.Id)
                orderby attempt.RecordedAt, attempt.Id
                select attempt.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<Guid>> SelectIncomingRedriveIdsAsync(
        Guid tenantId,
        DateTime utcNow,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var records = TenantRows(dbContext.IncomingWebhookRedriveRecords, tenantId);
        var messages = TenantRows(dbContext.IncomingWebhookMessages, tenantId);
        var holds = ActiveHolds(tenantId, utcNow);
        return await (
                from record in records
                join message in messages
                    on new { record.TenantId, record.IncomingWebhookMessageId }
                    equals new { message.TenantId, IncomingWebhookMessageId = message.Id }
                where message.DeadLetterEvidenceRetentionUntil <= utcNow
                where !holds.Any(hold =>
                    hold.SubjectKindId == (int)WebhookRetentionSubjectKind.IncomingMessage &&
                    hold.SubjectId == message.Id)
                orderby record.CreatedAt, record.Id
                select record.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<Guid>> SelectProviderAttemptIdsAsync(
        Guid tenantId,
        DateTime utcNow,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var attempts = TenantRows(dbContext.WebhookProviderPublicationAttempts, tenantId);
        var publications = TenantRows(dbContext.WebhookProviderPublications, tenantId);
        var plans = TenantRows(dbContext.WebhookDeliveryPlanSnapshots, tenantId);
        var holds = ActiveHolds(tenantId, utcNow);
        return await (
                from attempt in attempts
                join publication in publications
                    on new { attempt.TenantId, attempt.WebhookProviderPublicationId }
                    equals new { publication.TenantId, WebhookProviderPublicationId = publication.Id }
                join plan in plans
                    on new { publication.TenantId, publication.WebhookDeliveryPlanSnapshotId }
                    equals new { plan.TenantId, WebhookDeliveryPlanSnapshotId = plan.Id }
                where publication.StatusId == (int)WebhookProviderPublicationStatus.ProviderQueued
                    ? plan.AttemptRetentionUntilUtc <= utcNow
                    : (publication.StatusId == (int)WebhookProviderPublicationStatus.DeadLettered ||
                       publication.StatusId == (int)WebhookProviderPublicationStatus.Abandoned) &&
                      plan.DeadLetterEvidenceRetentionUntilUtc <= utcNow
                where !holds.Any(hold =>
                    (hold.SubjectKindId == (int)WebhookRetentionSubjectKind.ProviderPublication &&
                     hold.SubjectId == publication.Id) ||
                    (hold.SubjectKindId == (int)WebhookRetentionSubjectKind.OutgoingMessage &&
                     hold.SubjectId == publication.WebhookMessageId))
                orderby attempt.RecordedAt, attempt.Id
                select attempt.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<Guid>> SelectProviderPublicationIdsAsync(
        Guid tenantId,
        DateTime utcNow,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var publications = TenantRows(dbContext.WebhookProviderPublications, tenantId);
        var attempts = TenantRows(dbContext.WebhookProviderPublicationAttempts, tenantId);
        var holds = ActiveHolds(tenantId, utcNow);
        var removableStatuses = new[]
        {
            (int)WebhookProviderPublicationStatus.ProviderQueued,
            (int)WebhookProviderPublicationStatus.DeadLettered,
            (int)WebhookProviderPublicationStatus.Abandoned
        };
        return await publications
            .Where(publication =>
                removableStatuses.Contains(publication.StatusId) &&
                publication.PublicationRetentionUntil <= utcNow &&
                publication.IdempotencyValidUntil <= utcNow &&
                !attempts.Any(attempt => attempt.WebhookProviderPublicationId == publication.Id) &&
                !holds.Any(hold =>
                    (hold.SubjectKindId == (int)WebhookRetentionSubjectKind.ProviderPublication &&
                     hold.SubjectId == publication.Id) ||
                    (hold.SubjectKindId == (int)WebhookRetentionSubjectKind.OutgoingMessage &&
                     hold.SubjectId == publication.WebhookMessageId)))
            .OrderBy(publication => publication.PublicationRetentionUntil)
            .ThenBy(publication => publication.Id)
            .Take(batchSize)
            .Select(publication => publication.Id)
            .ToListAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<Guid>> SelectAdministrativeAuditIdsAsync(
        Guid tenantId,
        DateTime utcNow,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var audits = TenantRows(dbContext.WebhookAuditEvents, tenantId);
        var holds = ActiveHolds(tenantId, utcNow);
        return await audits
            .Where(audit =>
                audit.RetentionUntil <= utcNow &&
                !holds.Any(hold =>
                    hold.SubjectKindId == (int)WebhookRetentionSubjectKind.AdministrativeAudit &&
                    hold.SubjectId == audit.Id))
            .OrderBy(audit => audit.RetentionUntil)
            .ThenBy(audit => audit.Id)
            .Take(batchSize)
            .Select(audit => audit.Id)
            .ToListAsync(cancellationToken);
    }

    private async Task ClearOutboundPayloadsAsync(
        Guid tenantId,
        IReadOnlyList<Guid> ids,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return;
        }

        var messages = await TenantRows(dbContext.WebhookMessages, tenantId)
            .Where(message => ids.Contains(message.Id))
            .ToListAsync(cancellationToken);
        foreach (var message in messages)
        {
            message.ClearPayload(utcNow);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task ClearInboundPayloadsAsync(
        Guid tenantId,
        IReadOnlyList<Guid> ids,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return;
        }

        var messages = await TenantRows(dbContext.IncomingWebhookMessages, tenantId)
            .Where(message => ids.Contains(message.Id))
            .ToListAsync(cancellationToken);
        foreach (var message in messages)
        {
            message.ClearPayload(utcNow);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task DeleteByIdsAsync<TEntity>(
        DbSet<TEntity> set,
        Guid tenantId,
        IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        if (ids.Count == 0)
        {
            return;
        }

        await set
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .Where(entity =>
                EF.Property<Guid>(entity, "TenantId") == tenantId &&
                ids.Contains(EF.Property<Guid>(entity, "Id")))
            .ExecuteDeleteAsync(cancellationToken);
    }

    private IQueryable<WebhookRetentionHold> ActiveHolds(Guid tenantId, DateTime utcNow) =>
        TenantRows(dbContext.WebhookRetentionHolds, tenantId)
            .Where(hold => hold.ReleasedAt == null && (hold.ExpiresAt == null || hold.ExpiresAt > utcNow));

    private static IQueryable<TEntity> TenantRows<TEntity>(DbSet<TEntity> set, Guid tenantId)
        where TEntity : class =>
        set.IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .Where(entity => EF.Property<Guid>(entity, "TenantId") == tenantId);

    private static int[] IncomingTerminalStatuses() =>
    [
        (int)IncomingWebhookMessageStatus.Processed,
        (int)IncomingWebhookMessageStatus.Ignored,
        (int)IncomingWebhookMessageStatus.RejectedPermanent,
        (int)IncomingWebhookMessageStatus.DeadLettered,
        (int)IncomingWebhookMessageStatus.PayloadConflict
    ];

    private static WebhookRetentionCleanupResult CreateResult(
        int outboundPayloadsCleared,
        int inboundPayloadsCleared,
        int deliveryAttemptsDeleted,
        int incomingAttemptsDeleted,
        int incomingRedriveRecordsDeleted,
        int providerAttemptsDeleted,
        int providerPublicationsDeleted,
        int administrativeAuditsDeleted,
        bool dryRun) =>
        new(
            outboundPayloadsCleared,
            inboundPayloadsCleared,
            deliveryAttemptsDeleted,
            incomingAttemptsDeleted,
            incomingRedriveRecordsDeleted,
            providerAttemptsDeleted,
            providerPublicationsDeleted,
            administrativeAuditsDeleted,
            dryRun);
}
