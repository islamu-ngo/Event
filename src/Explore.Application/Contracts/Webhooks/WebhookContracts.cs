namespace Explore.Application.Contracts.Webhooks;

public sealed record WebhookEventEnvelope(
    Guid Id,
    string Type,
    int Version,
    DateTimeOffset OccurredAt,
    Guid TenantId,
    IReadOnlyDictionary<string, object?> Data);

public sealed record WebhookEventBuildContext(
    Guid MessageId,
    Guid TenantId,
    string EventType,
    string EventId,
    string AggregateKind,
    Guid AggregateId,
    DateTimeOffset OccurredAt,
    IReadOnlyDictionary<string, object?> Data,
    Guid? ConsumerId = null);

public sealed record WebhookPayloadBuildResult
{
    public WebhookPayloadBuildResult(
        bool Succeeded,
        WebhookEventEnvelope? Envelope,
        ReadOnlyMemory<byte>? PayloadBytes,
        string? PayloadHash,
        DateTimeOffset? PayloadRetentionUntil,
        string? FailureCategory,
        string? SafeDetail)
    {
        this.Succeeded = Succeeded;
        this.Envelope = Envelope;
        this.PayloadBytes = PayloadBytes.HasValue
            ? new ReadOnlyMemory<byte>(PayloadBytes.Value.ToArray())
            : (ReadOnlyMemory<byte>?)null;
        this.PayloadHash = PayloadHash;
        this.PayloadRetentionUntil = PayloadRetentionUntil;
        this.FailureCategory = FailureCategory;
        this.SafeDetail = SafeDetail;
    }

    public bool Succeeded { get; }
    public WebhookEventEnvelope? Envelope { get; }
    public ReadOnlyMemory<byte>? PayloadBytes { get; }
    public string? PayloadHash { get; }
    public DateTimeOffset? PayloadRetentionUntil { get; }
    public string? FailureCategory { get; }
    public string? SafeDetail { get; }

    public static WebhookPayloadBuildResult Success(
        WebhookEventEnvelope envelope,
        byte[] payloadBytes,
        string payloadHash,
        DateTimeOffset payloadRetentionUntil) =>
        new(true, envelope, payloadBytes, payloadHash, payloadRetentionUntil, null, null);

    public static WebhookPayloadBuildResult Failure(string failureCategory, string? safeDetail = null) =>
        new(false, null, null, null, null, failureCategory, safeDetail);
}

public sealed record WebhookEventPublishResult(
    bool Succeeded,
    bool Skipped,
    Guid? MessageId,
    string? ProviderMessageId,
    bool IsRetryable,
    string? FailureCategory,
    string? SafeDetail)
{
    public static WebhookEventPublishResult Success(Guid messageId, string? providerMessageId = null) =>
        new(true, false, messageId, providerMessageId, false, null, null);

    public static WebhookEventPublishResult SkippedResult(string failureCategory, string? safeDetail = null) =>
        new(true, true, null, null, false, failureCategory, safeDetail);

    public static WebhookEventPublishResult Failure(
        string failureCategory,
        bool isRetryable,
        string? safeDetail = null) =>
        new(false, false, null, null, isRetryable, failureCategory, safeDetail);
}

public sealed record CreateWebhookEndpointInput(
    Guid TenantId,
    Guid ConsumerId,
    Uri Url,
    string? Description,
    IReadOnlyCollection<string> EventTypes,
    int? MaxAttempts = null,
    int? TimeoutSeconds = null,
    int? RateLimitPerMinute = null);

public sealed record UpdateWebhookEndpointInput(
    Guid EndpointId,
    Guid TenantId,
    Uri Url,
    string? Description,
    IReadOnlyCollection<string> EventTypes,
    int? MaxAttempts = null,
    int? TimeoutSeconds = null,
    int? RateLimitPerMinute = null);

public sealed record WebhookEndpointResult(
    Guid EndpointId,
    Guid TenantId,
    Guid ConsumerId,
    Uri Url,
    string Status,
    int SecretVersion,
    IReadOnlyCollection<string> EventTypes);
