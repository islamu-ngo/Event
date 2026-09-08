using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class IncomingWebhookEffectReceipt : ITenantEntity, IAuditableEntity
{
    public const int MaxEffectKindLength = 200;
    public const int MaxSafeResultReferenceLength = 512;

    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; private set; }
    public Guid IncomingWebhookMessageId { get; private set; }
    public IncomingWebhookMessage? IncomingWebhookMessage { get; private set; }
    public string EffectKind { get; private set; } = string.Empty;
    public string PayloadHash { get; private set; } = string.Empty;
    public int ProcessingGeneration { get; private set; }
    public DateTime AppliedAt { get; private set; }
    public string? SafeResultReference { get; private set; }

    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public static IncomingWebhookEffectReceipt Create(
        Guid tenantId,
        Guid incomingWebhookMessageId,
        string effectKind,
        string payloadHash,
        int processingGeneration,
        DateTime appliedAt,
        string? safeResultReference = null)
    {
        RequireGuid(tenantId, nameof(tenantId));
        RequireGuid(incomingWebhookMessageId, nameof(incomingWebhookMessageId));
        ArgumentOutOfRangeException.ThrowIfLessThan(processingGeneration, 1);

        return new IncomingWebhookEffectReceipt
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            IncomingWebhookMessageId = incomingWebhookMessageId,
            EffectKind = NormalizeEffectKind(effectKind),
            PayloadHash = IncomingWebhookMessage.NormalizePayloadHash(payloadHash),
            ProcessingGeneration = processingGeneration,
            AppliedAt = appliedAt,
            SafeResultReference = IncomingWebhookMessage.NormalizeOptional(
                safeResultReference,
                MaxSafeResultReferenceLength,
                nameof(safeResultReference)),
            CreatedAt = appliedAt
        };
    }

    public void EnsureMatches(
        Guid tenantId,
        Guid incomingWebhookMessageId,
        string effectKind,
        string payloadHash,
        int currentProcessingGeneration)
    {
        if (TenantId != tenantId ||
            IncomingWebhookMessageId != incomingWebhookMessageId ||
            !string.Equals(EffectKind, NormalizeEffectKind(effectKind), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The effect receipt belongs to a different incoming webhook identity.");
        }

        if (!string.Equals(PayloadHash, IncomingWebhookMessage.NormalizePayloadHash(payloadHash), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The effect receipt payload hash conflicts with the incoming webhook payload.");
        }

        if (ProcessingGeneration > currentProcessingGeneration)
        {
            throw new InvalidOperationException("The effect receipt belongs to a future processing generation.");
        }
    }

    public static string NormalizeEffectKind(string effectKind) =>
        IncomingWebhookMessage.NormalizeRequired(effectKind, MaxEffectKindLength, nameof(effectKind)).ToLowerInvariant();

    private static void RequireGuid(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Identifier is required.", parameterName);
        }
    }
}
