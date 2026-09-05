// ABOUTME: Computed outbound delivery capability, independent of authentication provider policy.
// ABOUTME: Distinguishes intentional disablement from missing, invalid, or unavailable transport.

namespace Explore.Domain.Enums;

public enum EmailDeliveryState
{
    Disabled,
    Unconfigured,
    Misconfigured,
    Available,
    Degraded
}
