using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Explore.Application.Features.Webhooks.Requests.Commands;

namespace Explore.Application.Features.Webhooks.Handlers;

internal static class WebhookBulkReplayRequestIdentity
{
    public static string Compute(ScheduleWebhookBulkReplayCommand command)
    {
        var canonical = string.Join('|',
            command.TenantId.ToString("N"),
            command.FromUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            command.ToUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            command.WebhookConsumerId?.ToString("N") ?? string.Empty,
            command.WebhookEndpointId?.ToString("N") ?? string.Empty,
            command.EventType?.Trim() ?? string.Empty,
            command.MaxItems.ToString(CultureInfo.InvariantCulture),
            command.ReasonCode.Trim().ToLowerInvariant());
        return $"sha256:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant()}";
    }
}
