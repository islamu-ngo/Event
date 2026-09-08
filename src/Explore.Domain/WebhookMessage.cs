using System.Security.Cryptography;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public sealed class WebhookMessage : ITenantEntity, IAuditableEntity
{
    public const int MaxEventTypeLength = 200;
    public const int MaxEventIdLength = 200;
    public const int MaxAggregateKindLength = 100;
    public const int MaxContentTypeLength = 200;
    public const int MaxContentEncodingLength = 50;
    public const int PayloadHashLength = 71;

    private byte[]? _payloadBytes;

    private WebhookMessage()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; private set; }

    public string EventType { get; private set; } = string.Empty;
    public string EventId { get; private set; } = string.Empty;
    public string AggregateKind { get; private set; } = string.Empty;
    public Guid AggregateId { get; private set; }
    public Guid? ConsumerId { get; private set; }
    public WebhookConsumer? Consumer { get; private set; }
    public string PayloadHash { get; private set; } = string.Empty;
    public long PayloadByteLength { get; private set; }
    public int PayloadProvenanceId { get; private set; }
    public WebhookPayloadProvenanceLookup PayloadProvenanceLookup { get; private set; } = null!;
    public string ContentType { get; private set; } = string.Empty;
    public string ContentEncoding { get; private set; } = string.Empty;
    public DateTime OccurredAt { get; private set; }
    public DateTime MaterializedAt { get; private set; }
    public DateTime PayloadRetentionUntil { get; private set; }
    public DateTime? PayloadClearedAt { get; private set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public static WebhookMessage Create(
        Guid tenantId,
        string eventType,
        string eventId,
        string aggregateKind,
        Guid aggregateId,
        Guid? consumerId,
        ReadOnlySpan<byte> payloadBytes,
        string contentType,
        string contentEncoding,
        DateTime occurredAt,
        DateTime payloadRetentionUntil,
        DateTime materializedAt)
    {
        RequireGuid(tenantId, nameof(tenantId));
        RequireGuid(aggregateId, nameof(aggregateId));
        if (consumerId == Guid.Empty)
        {
            throw new ArgumentException("Consumer id cannot be empty when supplied.", nameof(consumerId));
        }

        if (payloadBytes.IsEmpty)
        {
            throw new ArgumentException("Webhook payload bytes are required.", nameof(payloadBytes));
        }

        RequireUtc(occurredAt, nameof(occurredAt));
        RequireUtc(materializedAt, nameof(materializedAt));
        RequireUtc(payloadRetentionUntil, nameof(payloadRetentionUntil));
        if (occurredAt > materializedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(occurredAt), "Occurrence cannot be after materialization.");
        }

        if (payloadRetentionUntil <= materializedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(payloadRetentionUntil),
                "Payload retention must end after message creation.");
        }

        var ownedPayload = payloadBytes.ToArray();
        return new WebhookMessage
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            EventType = NormalizeRequired(eventType, MaxEventTypeLength, nameof(eventType)),
            EventId = NormalizeRequired(eventId, MaxEventIdLength, nameof(eventId)),
            AggregateKind = NormalizeRequired(aggregateKind, MaxAggregateKindLength, nameof(aggregateKind)),
            AggregateId = aggregateId,
            ConsumerId = consumerId,
            _payloadBytes = ownedPayload,
            PayloadHash = ComputePayloadHash(ownedPayload),
            PayloadByteLength = ownedPayload.LongLength,
            PayloadProvenanceId = (int)WebhookPayloadProvenance.ExactBytes,
            ContentType = NormalizeRequired(contentType, MaxContentTypeLength, nameof(contentType)).ToLowerInvariant(),
            ContentEncoding = NormalizeRequired(contentEncoding, MaxContentEncodingLength, nameof(contentEncoding)).ToLowerInvariant(),
            OccurredAt = occurredAt,
            MaterializedAt = materializedAt,
            PayloadRetentionUntil = payloadRetentionUntil,
            CreatedAt = materializedAt
        };
    }

    public byte[]? GetPayloadBytes() => _payloadBytes?.ToArray();

    public void ClearPayload(DateTime clearedAt)
    {
        RequireUtc(clearedAt, nameof(clearedAt));
        if (clearedAt < PayloadRetentionUntil)
        {
            throw new InvalidOperationException("Webhook payload cannot be cleared before retention expires.");
        }

        if (_payloadBytes is null)
        {
            return;
        }

        _payloadBytes = null;
        PayloadClearedAt = clearedAt;
        UpdatedAt = clearedAt;
    }

    private static string ComputePayloadHash(ReadOnlySpan<byte> payloadBytes) =>
        $"sha256:{Convert.ToHexString(SHA256.HashData(payloadBytes)).ToLowerInvariant()}";

    private static string NormalizeRequired(string value, int maxLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", parameterName);
        }

        var normalized = value.Trim();
        if (normalized.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(parameterName, $"Value cannot exceed {maxLength} characters.");
        }

        return normalized;
    }

    private static void RequireGuid(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A non-empty identifier is required.", parameterName);
        }
    }

    private static void RequireUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Timestamp must use UTC kind.", parameterName);
        }
    }
}
