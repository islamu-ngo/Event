using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Webhooks;
using Explore.Domain;

namespace Explore.Application.Services.Webhooks;

public sealed class CoopDecisionIncomingWebhookHandler(
    IIncomingWebhookEffectOutboxRepository pointerRepository) : IIncomingWebhookHandler
{
    public const string StableEffectKind = "moderation.coop.decision";

    public string EffectKind => StableEffectKind;

    public bool CanHandle(string provider, string? eventType) =>
        string.Equals(provider, "coop", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(eventType, StableEffectKind, StringComparison.Ordinal);

    public async Task<IncomingWebhookProcessingResult> HandleAsync(
        IncomingWebhookProcessingContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.ProviderMessageId))
        {
            return IncomingWebhookProcessingResult.RejectedPermanent(
                "coop_provider_decision_id_missing",
                "A signed Coop callback requires a provider decision identifier.");
        }

        if (context.ProviderMessageId.Length > IncomingWebhookEffectOutbox.MaxProviderDecisionIdLength)
        {
            return IncomingWebhookProcessingResult.RejectedPermanent(
                "coop_provider_decision_id_invalid",
                "The provider decision identifier exceeds the allowed size.");
        }

        var existing = await pointerRepository.GetByProviderIdentityAsync(
            context.TenantId,
            context.Provider,
            context.ProviderMessageId,
            StableEffectKind,
            cancellationToken);
        if (existing is not null)
        {
            if (!string.Equals(existing.PayloadSha256, context.PayloadHash, StringComparison.Ordinal))
            {
                return IncomingWebhookProcessingResult.RejectedPermanent(
                    "coop_provider_decision_payload_conflict",
                    "The provider decision identifier was reused with different callback content.");
            }

            return IncomingWebhookProcessingResult.PointerPersisted(
                "incoming-webhook-effect:" + existing.Id.ToString("N"));
        }

        var pointer = IncomingWebhookEffectOutbox.CreatePending(
            context.TenantId,
            context.IncomingWebhookMessageId,
            context.Provider,
            context.ProviderMessageId,
            StableEffectKind,
            context.PayloadHash,
            context.ReceivedAt);
        await pointerRepository.AddAsync(pointer, cancellationToken);

        return IncomingWebhookProcessingResult.PointerPersisted(
            "incoming-webhook-effect:" + pointer.Id.ToString("N"));
    }
}
