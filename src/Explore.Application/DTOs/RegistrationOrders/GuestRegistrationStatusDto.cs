// ABOUTME: Publishes only authorized guest order lifecycle facts and its finite status-access promise.
// ABOUTME: Excludes attendee data, venue details, payment facts and every capability or admission credential.

namespace Explore.Application.DTOs.RegistrationOrders;

public sealed record GuestRegistrationStatusDto(
    Guid EventId,
    Guid OrderId,
    int EventStatusId,
    int RegistrationOrderStatusId,
    DateTime ConfirmedAt,
    DateTime? CancelledAt,
    DateTimeOffset? LastSessionEndUtc,
    DateTimeOffset StatusAccessUntil);
