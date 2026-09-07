using System.Text.Json;
using System.Text.Json.Serialization;
using Explore.Domain.Enums;

namespace Explore.Infrastructure.Payments.Stripe;

internal sealed record StripeDisputeWebhookEnvelope(
    string EventId,
    string EventType,
    string ProviderDisputeId,
    string ProviderPaymentId,
    string AccountId,
    long AmountMinor,
    string CurrencyCode,
    PaymentDisputeStage Stage,
    PaymentDisputeStatus Status,
    DateTime? ResponseDueAt,
    DateTime CreatedAt)
{
    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    public byte[] Serialize() => JsonSerializer.SerializeToUtf8Bytes(this, SerializerOptions);

    public static StripeDisputeWebhookEnvelope? Deserialize(ReadOnlySpan<byte> payload) =>
        JsonSerializer.Deserialize<StripeDisputeWebhookEnvelope>(payload, SerializerOptions);

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new JsonStringEnumConverter<PaymentDisputeStage>());
        options.Converters.Add(new JsonStringEnumConverter<PaymentDisputeStatus>());
        return options;
    }
}
