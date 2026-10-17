using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Webhooks.Requests.Commands;
using Explore.Domain;

namespace Explore.Application.Contracts.Services;

/// <summary>
/// Database-only scheduling inside the original selected tenant's actual human Serializable
/// attempt. The parent prepares the exact command outside transactions and supplies that
/// trusted, unconsumed capability here; this entry acquires it before any business reads.
/// The parent owns execution, final source/time admission and physical settlement.
/// </summary>
public interface ISelectedWebhookManualRetryScheduler
{
    Task<WebhookDeliverySingleDrainResult> ScheduleAsync(
        RetryWebhookDeliveryAttemptCommand command,
        IPreparedHumanWebhookMutation preparedRequester,
        WebhookAuditPrincipalKind principalKind,
        string principalReference,
        CancellationToken cancellationToken);
}
