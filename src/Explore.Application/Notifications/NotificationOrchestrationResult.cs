using Explore.Domain;

namespace Explore.Application.Notifications;

public sealed record NotificationOrchestrationResult(
    NotificationIntent Intent,
    NotificationOwnershipDecision Decision,
    NotificationDelivery? Delivery = null,
    NotificationExternalDelegation? ExternalDelegation = null,
    bool IsFenced = false)
{
    public static NotificationOrchestrationResult Fenced() => new(null!, null!, null, null, true);
}
