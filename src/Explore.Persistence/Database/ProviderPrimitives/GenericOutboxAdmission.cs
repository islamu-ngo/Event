using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Admissions;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;

using Explore.Persistence.Repositories;

namespace Explore.Persistence.Database.ProviderPrimitives;

public sealed class GenericOutboxAdmission(IGenericOutboxAdmissionDbContextFactory factory, TimeProvider clock)
    : IGenericOutboxAdmission
{
    private const string SourceLookupReason =
        "Generic outbox admission resolves native ownership identities for one persisted claimed envelope.";

    public async Task<IReadOnlyList<GenericOutboxClaim>> ClaimDueAsync(
        int batchSize, DateTime claimedAt, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(batchSize, 1000);
        if (claimedAt.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Outbox claim time must be UTC.", nameof(claimedAt));
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        RequireUnbound(context);
        if (context.Database.IsNpgsql())
            return await PostgresGenericOutboxAdmissionContract.ClaimAsync(
                context, batchSize, claimedAt, cancellationToken);
        DateTime observedAt = clock.GetUtcNow().UtcDateTime;
        DateTime now = claimedAt < observedAt ? claimedAt : observedAt;
        var repository = new OutboxRepository(context);
        var candidates = await repository.GetPendingBatch(batchSize, cancellationToken);
        var claims = new List<GenericOutboxClaim>();
        foreach (var candidate in candidates)
        {
            var status = candidate.Status == OutboxMessageStatus.DeadLettered
                ? OutboxMessageStatus.DeadLettered : OutboxMessageStatus.Processing;
            DateTime? expiry = status == OutboxMessageStatus.DeadLettered
                ? await repository.TryClaimDeadLetterReconciliation(candidate.Id, now, cancellationToken)
                : await repository.TryClaimForProcessing(candidate.Id, now, cancellationToken);
            if (expiry is null)
                continue;
            var claim = await ReadPortableAsync(context, candidate.Id, expiry.Value, status, cancellationToken);
            if (claim is not null)
                claims.Add(claim);
            else
                await context.OutboxMessages.Where(row => row.Id == candidate.Id
                        && row.Status == status && row.NextRetryAt == expiry.Value)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(row => row.Status, status == OutboxMessageStatus.DeadLettered
                            ? OutboxMessageStatus.DeadLettered : OutboxMessageStatus.Failed)
                        .SetProperty(row => row.LastError, "generic_source_not_admitted"), cancellationToken);
        }
        return claims;
    }

    public async Task<GenericOutboxClaim?> ReadClaimAsync(
        Guid messageId, DateTime leaseExpiresAt, OutboxMessageStatus status, CancellationToken cancellationToken)
    {
        if (messageId == Guid.Empty || leaseExpiresAt.Kind != DateTimeKind.Utc
            || status is not (OutboxMessageStatus.Processing or OutboxMessageStatus.DeadLettered))
            return null;
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        RequireUnbound(context);
        return context.Database.IsNpgsql()
            ? await PostgresGenericOutboxAdmissionContract.ReadAsync(
                context, messageId, leaseExpiresAt, status, cancellationToken)
            : await ReadPortableAsync(context, messageId, leaseExpiresAt, status, cancellationToken);
    }

    internal static string PayloadDigest(string? payload) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload is null ? "0" : "1" + payload)));

    private static void RequireUnbound(ExploreDbContext context)
    {
        if (context.BindTenantLease() is not null || context.IsTenantFilterBypassed)
            throw new InvalidOperationException("generic_outbox_admission_requires_unbound_context");
    }

    private async Task<GenericOutboxClaim?> ReadPortableAsync(
        ExploreDbContext context, Guid id, DateTime expiry, OutboxMessageStatus status, CancellationToken ct)
    {
        DateTime observedAt = clock.GetUtcNow().UtcDateTime;
        var message = await context.OutboxMessages.AsNoTracking().SingleOrDefaultAsync(row =>
            row.Id == id && row.Status == status && row.NextRetryAt == expiry && row.NextRetryAt > observedAt, ct);
        if (message is null || !GenericOutboxRoutes.Contains(message.EventType)
            || message.AggregateId == Guid.Empty
            || status == OutboxMessageStatus.DeadLettered && !GenericOutboxRoutes.SupportsDeadLetter(message.EventType))
            return null;
        try
        {
            using var json = message.Payload is null ? null : JsonDocument.Parse(message.Payload);
            if (json is not null)
            {
                if (message.EventType == "ConfigurationImportEffectsRequested")
                {
                    if (json.RootElement.ValueKind != JsonValueKind.Array)
                        return null;
                }
                else if (json.RootElement.ValueKind != JsonValueKind.Object
                    || json.RootElement.EnumerateObject().GroupBy(member => member.Name, StringComparer.OrdinalIgnoreCase)
                        .Any(group => group.Count() != 1))
                    return null;
            }
            Guid Id(string name) => json is not null && json.RootElement.TryGetProperty(name, out var value)
                && value.ValueKind == JsonValueKind.String && value.TryGetGuid(out Guid valueId) ? valueId : Guid.Empty;
            int Number(string name) => json is not null && json.RootElement.TryGetProperty(name, out var value)
                && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number) ? number : -1;
            string Identity(params Guid[] ids) => string.Join(":", ids.Select(value => value.ToString("D")));
            GenericOutboxClaim? Claim(Guid? tenant, GenericOutboxOwnership ownership, Guid source, string identity)
            {
                if (expiry <= clock.GetUtcNow().UtcDateTime || source == Guid.Empty || tenant == Guid.Empty
                    || ownership == GenericOutboxOwnership.Tenant && tenant is null
                    || ownership != GenericOutboxOwnership.Tenant && tenant is not null)
                    return null;
                return new(message.Id, tenant, ownership, source, identity, message.AggregateId,
                    message.AggregateType, message.EventType, PayloadDigest(message.Payload), message.CreatedAt, status, expiry);
            }
            GenericOutboxClaim? Tenant(Guid tenant, Guid source, string identity) =>
                Id("TenantId") == tenant ? Claim(tenant, GenericOutboxOwnership.Tenant, source, identity) : null;
            Guid aggregate = message.AggregateId;
            switch (message.EventType)
            {
                case "PrivacyErasureCacheInvalidationRequested":
                    return message.AggregateType == nameof(User) && message.Payload is null
                        ? Claim(null, GenericOutboxOwnership.Subject, aggregate, Identity(aggregate)) : null;
                case "LocationPiiErased":
                    return message.AggregateType == nameof(Location) && Id("LocationId") == aggregate
                        && Id("IntentId") != Guid.Empty
                        ? Claim(null, GenericOutboxOwnership.Subject, aggregate, Identity(aggregate, Id("IntentId"))) : null;
                case "OperatorIdentityImported":
                    return message.AggregateType == "InstanceOperatorIdentity" && json is not null
                        ? Claim(null, GenericOutboxOwnership.Instance, aggregate, Identity(aggregate)) : null;
                case "EventReportProviderSyncRequested":
                    {
                        if (message.AggregateType != nameof(EventReport) || Id("ReportId") != aggregate)
                            return null;
                        var report = await context.Set<EventReport>().IgnoreTenantFilter(SourceLookupReason).AsNoTracking()
                            .Where(row => row.Id == aggregate).Select(row => new
                            {
                                row.TenantId,
                                row.EventId,
                                row.CreatedAt,
                                row.ProviderSyncOutboxId,
                                row.ProviderSyncCaseId,
                                row.ProviderSyncCaseConcurrencyStamp,
                                row.ProviderSyncSubmittedAtUtc,
                                row.ProviderSyncPayloadDigest
                            }).SingleOrDefaultAsync(ct);
                        Guid caseId = Id("CaseId");
                        if (report is null || Id("EventId") != report.EventId
                            || report.ProviderSyncOutboxId != message.Id || report.ProviderSyncCaseId != caseId
                            || report.ProviderSyncCaseConcurrencyStamp is not { } submissionVersion
                            || submissionVersion == Guid.Empty || Id("CaseConcurrencyStamp") != submissionVersion
                            || report.ProviderSyncSubmittedAtUtc is not { } submittedAt
                            || submittedAt != report.CreatedAt || submittedAt != message.CreatedAt
                            || json is null || !json.RootElement.TryGetProperty("SubmittedAtUtc", out var submitted)
                            || submitted.ValueKind != JsonValueKind.String || !submitted.TryGetDateTime(out DateTime postedAt)
                            || postedAt.Kind != DateTimeKind.Utc || postedAt != submittedAt
                            || report.ProviderSyncPayloadDigest is not { Length: 64 } submissionDigest
                            || !submissionDigest.All(char.IsAsciiHexDigitUpper)
                            || submissionDigest != Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(message.Payload!)))
                            || !await context.Set<EventReportCase>().IgnoreTenantFilter(SourceLookupReason).AsNoTracking()
                                .AnyAsync(row => row.Id == caseId && row.ReportId == aggregate && row.TenantId == report.TenantId, ct))
                            return null;
                        return Tenant(report.TenantId, aggregate,
                            Identity(aggregate, report.EventId, caseId, submissionVersion) + ":" + submissionDigest);
                    }
                case "LocationPrivacyCorrectionRequested":
                case "location.privacy.corrected":
                    {
                        if (message.AggregateType != nameof(EventLocation) || Id("EventLocationId") != aggregate)
                            return null;
                        bool erasure = message.EventType == "LocationPrivacyCorrectionRequested";
                        var receipt = await context.Set<LocationPrivacyCorrectionReceipt>()
                            .IgnoreTenantFilter(SourceLookupReason).AsNoTracking()
                            .Where(row => row.Id == message.Id && row.EventLocationId == aggregate
                                && row.EventType == message.EventType && row.CreatedAtUtc == message.CreatedAt)
                            .Select(row => new
                            {
                                row.Id,
                                row.TenantId,
                                row.EventId,
                                row.PolicyVersion,
                                row.DisclosureAuditId,
                                row.ErasureIntentId,
                                row.AuthoritySequence,
                                row.LocationId
                            })
                            .SingleOrDefaultAsync(ct);
                        if (receipt is null || Id("EventId") != receipt.EventId
                            || Number("PolicyVersion") != receipt.PolicyVersion || receipt.PolicyVersion <= 0
                            || Number("SchemaVersion") != 1)
                            return null;
                        if (erasure)
                        {
                            if (receipt.DisclosureAuditId is not null || receipt.ErasureIntentId is not { } intent
                                || intent == Guid.Empty || Id("IntentId") != intent
                                || receipt.LocationId is not { } location || location == Guid.Empty
                                || Id("LocationId") != location || receipt.AuthoritySequence is not > 0
                                || Number("SchemaVersion") != 1 || json is null
                                || !json.RootElement.TryGetProperty("AuthoritySequence", out var sequence)
                                || sequence.ValueKind != JsonValueKind.Number
                                || !sequence.TryGetInt64(out long postedSequence) || postedSequence != receipt.AuthoritySequence)
                                return null;
                            return Tenant(receipt.TenantId, receipt.Id,
                                Identity(receipt.Id, aggregate, receipt.EventId, intent, location) + ":"
                                + postedSequence.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":"
                                + receipt.PolicyVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        }
                        if (receipt.DisclosureAuditId is not { } audit || audit == Guid.Empty
                            || receipt.ErasureIntentId is not null || receipt.AuthoritySequence is not null
                            || receipt.LocationId is not null)
                            return null;
                        return Tenant(receipt.TenantId, receipt.Id,
                            Identity(receipt.Id, aggregate, receipt.EventId, audit) + ":"
                            + receipt.PolicyVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    }
                case "RegistrationOrderRejected":
                case "RegistrationOrderConfirmed":
                case "RegistrationOrderCancelled":
                    {
                        if (message.AggregateType != nameof(RegistrationOrder) || Id("RegistrationOrderId") != aggregate)
                            return null;
                        var order = await context.Set<RegistrationOrder>().IgnoreTenantFilter(SourceLookupReason).AsNoTracking()
                            .Where(row => row.Id == aggregate)
                            .Select(row => new { row.TenantId, row.EventId, row.RegistrationOrderStatusId }).SingleOrDefaultAsync(ct);
                        if (order is null || Id("EventId") != order.EventId
                            || message.EventType == "RegistrationOrderCancelled"
                                && order.RegistrationOrderStatusId != (int)RegistrationOrderStatusEnum.Cancelled)
                            return null;
                        return Tenant(order.TenantId, aggregate, Identity(aggregate, order.EventId));
                    }
                case "admission.event_cancellation.requested":
                    {
                        if (message.AggregateType != nameof(AdmissionTicket) || Id("EventId") != aggregate)
                            return null;
                        var tenant = await context.Events.IgnoreTenantFilter(SourceLookupReason).AsNoTracking()
                            .Where(row => row.Id == aggregate && row.EventStatusId == (int)EventStatusEnum.Cancelled)
                            .Select(row => (Guid?)row.TenantId).SingleOrDefaultAsync(ct);
                        return tenant is { } owner ? Tenant(owner, aggregate, Identity(aggregate)) : null;
                    }
                case "AdmissionCredentialDeliveryRequested":
                    {
                        if (message.AggregateType != nameof(AdmissionTicket) || Id("DeliveryIntentId") != message.Id
                            || Id("AdmissionTicketId") != aggregate)
                            return null;
                        var tenant = await context.Set<AdmissionDeliveryIntent>().IgnoreTenantFilter(SourceLookupReason).AsNoTracking()
                            .Where(row => row.Id == message.Id && row.AdmissionTicketId == aggregate)
                            .Select(row => (Guid?)row.TenantId).SingleOrDefaultAsync(ct);
                        return tenant is { } owner ? Tenant(owner, message.Id, Identity(message.Id, aggregate)) : null;
                    }
                case "AdmissionRecoveryRequestProcessingRequested":
                    {
                        if (message.AggregateType != nameof(AdmissionRecoveryRequestIntent) || aggregate != message.Id
                            || Id("RequestIntentId") != message.Id)
                            return null;
                        var tenant = await context.Set<AdmissionRecoveryRequestIntent>().IgnoreTenantFilter(SourceLookupReason).AsNoTracking()
                            .Where(row => row.Id == message.Id).Select(row => (Guid?)row.TenantId).SingleOrDefaultAsync(ct);
                        return tenant is { } owner ? Tenant(owner, message.Id, Identity(message.Id)) : null;
                    }
                case "AdmissionRecoveryDeliveryRequested":
                    {
                        if (message.AggregateType != nameof(AdmissionRecoveryCapability)
                            || Id("DeliveryIntentId") != message.Id || Id("AdmissionTicketId") != aggregate)
                            return null;
                        var intent = await context.Set<AdmissionRecoveryDeliveryIntent>().IgnoreTenantFilter(SourceLookupReason).AsNoTracking()
                            .Where(row => row.Id == message.Id && row.AdmissionTicketId == aggregate)
                            .Select(row => new { row.TenantId, row.RecoveryRequestId }).SingleOrDefaultAsync(ct);
                        return intent is not null
                            ? Tenant(intent.TenantId, message.Id, Identity(message.Id, aggregate, intent.RecoveryRequestId)) : null;
                    }
                case "registration.refund_campaign.process_requested":
                    {
                        if (message.AggregateType != nameof(RefundCampaign) || Id("CampaignId") != aggregate)
                            return null;
                        var campaign = await context.Set<RefundCampaign>().IgnoreTenantFilter(SourceLookupReason).AsNoTracking()
                            .Where(row => row.Id == aggregate).Select(row => new { row.TenantId, row.EventId }).SingleOrDefaultAsync(ct);
                        return campaign is not null ? Tenant(campaign.TenantId, aggregate, Identity(aggregate, campaign.EventId)) : null;
                    }
                case "registration.refund.dispatch_requested":
                case "registration.refund.reconciliation_requested":
                    {
                        if (message.AggregateType != nameof(RefundAttempt) || Id("RefundAttemptId") != aggregate)
                            return null;
                        var refund = await context.Set<RefundAttempt>().IgnoreTenantFilter(SourceLookupReason).AsNoTracking()
                            .Where(row => row.Id == aggregate).Select(row => new
                            { row.TenantId, row.PaymentAttemptId, row.RegistrationOrderId, row.PaidOrderAcceptanceSnapshotId })
                            .SingleOrDefaultAsync(ct);
                        return refund is not null ? Tenant(refund.TenantId, aggregate,
                            Identity(aggregate, refund.PaymentAttemptId, refund.RegistrationOrderId, refund.PaidOrderAcceptanceSnapshotId)) : null;
                    }
                case "registration.payment.cancellation_requested":
                    {
                        if (message.AggregateType != nameof(PaymentAttempt) || Id("PaymentAttemptId") != aggregate)
                            return null;
                        Guid campaignId = Id("CampaignId");
                        var source = await (
                            from payment in context.Set<PaymentAttempt>().IgnoreTenantFilter(SourceLookupReason).AsNoTracking()
                            join order in context.Set<RegistrationOrder>().IgnoreTenantFilter(SourceLookupReason).AsNoTracking()
                                on new { payment.TenantId, Id = payment.RegistrationOrderId } equals new { order.TenantId, order.Id }
                            join campaign in context.Set<RefundCampaign>().IgnoreTenantFilter(SourceLookupReason).AsNoTracking()
                                on new { order.TenantId, order.EventId } equals new { campaign.TenantId, campaign.EventId }
                            where payment.Id == aggregate && campaign.Id == campaignId && campaign.Kind == RefundCampaignKind.EventCancellation
                            select new { payment.TenantId, order.Id, order.EventId }).SingleOrDefaultAsync(ct);
                        return source is not null ? Tenant(source.TenantId, aggregate,
                            Identity(aggregate, source.Id, campaignId, source.EventId)) : null;
                    }
                case "ConfigurationManifestEffectsRequested":
                    return message.AggregateType == nameof(ConfigurationManifestOperation) && message.Payload is not null
                        && await context.Set<ConfigurationManifestOperation>().AsNoTracking().AnyAsync(row =>
                            row.Id == aggregate && row.Status == ConfigurationManifestOperationStatus.Applied
                            && row.EffectOutboxId == message.Id, ct)
                        ? Claim(null, GenericOutboxOwnership.Instance, aggregate, Identity(aggregate, message.Id)) : null;
                case "ConfigurationImportEffectsRequested":
                    {
                        if (message.AggregateType != nameof(ConfigurationImportOperation))
                            return null;
                        var operation = await context.Set<ConfigurationImportOperation>().AsNoTracking()
                            .Where(row => row.Id == aggregate && row.EffectOutboxId == message.Id
                                && (row.Status == ConfigurationImportOperationStatus.Applied
                                    || row.Status == ConfigurationImportOperationStatus.RolledBack))
                            .Select(row => new { row.TargetTenantId, row.EffectPayloadDigest }).SingleOrDefaultAsync(ct);
                        if (operation?.EffectPayloadDigest is not { Length: 64 } effectDigest
                            || !effectDigest.All(char.IsAsciiHexDigitUpper)
                            || effectDigest != Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(message.Payload ?? string.Empty))))
                            return null;
                        return Claim(operation.TargetTenantId,
                            operation.TargetTenantId is null ? GenericOutboxOwnership.Instance : GenericOutboxOwnership.Tenant,
                            aggregate, Identity(aggregate, message.Id) + ":" + effectDigest);
                    }
                case "ManagedTenantProvisioningProcessRequested":
                    {
                        if (message.AggregateType != nameof(ManagedTenantProvisioningOperation) || message.Payload is not null)
                            return null;
                        Guid? instance = await context.Set<ManagedTenantProvisioningOperation>().AsNoTracking()
                            .Where(row => row.Id == aggregate && row.CurrentOutboxMessageId == message.Id)
                            .Select(row => (Guid?)row.ManagedInstanceId).SingleOrDefaultAsync(ct);
                        return instance is { } owner
                            ? Claim(null, GenericOutboxOwnership.Instance, aggregate, Identity(aggregate, owner, message.Id)) : null;
                    }
                default:
                    return null;
            }
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
