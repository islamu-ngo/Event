using System.ComponentModel.DataAnnotations.Schema;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public sealed record WebhookBulkReplayPreviewSnapshot(
    int EligibleCount,
    int HeldCount,
    int PayloadUnavailableCount,
    int EndpointUnavailableCount,
    int IneligibleLocalStateCount,
    int ProviderConflictCount,
    int ProviderUnknownCount,
    int ProviderManualReconciliationCount,
    int ProviderIneligibleCount)
{
    public int TotalExcludedCount =>
        HeldCount +
        PayloadUnavailableCount +
        EndpointUnavailableCount +
        IneligibleLocalStateCount +
        ProviderConflictCount +
        ProviderUnknownCount +
        ProviderManualReconciliationCount +
        ProviderIneligibleCount;
}

public sealed class WebhookBulkReplayOperation : ITenantEntity, IAuditableEntity
{
    public const int MaxReasonCodeLength = 200;
    public const int MaxFailureCodeLength = 200;
    public const int RequestHashLength = 71;
    public const int HardMaximumItems = 1_000;

    private WebhookBulkReplayOperation()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; private set; }
    public Guid OperationKey { get; private set; }
    public string RequestHash { get; private set; } = string.Empty;
    public int StatusId { get; private set; }
    public WebhookBulkReplayStatusLookup StatusLookup { get; private set; } = null!;
    public DateTime FromUtc { get; private set; }
    public DateTime ToUtc { get; private set; }
    public Guid? WebhookConsumerId { get; private set; }
    public WebhookConsumer? WebhookConsumer { get; private set; }
    public Guid? WebhookEndpointId { get; private set; }
    public WebhookEndpoint? WebhookEndpoint { get; private set; }
    public string? EventType { get; private set; }
    public int RequestedMaxItems { get; private set; }
    public string ReasonCode { get; private set; } = string.Empty;
    public string? CancellationReasonCode { get; private set; }
    public int EstimatedEligibleCount { get; private set; }
    public int EstimatedSelectedCount { get; private set; }
    public int ExcludedHeldCount { get; private set; }
    public int ExcludedPayloadUnavailableCount { get; private set; }
    public int ExcludedEndpointUnavailableCount { get; private set; }
    public int ExcludedIneligibleLocalStateCount { get; private set; }
    public int ExcludedProviderConflictCount { get; private set; }
    public int ExcludedProviderUnknownCount { get; private set; }
    public int ExcludedProviderManualReconciliationCount { get; private set; }
    public int ExcludedProviderIneligibleCount { get; private set; }
    public int ScheduledCount { get; private set; }
    public string? FailureCode { get; private set; }
    public long ConcurrencyVersion { get; private set; }
    public DateTime QueuedAt { get; private set; }
    public DateTime? StartedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public DateTime? CancelledAt { get; private set; }
    public DateTime? FailedAt { get; private set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    [NotMapped]
    public WebhookBulkReplayStatus Status => (WebhookBulkReplayStatus)StatusId;

    [NotMapped]
    public int EstimatedExcludedCount =>
        ExcludedHeldCount +
        ExcludedPayloadUnavailableCount +
        ExcludedEndpointUnavailableCount +
        ExcludedIneligibleLocalStateCount +
        ExcludedProviderConflictCount +
        ExcludedProviderUnknownCount +
        ExcludedProviderManualReconciliationCount +
        ExcludedProviderIneligibleCount;

    public static WebhookBulkReplayOperation Create(
        Guid tenantId,
        Guid operationKey,
        string requestHash,
        DateTime fromUtc,
        DateTime toUtc,
        Guid? webhookConsumerId,
        Guid? webhookEndpointId,
        string? eventType,
        int requestedMaxItems,
        string reasonCode,
        WebhookBulkReplayPreviewSnapshot preview,
        DateTime queuedAt)
    {
        RequireGuid(tenantId, nameof(tenantId));
        RequireGuid(operationKey, nameof(operationKey));
        if (webhookConsumerId == Guid.Empty)
        {
            throw new ArgumentException("Consumer id cannot be empty when supplied.", nameof(webhookConsumerId));
        }

        if (webhookEndpointId == Guid.Empty)
        {
            throw new ArgumentException("Endpoint id cannot be empty when supplied.", nameof(webhookEndpointId));
        }

        RequireUtc(fromUtc, nameof(fromUtc));
        RequireUtc(toUtc, nameof(toUtc));
        RequireUtc(queuedAt, nameof(queuedAt));
        if (toUtc <= fromUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(toUtc), "Replay filter end must be after its start.");
        }

        if (requestedMaxItems is < 1 or > HardMaximumItems)
        {
            throw new ArgumentOutOfRangeException(nameof(requestedMaxItems));
        }

        ArgumentNullException.ThrowIfNull(preview);
        ValidatePreview(preview);
        return new WebhookBulkReplayOperation
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            OperationKey = operationKey,
            RequestHash = NormalizeRequestHash(requestHash),
            StatusId = (int)WebhookBulkReplayStatus.Queued,
            FromUtc = fromUtc,
            ToUtc = toUtc,
            WebhookConsumerId = webhookConsumerId,
            WebhookEndpointId = webhookEndpointId,
            EventType = NormalizeOptionalEventType(eventType),
            RequestedMaxItems = requestedMaxItems,
            ReasonCode = NormalizeReasonCode(reasonCode, nameof(reasonCode)),
            EstimatedEligibleCount = preview.EligibleCount,
            EstimatedSelectedCount = Math.Min(preview.EligibleCount, requestedMaxItems),
            ExcludedHeldCount = preview.HeldCount,
            ExcludedPayloadUnavailableCount = preview.PayloadUnavailableCount,
            ExcludedEndpointUnavailableCount = preview.EndpointUnavailableCount,
            ExcludedIneligibleLocalStateCount = preview.IneligibleLocalStateCount,
            ExcludedProviderConflictCount = preview.ProviderConflictCount,
            ExcludedProviderUnknownCount = preview.ProviderUnknownCount,
            ExcludedProviderManualReconciliationCount = preview.ProviderManualReconciliationCount,
            ExcludedProviderIneligibleCount = preview.ProviderIneligibleCount,
            ConcurrencyVersion = 1,
            QueuedAt = queuedAt,
            CreatedAt = queuedAt
        };
    }

    public void Start(DateTime startedAt)
    {
        RequireUtc(startedAt, nameof(startedAt));
        if (Status != WebhookBulkReplayStatus.Queued)
        {
            throw new InvalidOperationException("Only a queued bulk replay can start.");
        }

        StatusId = (int)WebhookBulkReplayStatus.Executing;
        StartedAt = startedAt;
        AdvanceVersion(startedAt);
    }

    public void Complete(int scheduledCount, DateTime completedAt)
    {
        RequireUtc(completedAt, nameof(completedAt));
        if (Status != WebhookBulkReplayStatus.Executing)
        {
            throw new InvalidOperationException("Only an executing bulk replay can complete.");
        }

        if (scheduledCount is < 0 || scheduledCount > RequestedMaxItems)
        {
            throw new ArgumentOutOfRangeException(nameof(scheduledCount));
        }

        StatusId = (int)WebhookBulkReplayStatus.Completed;
        ScheduledCount = scheduledCount;
        CompletedAt = completedAt;
        AdvanceVersion(completedAt);
    }

    public void Cancel(string reasonCode, DateTime cancelledAt)
    {
        RequireUtc(cancelledAt, nameof(cancelledAt));
        if (Status != WebhookBulkReplayStatus.Queued)
        {
            throw new InvalidOperationException("Only a queued bulk replay can be cancelled.");
        }

        StatusId = (int)WebhookBulkReplayStatus.Cancelled;
        CancellationReasonCode = NormalizeReasonCode(reasonCode, nameof(reasonCode));
        CancelledAt = cancelledAt;
        AdvanceVersion(cancelledAt);
    }

    public void Fail(string failureCode, DateTime failedAt)
    {
        RequireUtc(failedAt, nameof(failedAt));
        if (Status != WebhookBulkReplayStatus.Executing)
        {
            throw new InvalidOperationException("Only an executing bulk replay can fail.");
        }

        StatusId = (int)WebhookBulkReplayStatus.Failed;
        FailureCode = NormalizeCode(failureCode, MaxFailureCodeLength, nameof(failureCode));
        FailedAt = failedAt;
        AdvanceVersion(failedAt);
    }

    private void AdvanceVersion(DateTime changedAt)
    {
        ConcurrencyVersion = checked(ConcurrencyVersion + 1);
        UpdatedAt = changedAt;
    }

    private static void ValidatePreview(WebhookBulkReplayPreviewSnapshot preview)
    {
        int[] counts =
        [
            preview.EligibleCount,
            preview.HeldCount,
            preview.PayloadUnavailableCount,
            preview.EndpointUnavailableCount,
            preview.IneligibleLocalStateCount,
            preview.ProviderConflictCount,
            preview.ProviderUnknownCount,
            preview.ProviderManualReconciliationCount,
            preview.ProviderIneligibleCount
        ];
        if (counts.Any(count => count < 0))
        {
            throw new ArgumentOutOfRangeException(nameof(preview), "Preview counts cannot be negative.");
        }
    }

    private static string NormalizeRequestHash(string value)
    {
        var normalized = NormalizeCode(value, RequestHashLength, nameof(value)).ToLowerInvariant();
        if (normalized.Length != RequestHashLength ||
            !normalized.StartsWith("sha256:", StringComparison.Ordinal) ||
            normalized[7..].Any(character => !char.IsAsciiHexDigit(character)))
        {
            throw new ArgumentException("Request hash must be a lowercase SHA-256 identifier.", nameof(value));
        }

        return normalized;
    }

    private static string NormalizeReasonCode(string value, string parameterName) =>
        NormalizeCode(value, MaxReasonCodeLength, parameterName).ToLowerInvariant();

    private static string NormalizeCode(string value, int maximumLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var normalized = value.Trim();
        if (normalized.Length > maximumLength || normalized.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '_' and not '-' and not '.' and not ':'))
        {
            throw new ArgumentException(
                "Value must be a bounded identifier containing letters, digits, underscore, dash, dot, or colon.",
                parameterName);
        }

        return normalized;
    }

    private static string? NormalizeOptionalEventType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= WebhookMessage.MaxEventTypeLength
            ? normalized
            : throw new ArgumentOutOfRangeException(nameof(value));
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
