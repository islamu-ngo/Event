using Explore.Domain;

namespace Explore.Application.Contracts.Webhooks;

public sealed class IncomingWebhookProcessingContext
{
    private readonly byte[] _payloadBytes;

    private IncomingWebhookProcessingContext(IncomingWebhookMessage claimedMessage)
    {
        IncomingWebhookMessageId = claimedMessage.Id;
        TenantId = claimedMessage.TenantId;
        Provider = claimedMessage.Provider;
        ProviderMessageId = claimedMessage.ProviderMessageId;
        EventType = claimedMessage.EventType;
        PayloadHash = claimedMessage.PayloadHash;
        ProcessingGeneration = claimedMessage.ProcessingGeneration;
        ProcessingFence = claimedMessage.ProcessingFence;
        ProcessingLeaseOwner = claimedMessage.ProcessingLeaseOwner!;
        ProcessingLeaseToken = claimedMessage.ProcessingLeaseToken!.Value;
        ReceivedAt = claimedMessage.ReceivedAt;
        _payloadBytes = claimedMessage.PayloadBytes.ToArray();
    }

    public Guid IncomingWebhookMessageId { get; }
    public Guid TenantId { get; }
    public string Provider { get; }
    public string ProviderMessageId { get; }
    public string? EventType { get; }
    public string PayloadHash { get; }
    public int ProcessingGeneration { get; }
    public long ProcessingFence { get; }
    public string ProcessingLeaseOwner { get; }
    public Guid ProcessingLeaseToken { get; }
    public DateTime ReceivedAt { get; }
    public ReadOnlyMemory<byte> PayloadBytes => _payloadBytes;

    public static IncomingWebhookProcessingContext FromClaimedMessage(
        IncomingWebhookMessage claimedMessage,
        Guid expectedLeaseToken,
        long expectedProcessingFence,
        int expectedProcessingGeneration,
        DateTime observedAt)
    {
        ArgumentNullException.ThrowIfNull(claimedMessage);
        claimedMessage.EnsureActiveClaim(
            expectedLeaseToken,
            expectedProcessingFence,
            expectedProcessingGeneration,
            observedAt);

        return new IncomingWebhookProcessingContext(claimedMessage);
    }

    public void EnsureMatches(IncomingWebhookMessage claimedMessage, DateTime observedAt)
    {
        ArgumentNullException.ThrowIfNull(claimedMessage);
        claimedMessage.EnsureActiveClaim(
            ProcessingLeaseToken,
            ProcessingFence,
            ProcessingGeneration,
            observedAt);

        if (claimedMessage.Id != IncomingWebhookMessageId ||
            claimedMessage.TenantId != TenantId ||
            !string.Equals(claimedMessage.Provider, Provider, StringComparison.Ordinal) ||
            !string.Equals(claimedMessage.ProviderMessageId, ProviderMessageId, StringComparison.Ordinal) ||
            !string.Equals(claimedMessage.PayloadHash, PayloadHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The processing context does not match the claimed incoming webhook identity.");
        }
    }
}
