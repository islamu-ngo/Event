namespace Explore.Application.Contracts.Persistence;

/// <summary>Fixed fleet counts only; no tenant, target, transport or credential material.</summary>
public sealed record WebhookLocalDeliveryHealth(int DueLocalTargets, int StaleDeliveringTargets);

/// <summary>
/// Observes Local delivery through independent, unbound Global authority.
/// Missing or invalid authority throws rather than reporting an empty fleet.
/// </summary>
public interface IWebhookLocalDeliveryHealthObserver
{
    Task<WebhookLocalDeliveryHealth> ObserveAsync(
        DateTimeOffset observedAtUtc, CancellationToken cancellationToken);
}
