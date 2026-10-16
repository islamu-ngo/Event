using Explore.Domain.Enums;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

/// <summary>
/// Identity-only effect ownership retained in the original correction transaction.
/// Id is the original outbox message id. No foreign key to disposable PII or source
/// aggregates is retained; an erased source cannot revoke committed correction custody.
/// </summary>
public sealed class LocationPrivacyCorrectionReceipt : ITenantEntity
{
    private LocationPrivacyCorrectionReceipt() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid EventId { get; private set; }
    public Guid EventLocationId { get; private set; }
    public int PolicyVersion { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? DisclosureAuditId { get; private set; }
    public Guid? ErasureIntentId { get; private set; }
    public long? AuthoritySequence { get; private set; }
    public Guid? LocationId { get; private set; }
    public string EventType { get; private set; } = null!;

    Guid ITenantEntity.TenantId
    {
        get => TenantId;
        set
        {
            if (value != TenantId)
                throw new InvalidOperationException("Correction receipt tenant ownership is immutable.");
        }
    }

    public static LocationPrivacyCorrectionReceipt ForGovernance(
        Guid messageId, Guid eventId, EventLocationDisclosureAudit audit, DateTime createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(audit);
        if (audit.CorrectionMessageId != messageId || audit.OccurredAtUtc != createdAtUtc
            || audit.Reason is not EventLocationDisclosureAuditReasonEnum.GovernanceTightening
                and not EventLocationDisclosureAuditReasonEnum.PrivacyErasureRemediation)
            throw new InvalidOperationException("Correction must be paired with its native disclosure transition.");
        RequireId(audit.Id);
        var receipt = Create(messageId, audit.TenantId, eventId, audit.EventLocationId,
            audit.NewPolicyVersion, createdAtUtc, "location.privacy.corrected");
        receipt.DisclosureAuditId = audit.Id;
        return receipt;
    }

    public static LocationPrivacyCorrectionReceipt ForErasure(
        Guid messageId, PrivacyErasureIntent intent, Guid tenantId, Guid eventId,
        Guid eventLocationId, Guid? locationId, int policyVersion, DateTime createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(intent);
        RequireId(intent.IntentId);
        if (intent.AuthoritySequence <= 0 || createdAtUtc < intent.RecordedAtUtc
            || locationId is not { } location || location == Guid.Empty)
            throw new InvalidOperationException("Erasure correction must retain its owned effect and authority sequence.");
        var receipt = Create(messageId, tenantId, eventId, eventLocationId, policyVersion,
            createdAtUtc, "LocationPrivacyCorrectionRequested");
        receipt.ErasureIntentId = intent.IntentId;
        receipt.AuthoritySequence = intent.AuthoritySequence;
        receipt.LocationId = location;
        return receipt;
    }

    private static LocationPrivacyCorrectionReceipt Create(
        Guid id, Guid tenantId, Guid eventId, Guid eventLocationId, int policyVersion,
        DateTime createdAtUtc, string eventType)
    {
        RequireId(id);
        if (id.Version != 7 || id.Variant is < 8 or > 11)
            throw new ArgumentException("Correction message id must be UUIDv7.", nameof(id));
        RequireId(tenantId);
        RequireId(eventId);
        RequireId(eventLocationId);
        if (policyVersion <= 0 || createdAtUtc == default || createdAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Correction policy and UTC creation time are required.");
        return new()
        {
            Id = id,
            TenantId = tenantId,
            EventId = eventId,
            EventLocationId = eventLocationId,
            PolicyVersion = policyVersion,
            CreatedAtUtc = createdAtUtc,
            EventType = eventType
        };
    }

    private static void RequireId(Guid id)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Correction ownership requires non-empty identities.");
    }
}
