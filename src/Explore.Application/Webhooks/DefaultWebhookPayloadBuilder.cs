using System.Security.Cryptography;
using System.Text.Json;
using Explore.Application.Contracts.Webhooks;

namespace Explore.Application.Webhooks;

public sealed class DefaultWebhookPayloadBuilder(IWebhookEventTypeRegistry eventTypeRegistry) : IWebhookPayloadBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<WebhookPayloadBuildResult> BuildAsync(
        WebhookEventBuildContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var descriptor = eventTypeRegistry.FindByName(context.EventType);
        if (descriptor is null)
        {
            return Task.FromResult(WebhookPayloadBuildResult.Failure(
                "unknown_event_type",
                $"Webhook event type '{context.EventType}' is not registered."));
        }

        var missingRequiredField = descriptor.DataFields.FirstOrDefault(
            field => field.Required && !context.Data.ContainsKey(field.Name));
        if (missingRequiredField is not null)
        {
            return Task.FromResult(WebhookPayloadBuildResult.Failure(
                "missing_required_payload_field",
                $"Webhook event type '{descriptor.Name}' requires data field '{missingRequiredField.Name}'."));
        }

        var data = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var field in descriptor.DataFields)
        {
            if (context.Data.TryGetValue(field.Name, out var value))
            {
                data[field.Name] = value;
            }
        }

        var envelope = new WebhookEventEnvelope(
            context.MessageId,
            descriptor.Name,
            descriptor.SchemaVersion,
            context.OccurredAt,
            context.TenantId,
            data);
        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions);
        var payloadHash = ComputeSha256Identifier(payloadBytes);
        var payloadRetentionUntil = context.OccurredAt.AddDays(descriptor.PayloadRetentionDays);

        return Task.FromResult(WebhookPayloadBuildResult.Success(
            envelope,
            payloadBytes,
            payloadHash,
            payloadRetentionUntil));
    }

    private static string ComputeSha256Identifier(ReadOnlySpan<byte> payloadBytes) =>
        $"sha256:{Convert.ToHexString(SHA256.HashData(payloadBytes)).ToLowerInvariant()}";
}
