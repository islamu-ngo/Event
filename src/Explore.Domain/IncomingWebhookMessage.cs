using System.ComponentModel.DataAnnotations.Schema;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class IncomingWebhookMessage : ITenantEntity, IAuditableEntity
{
    public const int MaxProviderLength = 100;
    public const int MaxProviderMessageIdLength = 256;
    public const int MaxIdempotencyKeyLength = 256;
    public const int MaxEventTypeLength = 200;
    public const int MaxContentTypeLength = 200;
    public const int MaxContentEncodingLength = 50;
    public const int MaxLeaseOwnerLength = 200;
    public const int MaxFailureCodeLength = 100;
    public const int MaxSafeDetailLength = 1024;

    private readonly List<IncomingWebhookProcessingAttempt> _processingAttempts = [];
    private readonly List<IncomingWebhookRedriveRecord> _redriveRecords = [];
    private byte[]? _payloadBytes;

    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; private set; }
    public Guid? WebhookConsumerProviderBindingId { get; private set; }
    public WebhookConsumerProviderBinding? WebhookConsumerProviderBinding { get; private set; }

    public string Provider { get; private set; } = string.Empty;
    public string ProviderMessageId { get; private set; } = string.Empty;
    public string? IdempotencyKey { get; private set; }
    public string? EventType { get; private set; }
    public string? HeadersJson { get; private set; }
    public ReadOnlyMemory<byte> PayloadBytes => _payloadBytes ?? ReadOnlyMemory<byte>.Empty;
    public string PayloadHash { get; private set; } = string.Empty;
    public long PayloadByteLength { get; private set; }
    public int PayloadProvenanceId { get; private set; }
    public WebhookPayloadProvenanceLookup PayloadProvenanceLookup { get; private set; } = null!;
    public string ContentType { get; private set; } = string.Empty;
    public string ContentEncoding { get; private set; } = string.Empty;
    public DateTime PayloadRetentionUntil { get; private set; }
    public DateTime? PayloadClearedAt { get; private set; }
    public string RetentionPolicyVersion { get; private set; } = string.Empty;
    public DateTime ProcessingAttemptRetentionUntil { get; private set; }
    public DateTime DeadLetterEvidenceRetentionUntil { get; private set; }
    public DateTime ReplayWindowUntil { get; private set; }
    public DateTime OperationalLogRetentionUntil { get; private set; }
    public int StatusId { get; private set; }
    public IncomingWebhookMessageStatusLookup StatusLookup { get; private set; } = null!;
    [NotMapped]
    public IncomingWebhookMessageStatus Status
    {
        get => (IncomingWebhookMessageStatus)StatusId;
        private set => StatusId = (int)value;
    }

    public int ProcessingGeneration { get; private set; }
    public long ProcessingFence { get; private set; }
    public int AttemptCount { get; private set; }
    public string? ProcessingLeaseOwner { get; private set; }
    public Guid? ProcessingLeaseToken { get; private set; }
    public DateTime? ProcessingLeaseExpiresAt { get; private set; }
    public DateTime? ProcessingStartedAt { get; private set; }
    public DateTime? NextAttemptAt { get; private set; }

    public DateTime ReceivedAt { get; private set; }
    public DateTime VerifiedAt { get; private set; }
    public DateTime? ProcessedAt { get; private set; }
    public DateTime? IgnoredAt { get; private set; }
    public DateTime? RejectedAt { get; private set; }
    public DateTime? DeadLetteredAt { get; private set; }
    public DateTime? PayloadConflictAt { get; private set; }
    public string? FailureCategory { get; private set; }
    public string? SafeDetail { get; private set; }

    public Guid? SettledByEffectReceiptId { get; private set; }
    public IncomingWebhookEffectReceipt? SettledByEffectReceipt { get; private set; }
    public string? SettledEffectKind { get; private set; }
    public int? SettlementSourceId { get; private set; }
    public IncomingWebhookSettlementSourceLookup? SettlementSourceLookup { get; private set; }
    [NotMapped]
    public IncomingWebhookSettlementSource SettlementSource
    {
        get => SettlementSourceId is null
            ? IncomingWebhookSettlementSource.None
            : (IncomingWebhookSettlementSource)SettlementSourceId.Value;
        private set => SettlementSourceId = value == IncomingWebhookSettlementSource.None
            ? null
            : (int)value;
    }

    public IReadOnlyCollection<IncomingWebhookProcessingAttempt> ProcessingAttempts => _processingAttempts;
    public IReadOnlyCollection<IncomingWebhookRedriveRecord> RedriveRecords => _redriveRecords;

    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public static IncomingWebhookMessage CreateVerified(
        Guid tenantId,
        string provider,
        string providerMessageId,
        string? idempotencyKey,
        string? eventType,
        ReadOnlySpan<byte> payloadBytes,
        string payloadHash,
        string contentType,
        string contentEncoding,
        string? headersJson,
        DateTime receivedAt,
        DateTime verifiedAt,
        DateTime payloadRetentionUntil,
        string retentionPolicyVersion,
        DateTime processingAttemptRetentionUntil,
        DateTime deadLetterEvidenceRetentionUntil,
        DateTime replayWindowUntil,
        DateTime operationalLogRetentionUntil,
        Guid? webhookConsumerProviderBindingId = null,
        WebhookPayloadProvenance payloadProvenance = WebhookPayloadProvenance.ExactBytes)
    {
        RequireGuid(tenantId, nameof(tenantId));
        if (payloadBytes.IsEmpty)
        {
            throw new ArgumentException("Incoming webhook payload is required.", nameof(payloadBytes));
        }

        if (receivedAt.Kind != DateTimeKind.Utc ||
            verifiedAt.Kind != DateTimeKind.Utc ||
            payloadRetentionUntil.Kind != DateTimeKind.Utc ||
            processingAttemptRetentionUntil.Kind != DateTimeKind.Utc ||
            deadLetterEvidenceRetentionUntil.Kind != DateTimeKind.Utc ||
            replayWindowUntil.Kind != DateTimeKind.Utc ||
            operationalLogRetentionUntil.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Webhook timestamps must use UTC kind.");
        }

        if (verifiedAt < receivedAt || payloadRetentionUntil <= verifiedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(payloadRetentionUntil));
        }

        if (processingAttemptRetentionUntil <= verifiedAt ||
            deadLetterEvidenceRetentionUntil < processingAttemptRetentionUntil ||
            replayWindowUntil <= verifiedAt ||
            payloadRetentionUntil < replayWindowUntil ||
            operationalLogRetentionUntil <= verifiedAt)
        {
            throw new ArgumentException("Incoming webhook evidence retention horizons are inconsistent.");
        }

        return new IncomingWebhookMessage
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            WebhookConsumerProviderBindingId = webhookConsumerProviderBindingId,
            Provider = NormalizeRequired(provider, MaxProviderLength, nameof(provider)).ToLowerInvariant(),
            ProviderMessageId = NormalizeRequired(providerMessageId, MaxProviderMessageIdLength, nameof(providerMessageId)),
            IdempotencyKey = NormalizeOptional(idempotencyKey, MaxIdempotencyKeyLength, nameof(idempotencyKey)),
            EventType = NormalizeOptional(eventType, MaxEventTypeLength, nameof(eventType)),
            HeadersJson = headersJson,
            _payloadBytes = payloadBytes.ToArray(),
            PayloadHash = NormalizePayloadHash(payloadHash),
            PayloadByteLength = payloadBytes.Length,
            PayloadProvenanceId = (int)payloadProvenance,
            ContentType = NormalizeRequired(contentType, MaxContentTypeLength, nameof(contentType)).ToLowerInvariant(),
            ContentEncoding = NormalizeRequired(contentEncoding, MaxContentEncodingLength, nameof(contentEncoding)).ToLowerInvariant(),
            PayloadRetentionUntil = payloadRetentionUntil,
            RetentionPolicyVersion = NormalizeRequired(
                retentionPolicyVersion,
                WebhookDeliveryPlanSnapshot.MaxVersionLength,
                nameof(retentionPolicyVersion)),
            ProcessingAttemptRetentionUntil = processingAttemptRetentionUntil,
            DeadLetterEvidenceRetentionUntil = deadLetterEvidenceRetentionUntil,
            ReplayWindowUntil = replayWindowUntil,
            OperationalLogRetentionUntil = operationalLogRetentionUntil,
            Status = IncomingWebhookMessageStatus.Verified,
            ProcessingGeneration = 1,
            ReceivedAt = receivedAt,
            VerifiedAt = verifiedAt,
            CreatedAt = verifiedAt
        };
    }

    public void ClearPayload(DateTime clearedAt)
    {
        if (clearedAt.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Timestamp must use UTC kind.", nameof(clearedAt));
        }

        if (clearedAt < PayloadRetentionUntil)
        {
            throw new InvalidOperationException("Incoming payload cannot be cleared before retention expires.");
        }

        if (_payloadBytes is null)
        {
            return;
        }

        _payloadBytes = null;
        PayloadClearedAt = clearedAt;
        UpdatedAt = clearedAt;
    }

    public IncomingWebhookDuplicateClassification ClassifyDuplicate(string payloadHash, DateTime classifiedAt)
    {
        var normalizedHash = NormalizePayloadHash(payloadHash);
        if (string.Equals(PayloadHash, normalizedHash, StringComparison.Ordinal))
        {
            return IncomingWebhookDuplicateClassification.Duplicate;
        }

        if (Status == IncomingWebhookMessageStatus.PayloadConflict)
        {
            return IncomingWebhookDuplicateClassification.PayloadConflict;
        }

        Status = IncomingWebhookMessageStatus.PayloadConflict;
        PayloadConflictAt = classifiedAt;
        FailureCategory = "payload_hash_conflict";
        SafeDetail = "The provider identity was reused with a different payload hash.";
        ClearLease();
        UpdatedAt = classifiedAt;
        AppendAttempt(IncomingWebhookProcessingAttemptOutcome.PayloadConflict, classifiedAt, FailureCategory, SafeDetail);
        return IncomingWebhookDuplicateClassification.PayloadConflict;
    }

    public void Claim(
        string leaseOwner,
        Guid leaseToken,
        DateTime leaseExpiresAt,
        DateTime claimedAt)
    {
        if (Status is not (IncomingWebhookMessageStatus.Verified or IncomingWebhookMessageStatus.RetryDue))
        {
            throw new InvalidOperationException($"Incoming webhook in state '{Status}' cannot be claimed.");
        }

        if (NextAttemptAt is not null && NextAttemptAt > claimedAt)
        {
            throw new InvalidOperationException("Incoming webhook is not due for processing.");
        }

        RequireGuid(leaseToken, nameof(leaseToken));
        if (leaseExpiresAt <= claimedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseExpiresAt), "Processing lease must expire after the claim time.");
        }

        ProcessingLeaseOwner = NormalizeRequired(leaseOwner, MaxLeaseOwnerLength, nameof(leaseOwner));
        ProcessingLeaseToken = leaseToken;
        ProcessingLeaseExpiresAt = leaseExpiresAt;
        ProcessingStartedAt = claimedAt;
        NextAttemptAt = null;
        ProcessingFence = checked(ProcessingFence + 1);
        AttemptCount = checked(AttemptCount + 1);
        Status = IncomingWebhookMessageStatus.Processing;
        UpdatedAt = claimedAt;
        AppendAttempt(IncomingWebhookProcessingAttemptOutcome.Claimed, claimedAt);
    }

    public void RenewLease(
        Guid leaseToken,
        long processingFence,
        int processingGeneration,
        DateTime leaseExpiresAt,
        DateTime renewedAt)
    {
        EnsureActiveClaim(leaseToken, processingFence, processingGeneration, renewedAt);
        if (leaseExpiresAt <= renewedAt || leaseExpiresAt <= ProcessingLeaseExpiresAt)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseExpiresAt), "A renewed lease must extend the active lease.");
        }

        ProcessingLeaseExpiresAt = leaseExpiresAt;
        UpdatedAt = renewedAt;
    }

    public void RecoverExpiredClaim(DateTime recoveredAt)
    {
        if (Status != IncomingWebhookMessageStatus.Processing ||
            ProcessingLeaseExpiresAt is null ||
            ProcessingLeaseExpiresAt > recoveredAt)
        {
            throw new InvalidOperationException("Only an expired incoming webhook processing claim can be recovered.");
        }

        Status = IncomingWebhookMessageStatus.RetryDue;
        NextAttemptAt = recoveredAt;
        FailureCategory = "processing_lease_expired";
        SafeDetail = "The previous processing lease expired before settlement.";
        AppendAttempt(
            IncomingWebhookProcessingAttemptOutcome.LeaseExpired,
            recoveredAt,
            FailureCategory,
            SafeDetail);
        ClearLease();
        UpdatedAt = recoveredAt;
    }

    public void SettleProcessed(
        IncomingWebhookEffectReceipt receipt,
        string expectedEffectKind,
        IncomingWebhookSettlementSource settlementSource,
        Guid leaseToken,
        long processingFence,
        int processingGeneration,
        DateTime settledAt)
    {
        EnsureActiveClaim(leaseToken, processingFence, processingGeneration, settledAt);
        if (settlementSource is not (IncomingWebhookSettlementSource.EffectCommitted or IncomingWebhookSettlementSource.ExistingReceipt))
        {
            throw new ArgumentOutOfRangeException(nameof(settlementSource));
        }

        receipt.EnsureMatches(TenantId, Id, expectedEffectKind, PayloadHash, ProcessingGeneration);

        Status = IncomingWebhookMessageStatus.Processed;
        ProcessedAt = settledAt;
        SettledByEffectReceiptId = receipt.Id;
        SettledEffectKind = IncomingWebhookEffectReceipt.NormalizeEffectKind(expectedEffectKind);
        SettlementSource = settlementSource;
        FailureCategory = null;
        SafeDetail = null;
        AppendAttempt(
            settlementSource == IncomingWebhookSettlementSource.ExistingReceipt
                ? IncomingWebhookProcessingAttemptOutcome.SettledFromReceipt
                : IncomingWebhookProcessingAttemptOutcome.Processed,
            settledAt);
        ClearLease();
        UpdatedAt = settledAt;
    }

    public void SettlePointerPersisted(
        string effectKind,
        Guid leaseToken,
        long processingFence,
        int processingGeneration,
        DateTime settledAt)
    {
        EnsureActiveClaim(leaseToken, processingFence, processingGeneration, settledAt);
        Status = IncomingWebhookMessageStatus.Processed;
        ProcessedAt = settledAt;
        SettledByEffectReceiptId = null;
        SettledEffectKind = IncomingWebhookEffectReceipt.NormalizeEffectKind(effectKind);
        SettlementSource = IncomingWebhookSettlementSource.None;
        FailureCategory = null;
        SafeDetail = null;
        AppendAttempt(IncomingWebhookProcessingAttemptOutcome.Processed, settledAt);
        ClearLease();
        UpdatedAt = settledAt;
    }

    public void RecordConcurrentReceiptRecovery(
        IncomingWebhookEffectReceipt receipt,
        string expectedEffectKind,
        int processingGeneration,
        DateTime observedAt)
    {
        if (observedAt.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Timestamp must use UTC kind.", nameof(observedAt));
        }

        if (Status != IncomingWebhookMessageStatus.Processed ||
            ProcessingGeneration != processingGeneration ||
            SettledByEffectReceiptId != receipt.Id)
        {
            throw new InvalidOperationException("The concurrent effect receipt did not settle this processing generation.");
        }

        receipt.EnsureMatches(TenantId, Id, expectedEffectKind, PayloadHash, ProcessingGeneration);
        AppendAttempt(IncomingWebhookProcessingAttemptOutcome.SettledFromReceipt, observedAt);
        UpdatedAt = observedAt;
    }

    public void Ignore(
        Guid leaseToken,
        long processingFence,
        int processingGeneration,
        string reasonCode,
        string? safeDetail,
        DateTime ignoredAt)
    {
        EnsureActiveClaim(leaseToken, processingFence, processingGeneration, ignoredAt);
        Status = IncomingWebhookMessageStatus.Ignored;
        IgnoredAt = ignoredAt;
        FailureCategory = NormalizeRequired(reasonCode, MaxFailureCodeLength, nameof(reasonCode));
        SafeDetail = BoundSafeDetail(safeDetail);
        AppendAttempt(IncomingWebhookProcessingAttemptOutcome.Ignored, ignoredAt, FailureCategory, SafeDetail);
        ClearLease();
        UpdatedAt = ignoredAt;
    }

    public void RejectPermanent(
        Guid leaseToken,
        long processingFence,
        int processingGeneration,
        string failureCategory,
        string? safeDetail,
        DateTime rejectedAt)
    {
        EnsureActiveClaim(leaseToken, processingFence, processingGeneration, rejectedAt);
        Status = IncomingWebhookMessageStatus.RejectedPermanent;
        RejectedAt = rejectedAt;
        FailureCategory = NormalizeRequired(failureCategory, MaxFailureCodeLength, nameof(failureCategory));
        SafeDetail = BoundSafeDetail(safeDetail);
        AppendAttempt(IncomingWebhookProcessingAttemptOutcome.RejectedPermanent, rejectedAt, FailureCategory, SafeDetail);
        ClearLease();
        UpdatedAt = rejectedAt;
    }

    public void ScheduleRetry(
        Guid leaseToken,
        long processingFence,
        int processingGeneration,
        string failureCategory,
        string? safeDetail,
        DateTime nextAttemptAt,
        DateTime failedAt)
    {
        EnsureActiveClaim(leaseToken, processingFence, processingGeneration, failedAt);
        if (nextAttemptAt <= failedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(nextAttemptAt), "Retry must be scheduled in the future.");
        }

        Status = IncomingWebhookMessageStatus.RetryDue;
        NextAttemptAt = nextAttemptAt;
        FailureCategory = NormalizeRequired(failureCategory, MaxFailureCodeLength, nameof(failureCategory));
        SafeDetail = BoundSafeDetail(safeDetail);
        AppendAttempt(IncomingWebhookProcessingAttemptOutcome.RetryScheduled, failedAt, FailureCategory, SafeDetail);
        ClearLease();
        UpdatedAt = failedAt;
    }

    public void DeadLetter(
        Guid leaseToken,
        long processingFence,
        int processingGeneration,
        string failureCategory,
        string? safeDetail,
        DateTime deadLetteredAt)
    {
        EnsureActiveClaim(leaseToken, processingFence, processingGeneration, deadLetteredAt);
        Status = IncomingWebhookMessageStatus.DeadLettered;
        DeadLetteredAt = deadLetteredAt;
        FailureCategory = NormalizeRequired(failureCategory, MaxFailureCodeLength, nameof(failureCategory));
        SafeDetail = BoundSafeDetail(safeDetail);
        AppendAttempt(IncomingWebhookProcessingAttemptOutcome.DeadLettered, deadLetteredAt, FailureCategory, SafeDetail);
        ClearLease();
        UpdatedAt = deadLetteredAt;
    }

    public IncomingWebhookRedriveRecord Redrive(
        int expectedProcessingGeneration,
        string actorId,
        string reason,
        DateTime requestedAt)
    {
        if (requestedAt.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Timestamp must use UTC kind.", nameof(requestedAt));
        }

        if (Status != IncomingWebhookMessageStatus.DeadLettered)
        {
            throw new InvalidOperationException("Only dead-lettered incoming webhooks can be redriven.");
        }

        var sourceGeneration = ProcessingGeneration;
        if (sourceGeneration != expectedProcessingGeneration)
        {
            throw new InvalidOperationException("The incoming webhook processing generation changed before redrive.");
        }

        ProcessingGeneration = checked(ProcessingGeneration + 1);
        Status = IncomingWebhookMessageStatus.RetryDue;
        NextAttemptAt = requestedAt;
        DeadLetteredAt = null;
        FailureCategory = null;
        SafeDetail = null;
        ClearLease();
        var record = IncomingWebhookRedriveRecord.Create(
            TenantId,
            Id,
            actorId,
            reason,
            requestedAt,
            sourceGeneration,
            ProcessingGeneration,
            IncomingWebhookRedriveResult.Scheduled);
        _redriveRecords.Add(record);
        UpdatedAt = requestedAt;
        return record;
    }

    public void EnsureActiveClaim(
        Guid leaseToken,
        long processingFence,
        int processingGeneration,
        DateTime observedAt)
    {
        if (Status != IncomingWebhookMessageStatus.Processing ||
            ProcessingLeaseToken != leaseToken ||
            ProcessingFence != processingFence ||
            ProcessingGeneration != processingGeneration ||
            ProcessingLeaseExpiresAt is null ||
            ProcessingLeaseExpiresAt <= observedAt)
        {
            throw new InvalidOperationException("The incoming webhook processing claim is stale or no longer active.");
        }
    }

    private void AppendAttempt(
        IncomingWebhookProcessingAttemptOutcome outcome,
        DateTime recordedAt,
        string? failureCategory = null,
        string? safeDetail = null)
    {
        _processingAttempts.Add(IncomingWebhookProcessingAttempt.Create(
            TenantId,
            Id,
            ProcessingGeneration,
            ProcessingFence,
            AttemptCount,
            outcome,
            ProcessingStartedAt ?? recordedAt,
            recordedAt,
            failureCategory,
            safeDetail));
    }

    private void ClearLease()
    {
        ProcessingLeaseOwner = null;
        ProcessingLeaseToken = null;
        ProcessingLeaseExpiresAt = null;
        ProcessingStartedAt = null;
    }

    internal static string NormalizePayloadHash(string payloadHash)
    {
        var normalized = NormalizeRequired(payloadHash, 71, nameof(payloadHash)).ToLowerInvariant();
        if (!normalized.StartsWith("sha256:", StringComparison.Ordinal) ||
            normalized.Length != 71 ||
            !ContainsOnlyLowerHex(normalized.AsSpan(7)))
        {
            throw new ArgumentException("Payload hash must be a lowercase SHA-256 identifier.", nameof(payloadHash));
        }

        return normalized;
    }

    private static bool ContainsOnlyLowerHex(ReadOnlySpan<char> value)
    {
        foreach (var character in value)
        {
            if (character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))
            {
                return false;
            }
        }

        return true;
    }

    internal static string NormalizeRequired(string value, int maxLength, string parameterName)
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

    internal static string? NormalizeOptional(string? value, int maxLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return NormalizeRequired(value, maxLength, parameterName);
    }

    internal static string? BoundSafeDetail(string? safeDetail)
    {
        if (string.IsNullOrWhiteSpace(safeDetail))
        {
            return null;
        }

        var normalized = safeDetail.Trim();
        return normalized.Length <= MaxSafeDetailLength
            ? normalized
            : normalized[..MaxSafeDetailLength];
    }

    private static void RequireGuid(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Identifier is required.", parameterName);
        }
    }
}

public enum IncomingWebhookDuplicateClassification
{
    Duplicate = 1,
    PayloadConflict = 2
}
