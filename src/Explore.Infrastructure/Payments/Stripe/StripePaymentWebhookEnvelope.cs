using System.Text.Json;

namespace Explore.Infrastructure.Payments.Stripe;

internal sealed record StripePaymentWebhookEnvelope(
    string EventId,
    string EventType,
    string ObjectId,
    string AccountId,
    bool LiveMode,
    string ApiRevision,
    DateTime CreatedAt)
{
    public byte[] Serialize() => JsonSerializer.SerializeToUtf8Bytes(this);

    public static StripePaymentWebhookEnvelope? Deserialize(ReadOnlySpan<byte> payload) =>
        JsonSerializer.Deserialize<StripePaymentWebhookEnvelope>(payload);
}
