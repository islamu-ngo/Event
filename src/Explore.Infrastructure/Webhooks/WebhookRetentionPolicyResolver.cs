using Explore.Application.Contracts.Webhooks;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Webhooks;

public sealed class WebhookRetentionPolicyResolver(
    IOptionsMonitor<WebhookRetentionSettings> settings) : IWebhookRetentionPolicyResolver
{
    public WebhookRetentionPolicySnapshot Resolve(
        DateTimeOffset sourceOccurredAt,
        DateTimeOffset materializedAt,
        int? outboundPayloadRetentionDays = null)
    {
        if (sourceOccurredAt == default)
        {
            throw new ArgumentException("Source occurrence time is required.", nameof(sourceOccurredAt));
        }

        if (materializedAt == default || materializedAt < sourceOccurredAt)
        {
            throw new ArgumentOutOfRangeException(nameof(materializedAt));
        }

        var current = settings.CurrentValue;
        var outboundDays = outboundPayloadRetentionDays ?? current.OutboundPayloadRetentionDays;
        if (outboundDays is < 1 or > 3_650)
        {
            throw new ArgumentOutOfRangeException(nameof(outboundPayloadRetentionDays));
        }

        var policyVersion = string.Join(':',
            "webhook-retention-v1",
            $"i{current.InboundPayloadRetentionDays}",
            $"o{outboundDays}",
            $"a{current.ProcessingAttemptRetentionDays}",
            $"d{current.DeadLetterEvidenceRetentionDays}",
            $"p{current.ProviderPublicationRetentionDays}",
            $"l{current.OperationalLogRetentionDays}",
            $"u{current.AdministrativeAuditRetentionDays}",
            $"r{current.ReplayWindowDays}");

        return new WebhookRetentionPolicySnapshot(
            policyVersion,
            materializedAt.AddDays(current.InboundPayloadRetentionDays),
            sourceOccurredAt.AddDays(outboundDays),
            materializedAt.AddDays(current.ProcessingAttemptRetentionDays),
            materializedAt.AddDays(current.DeadLetterEvidenceRetentionDays),
            materializedAt.AddDays(current.ProviderPublicationRetentionDays),
            materializedAt.AddDays(current.OperationalLogRetentionDays),
            materializedAt.AddDays(current.AdministrativeAuditRetentionDays),
            materializedAt.AddDays(current.ReplayWindowDays));
    }
}
