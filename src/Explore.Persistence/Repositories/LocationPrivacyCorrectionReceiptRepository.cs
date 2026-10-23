using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class LocationPrivacyCorrectionReceiptRepository(ExploreDbContext context, TimeProvider clock)
    : ILocationPrivacyCorrectionReceiptRepository
{
    public async Task<LocationPrivacyCorrectionReceipt?> GetClaimedAsync(
        OutboxMessage message, bool deadLetterReconciliation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        Guid? tenant = context.TenantFilterTenantId;
        var status = deadLetterReconciliation ? OutboxMessageStatus.DeadLettered : OutboxMessageStatus.Processing;
        if (tenant is null || tenant == Guid.Empty || context.IsTenantFilterBypassed || message.Status != status
            || message.NextRetryAt is not { } expiry || expiry.Kind != DateTimeKind.Utc
            || expiry <= clock.GetUtcNow().UtcDateTime || message.ProcessedAt is not null)
            return null;

        // Both observations belong to one SQL statement under the immutable Runtime lease.
        // No filter bypass, source graph hydration or erasure/governance re-execution occurs.
        var retained = await (
            from receipt in context.Set<LocationPrivacyCorrectionReceipt>().AsNoTracking()
            join row in context.OutboxMessages.AsNoTracking() on receipt.Id equals row.Id
            where receipt.Id == message.Id && receipt.TenantId == tenant
                && receipt.EventType == row.EventType && receipt.CreatedAtUtc == row.CreatedAt
                && row.AggregateType == nameof(EventLocation) && row.AggregateId == receipt.EventLocationId
                && row.Status == status && row.NextRetryAt == expiry && row.ProcessedAt == null
                && row.AggregateType == message.AggregateType && row.AggregateId == message.AggregateId
                && row.EventType == message.EventType && row.CreatedAt == message.CreatedAt
                && row.Payload == message.Payload && row.DeadLetteredAt == message.DeadLetteredAt
            select receipt).SingleOrDefaultAsync(cancellationToken);
        return expiry <= clock.GetUtcNow().UtcDateTime ? null : retained;
    }
}
