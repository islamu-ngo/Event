
using System.Text.Json.Serialization;

namespace Explore.Application.DTOs.RegistrationOrders;

public sealed record GuestRegistrationStatusDto(
    Guid EventId,
    Guid OrderId,
    int EventStatusId,
    int RegistrationOrderStatusId,
    DateTime ConfirmedAt,
    DateTime? CancelledAt,
    DateTimeOffset? LastSessionEndUtc,
    DateTimeOffset StatusAccessUntil)
{
    [JsonIgnore]
    public bool CanCancelRegistration { get; init; }
}
