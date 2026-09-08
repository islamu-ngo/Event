using Explore.Application.Contracts.Webhooks;

namespace Event.Persistence.IntegrationTests.Fixtures;

internal sealed class FixedWebhookRetentionPolicyResolver : IWebhookRetentionPolicyResolver
{
    public WebhookRetentionPolicySnapshot Resolve(
        DateTimeOffset sourceOccurredAt,
        DateTimeOffset materializedAt,
        int? outboundPayloadRetentionDays = null) =>
        new(
            "webhook-retention-test-v1",
            materializedAt.AddDays(14),
            materializedAt.AddDays(outboundPayloadRetentionDays ?? 14),
            materializedAt.AddDays(30),
            materializedAt.AddDays(90),
            materializedAt.AddDays(90),
            materializedAt.AddDays(30),
            materializedAt.AddDays(365),
            materializedAt.AddDays(14));
}
