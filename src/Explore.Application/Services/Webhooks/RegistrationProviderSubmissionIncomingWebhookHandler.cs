using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.Features.RegistrationSubmissions.Commands;
using Explore.Domain;

namespace Explore.Application.Services.Webhooks;

public sealed class RegistrationProviderSubmissionIncomingWebhookHandler(
    IIncomingWebhookEffectOutboxRepository pointerRepository) : IIncomingWebhookHandler
{
    public const string ResponseSweepEffectKind = "registration.provider_response_sweep";

    public string EffectKind => ProcessProviderSubmissionEffectCommandHandler.StableEffectKind;

    public bool CanHandle(string provider, string? eventType) =>
        string.Equals(provider, "registration-provider", StringComparison.Ordinal) &&
        (string.Equals(eventType, EffectKind, StringComparison.Ordinal) ||
            string.Equals(eventType, ResponseSweepEffectKind, StringComparison.Ordinal));

    public async Task<IncomingWebhookProcessingResult> HandleAsync(
        IncomingWebhookProcessingContext context,
        CancellationToken cancellationToken)
    {
        string effectKind = string.IsNullOrWhiteSpace(context.EventType) ? EffectKind : context.EventType;
        IncomingWebhookEffectOutbox? existing = await pointerRepository.GetByProviderIdentityAsync(
            context.TenantId,
            context.Provider,
            context.ProviderMessageId,
            effectKind,
            cancellationToken);
        if (existing is not null)
        {
            return string.Equals(existing.PayloadSha256, context.PayloadHash, StringComparison.Ordinal)
                ? IncomingWebhookProcessingResult.PointerPersisted("incoming-webhook-effect:" + existing.Id.ToString("N"))
                : IncomingWebhookProcessingResult.RejectedPermanent(
                    "registration_provider_submission_payload_conflict",
                    "The provider submission identity was reused with different callback content.");
        }

        IncomingWebhookEffectOutbox pointer = IncomingWebhookEffectOutbox.CreatePending(
            context.TenantId,
            context.IncomingWebhookMessageId,
            context.Provider,
            context.ProviderMessageId,
            effectKind,
            context.PayloadHash,
            context.ReceivedAt);
        await pointerRepository.AddAsync(pointer, cancellationToken);
        return IncomingWebhookProcessingResult.PointerPersisted("incoming-webhook-effect:" + pointer.Id.ToString("N"));
    }
}
