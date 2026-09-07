using Explore.Application.Contracts.Webhooks;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Webhooks;

public sealed class WebhookBulkReplayPolicyResolver(
    IOptionsMonitor<WebhookBulkReplaySettings> settings) : IWebhookBulkReplayPolicyResolver
{
    public WebhookBulkReplayLimits Resolve()
    {
        var current = settings.CurrentValue;
        var policyVersion = string.Join(':',
            "webhook-bulk-replay-v1",
            $"o{current.MaximumItemsPerOperation}",
            $"t{current.MaximumReservedItemsPerTenant}",
            $"w{current.MaximumFilterWindowDays}",
            $"p{current.OperationsPerPass}");
        return new WebhookBulkReplayLimits(
            current.MaximumItemsPerOperation,
            current.MaximumReservedItemsPerTenant,
            current.MaximumFilterWindowDays,
            current.OperationsPerPass,
            policyVersion);
    }
}
