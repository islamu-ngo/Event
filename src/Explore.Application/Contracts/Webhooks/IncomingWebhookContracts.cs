namespace Explore.Application.Contracts.Webhooks;

public sealed record IncomingWebhookContext(
    string Provider,
    string RawPayload,
    ReadOnlyMemory<byte> RawPayloadBytes,
    IReadOnlyDictionary<string, string> Headers,
    DateTimeOffset ReceivedAt);

public sealed record IncomingWebhookVerificationResult(
    bool IsVerified,
    Guid? TenantId,
    Guid? WebhookConsumerProviderBindingId,
    string? ProviderMessageId,
    string? EventType,
    string? IdempotencyKey,
    string? FailureCategory,
    string? SafeDetail,
    string? Receipt = null,
    ReadOnlyMemory<byte> RetainedPayloadBytes = default)
{
    public static IncomingWebhookVerificationResult VerifiedProviderBinding(
        Guid tenantId,
        Guid webhookConsumerProviderBindingId,
        string providerMessageId,
        string? eventType,
        string idempotencyKey,
        ReadOnlyMemory<byte> retainedPayloadBytes = default)
    {
        RequireGuid(tenantId, nameof(tenantId));
        RequireGuid(webhookConsumerProviderBindingId, nameof(webhookConsumerProviderBindingId));
        return new(true, tenantId, webhookConsumerProviderBindingId, providerMessageId, eventType, idempotencyKey, null, null, null, retainedPayloadBytes);
    }

    public static IncomingWebhookVerificationResult VerifiedTenantCredential(
        Guid tenantId,
        string providerMessageId,
        string? eventType,
        string idempotencyKey,
        ReadOnlyMemory<byte> retainedPayloadBytes = default)
    {
        RequireGuid(tenantId, nameof(tenantId));
        return new(true, tenantId, null, providerMessageId, eventType, idempotencyKey, null, null, null, retainedPayloadBytes);
    }

    public static IncomingWebhookVerificationResult Rejected(
        string failureCategory,
        string? safeDetail = null) =>
        new(false, null, null, null, null, null, failureCategory, safeDetail);

    private static void RequireGuid(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Identifier is required.", parameterName);
        }
    }
}

public sealed record IncomingWebhookProcessingResult(
    IncomingWebhookProcessingOutcome Outcome,
    string? FailureCategory = null,
    string? SafeDetail = null,
    string? SafeResultReference = null)
{
    public static IncomingWebhookProcessingResult Processed(string? safeResultReference = null) =>
        new(IncomingWebhookProcessingOutcome.Processed, SafeResultReference: safeResultReference);

    public static IncomingWebhookProcessingResult PointerPersisted(string safeResultReference) =>
        new(IncomingWebhookProcessingOutcome.PointerPersisted, SafeResultReference: safeResultReference);

    public static IncomingWebhookProcessingResult Ignored(string reasonCode, string? safeDetail = null) =>
        new(IncomingWebhookProcessingOutcome.Ignored, reasonCode, safeDetail);

    public static IncomingWebhookProcessingResult RejectedPermanent(string failureCategory, string? safeDetail = null) =>
        new(IncomingWebhookProcessingOutcome.RejectedPermanent, failureCategory, safeDetail);

    public static IncomingWebhookProcessingResult RetryDue(string failureCategory, string? safeDetail = null) =>
        new(IncomingWebhookProcessingOutcome.RetryDue, failureCategory, safeDetail);

    public static IncomingWebhookProcessingResult DeadLettered(string failureCategory, string? safeDetail = null) =>
        new(IncomingWebhookProcessingOutcome.DeadLettered, failureCategory, safeDetail);
}

public enum IncomingWebhookProcessingOutcome
{
    Processed = 1,
    Ignored = 2,
    RejectedPermanent = 3,
    RetryDue = 4,
    DeadLettered = 5,
    PointerPersisted = 6
}

public interface IIncomingWebhookVerifier
{
    string Provider { get; }

    Task<IncomingWebhookVerificationResult> VerifyAsync(
        IncomingWebhookContext context,
        CancellationToken cancellationToken);
}

public interface IIncomingWebhookHandler
{
    string EffectKind { get; }

    bool CanHandle(string provider, string? eventType);

    Task<IncomingWebhookProcessingResult> HandleAsync(
        IncomingWebhookProcessingContext context,
        CancellationToken cancellationToken);
}
