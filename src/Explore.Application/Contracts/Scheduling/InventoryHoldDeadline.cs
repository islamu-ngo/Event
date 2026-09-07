using System.Globalization;

namespace Explore.Application.Contracts.Scheduling;

/// <summary>
/// The deadline key and pointer are a durable contract spread across three places — the handler that
/// registers a deadline, the lifecycle transition that cancels it, and the job that services it after a
/// restart. Defining them once means a cancellation can never quietly miss the trigger it was meant to
/// remove because two call sites formatted the same identifier differently.
/// <para>
/// Deadlines are keyed per order rather than per hold: a multi-line order would otherwise leave one trigger
/// per line to schedule and later unschedule, and the expiry work is already grouped by order.
/// </para>
/// </summary>
public static class InventoryHoldDeadline
{
    public static string KeyFor(Guid registrationOrderId)
        => registrationOrderId.ToString("D", CultureInfo.InvariantCulture);

    /// <summary>
    /// Durable identifiers only. Tenant identity travels with the deadline because the job runs outside any
    /// request and must re-establish tenant scope before touching data; it is a lookup key the job reads
    /// state under, never an authority claim the job trusts.
    /// </summary>
    public static IReadOnlyDictionary<string, string> PointerFor(Guid tenantId, Guid registrationOrderId)
        => new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ScheduledDeadlinePointerKeys.TenantId] = tenantId.ToString("D", CultureInfo.InvariantCulture),
            [ScheduledDeadlinePointerKeys.RegistrationOrderId] =
                registrationOrderId.ToString("D", CultureInfo.InvariantCulture),
        };
}
