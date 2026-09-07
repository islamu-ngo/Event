using System.Text.Json;
using System.Text.Json.Serialization;
using Explore.Application.Contracts.Payments;

namespace Explore.Infrastructure.Payments.Stripe;

internal sealed record StripeRefundWebhookEnvelope(
    string EventId,
    string EventType,
    Guid RefundAttemptId,
    string ProviderRefundId,
    string ProviderPaymentId,
    string AccountId,
    long AmountMinor,
    string CurrencyCode,
    RefundProviderStatus Status,
    DateTime CreatedAt)
{
    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    public byte[] Serialize() => JsonSerializer.SerializeToUtf8Bytes(this, SerializerOptions);

    public static StripeRefundWebhookEnvelope? Deserialize(ReadOnlySpan<byte> payload) =>
        JsonSerializer.Deserialize<StripeRefundWebhookEnvelope>(payload, SerializerOptions);

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new JsonStringEnumConverter<RefundProviderStatus>());
        return options;
    }
}
