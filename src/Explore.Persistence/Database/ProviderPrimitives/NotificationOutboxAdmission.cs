using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Services;
using Explore.Application.Features.Events.Discovery.Commands;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;

using Explore.Persistence.Repositories;

namespace Explore.Persistence.Database.ProviderPrimitives;

public sealed class NotificationOutboxAdmission(
    INotificationOutboxAdmissionDbContextFactory factory, TimeProvider clock) : INotificationOutboxAdmission
{
    private const string SourceLookupReason =
        "Notification outbox admission resolves only source identity for an exact persisted claimed row.";

    public async Task<IReadOnlyList<NotificationOutboxClaim>> ClaimDueAsync(
        int batchSize, DateTime claimedAt, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(batchSize, 1000);
        if (claimedAt.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Notification claim time must be UTC.", nameof(claimedAt));
        await using var global = await factory.CreateDbContextAsync(cancellationToken);
        if (global.BindTenantLease() is not null)
            throw new InvalidOperationException("notification_outbox_admission_requires_unbound_context");
        if (global.Database.IsNpgsql())
            return await PostgresNotificationOutboxAdmissionContract.ClaimAsync(
                global, batchSize, claimedAt, cancellationToken);

        DateTime now = clock.GetUtcNow().UtcDateTime;
        DateTime observed = claimedAt < now ? claimedAt : now;
        var candidates = await global.OutboxMessages.AsNoTracking()
            .Where(row => row.EventType == EventPublishedOutboxMessageFactory.EventPublishedNotificationFanoutRequestedEventType
                || row.EventType == EventModerationOutboxMessageFactory.EventLightModeratedNotificationFanoutRequestedEventType
                || row.EventType == EventModerationOutboxMessageFactory.EventHeavyRedactedNotificationFanoutRequestedEventType
                || row.EventType == NotificationFanoutOccurrenceOutboxMessageFactory.EventType
                || row.EventType == EventDiscoveryIdentityCorrectionRequested.EventType)
            .Where(row => (row.Status == OutboxMessageStatus.Pending && (row.NextRetryAt == null || row.NextRetryAt <= observed))
                || (row.Status == OutboxMessageStatus.Processing && row.NextRetryAt <= observed))
            .OrderBy(row => row.CreatedAt).ThenBy(row => row.Id).Select(row => row.Id)
            .Take(batchSize).ToListAsync(cancellationToken);
        var result = new List<NotificationOutboxClaim>();
        var repository = new OutboxRepository(global);
        foreach (Guid id in candidates)
        {
            DateTime? lease = await repository.TryClaimForProcessing(id, observed, cancellationToken);
            if (lease is null)
                continue;
            NotificationOutboxClaim? claim = await ReadPortableClaimAsync(global, id, lease.Value, cancellationToken);
            if (claim is not null)
                result.Add(claim);
            else
                await global.OutboxMessages
                    .Where(row => row.Id == id && row.Status == OutboxMessageStatus.Processing && row.NextRetryAt == lease.Value)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Status, OutboxMessageStatus.Failed)
                        .SetProperty(row => row.NextRetryAt, (DateTime?)null)
                        .SetProperty(row => row.LastError, "notification_source_not_admitted"), cancellationToken);
        }
        return result;
    }

    public async Task<NotificationOutboxClaim?> ReadClaimAsync(
        Guid messageId, DateTime processingLeaseExpiresAt, CancellationToken cancellationToken)
    {
        if (messageId == Guid.Empty || processingLeaseExpiresAt.Kind != DateTimeKind.Utc)
            return null;
        await using var global = await factory.CreateDbContextAsync(cancellationToken);
        if (global.BindTenantLease() is not null)
            throw new InvalidOperationException("notification_outbox_admission_requires_unbound_context");
        return global.Database.IsNpgsql()
            ? await PostgresNotificationOutboxAdmissionContract.ReadAsync(
                global, messageId, processingLeaseExpiresAt, cancellationToken)
            : await ReadPortableClaimAsync(global, messageId, processingLeaseExpiresAt, cancellationToken);
    }

    private async Task<NotificationOutboxClaim?> ReadPortableClaimAsync(
        ExploreDbContext global, Guid messageId, DateTime lease, CancellationToken cancellationToken)
    {
        DateTime observedAt = clock.GetUtcNow().UtcDateTime;
        var message = await global.OutboxMessages.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == messageId && row.Status == OutboxMessageStatus.Processing
                && row.NextRetryAt == lease && row.NextRetryAt > observedAt, cancellationToken);
        if (message?.Payload is null || !NotificationOutboxRoutes.Contains(message.EventType))
            return null;
        try
        {
            using var json = JsonDocument.Parse(message.Payload);
            if (json.RootElement.ValueKind != JsonValueKind.Object)
                return null;
            var members = json.RootElement.EnumerateObject().ToArray();
            if (members.GroupBy(member => member.Name, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() != 1))
                return null;
            Guid Id(string name)
            {
                JsonElement value = members.SingleOrDefault(member =>
                    member.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;
                return value.ValueKind == JsonValueKind.String && value.TryGetGuid(out Guid id) ? id : Guid.Empty;
            }
            Guid postedTenant = Id("TenantId");
            Guid? tenant = null;
            Guid sourceId = message.AggregateId;
            if (message.EventType == EventDiscoveryIdentityCorrectionRequested.EventType)
            {
                if (message.AggregateType != "EventDiscoveryIdentity" || Id("EventId") != message.AggregateId)
                    return null;
                string eventId = message.AggregateId.ToString("D");
                var receipts = await global.Set<AuditLog>().IgnoreTenantFilter(SourceLookupReason).AsNoTracking()
                    .Where(audit => audit.TenantId == postedTenant
                        && audit.EntityType == "EventDiscoveryIdentity" && audit.EntityId == eventId
                        && (audit.Action == "EventDiscoveryAliasConfirmed"
                            || audit.Action == "EventDiscoveryAliasUnlinked"
                            || audit.Action == "EventDiscoveryIndependentReview")
                        && audit.Timestamp == message.CreatedAt && audit.NewValues == message.Payload)
                    .Select(audit => new { audit.Id, audit.TenantId }).Take(2).ToListAsync(cancellationToken);
                if (receipts.Count != 1)
                    return null;
                sourceId = receipts[0].Id;
                tenant = receipts[0].TenantId;
            }
            else if (message.EventType == NotificationFanoutOccurrenceOutboxMessageFactory.EventType)
            {
                if (message.AggregateType != nameof(NotificationFanoutOccurrence) || Id("OccurrenceId") != sourceId)
                    return null;
                tenant = await global.NotificationFanoutOccurrences.IgnoreTenantFilter(SourceLookupReason)
                    .Where(row => row.Id == sourceId).Select(row => (Guid?)row.TenantId)
                    .SingleOrDefaultAsync(cancellationToken);
            }
            else if (message.AggregateType == nameof(Explore.Domain.Event))
            {
                if (message.EventType == EventPublishedOutboxMessageFactory.EventPublishedNotificationFanoutRequestedEventType)
                {
                    if (Id("EventId") != message.AggregateId)
                        return null;
                    tenant = await global.Events.IgnoreTenantFilter(SourceLookupReason)
                        .Where(row => row.Id == sourceId).Select(row => (Guid?)row.TenantId)
                        .SingleOrDefaultAsync(cancellationToken);
                }
                else
                {
                    sourceId = Id("ModerationRecordId");
                    bool heavy = message.EventType ==
                        EventModerationOutboxMessageFactory.EventHeavyRedactedNotificationFanoutRequestedEventType;
                    if (!heavy && Id("EventId") != message.AggregateId)
                        return null;
                    tenant = await global.EventModerationRecords.IgnoreTenantFilter(SourceLookupReason)
                        .Where(row => row.Id == sourceId && row.EventId == message.AggregateId
                            && row.ActionKind == (heavy ? EventModerationActionKind.HeavyRedacted : EventModerationActionKind.LightModerated)
                            && row.IsIrreversible == heavy)
                        .Select(row => (Guid?)row.TenantId).SingleOrDefaultAsync(cancellationToken);
                }
            }
            return lease > clock.GetUtcNow().UtcDateTime &&
                tenant is { } owner && owner != Guid.Empty && postedTenant == owner
                ? new NotificationOutboxClaim(message.Id, owner, sourceId, message.AggregateId,
                    message.AggregateType, message.EventType,
                    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(message.Payload))), lease)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
