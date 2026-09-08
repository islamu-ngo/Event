// ABOUTME: Authorizes only PII-free post-confirmation guest status under current order and event row fences.
// ABOUTME: Preserves a live monotonic promise without renewing expired or unpromised historical access.

using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Features.RegistrationOrders.Handlers;

internal static class GuestRegistrationStatusAccessGuard
{
    internal static bool IsWellFormed(Guid tenantId, Guid eventId, Guid orderId, string? token) =>
        tenantId != Guid.Empty && eventId != Guid.Empty && orderId != Guid.Empty && token is { Length: 43 } &&
        token.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    // The caller owns a serializable transaction. Both reads are fresh, tenant-filtered entities;
    // neither tracked order graphs nor a second read through the general checkout guard are authority.
    internal static async Task<(RegistrationOrder Order, Event Event, DateTime Deadline)?> GetAsync(
        IRegistrationInventoryRepository inventory,
        IEventRepository events,
        IGuestCapabilityTokenService capabilities,
        Guid tenantId,
        Guid eventId,
        Guid orderId,
        string capabilityToken,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        RegistrationOrder? order = await inventory.GetGuestStatusOrderForUpdateAsync(orderId, tenantId, cancellationToken);
        if (order is null || order.EventId != eventId || order.GuestAccessTokenHash is null ||
            order.ConfirmedAt is null ||
            order.RegistrationOrderStatusId is not ((int)RegistrationOrderStatusEnum.Confirmed) and not ((int)RegistrationOrderStatusEnum.Cancelled) ||
            order.GuestStatusAccessUntilUtc is not { } promisedUntil ||
            promisedUntil <= timeProvider.GetUtcNow().UtcDateTime ||
            !capabilities.Matches(capabilityToken, order.GuestAccessTokenHash))
        {
            return null;
        }

        Event? target = await events.GetRegistrationStatusEventForUpdateAsync(eventId, tenantId, cancellationToken);
        if (target is null || promisedUntil <= timeProvider.GetUtcNow().UtcDateTime)
        {
            return null;
        }

        DateTime deadline = RegistrationOrder.GetGuestStatusDeadline(target.LastSessionEndUtc) is { } current && current > promisedUntil
            ? current : promisedUntil;
        if (deadline > promisedUntil)
        {
            if (!await inventory.TryExtendGuestStatusAccessAsync(order, deadline, cancellationToken))
            {
                return null;
            }

            // A CAS may wait too. Roll back an extension that crossed the original authority boundary;
            // returning null normally here would commit a promise that was no longer ours to extend.
            if (promisedUntil <= timeProvider.GetUtcNow().UtcDateTime)
            {
                throw new GuestStatusPromiseExpiredException();
            }
        }

        return (order, target, deadline);
    }

    internal sealed class GuestStatusPromiseExpiredException : Exception;
}
