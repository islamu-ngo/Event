// ABOUTME: Captures the operator-declared public onboarding policy of a configurable account provider.
// ABOUTME: Unknown is deliberately distinct from existing-account login availability.

namespace Explore.Domain.Enums;

public enum PublicOnboardingPolicy
{
    Unknown = 0,
    Allowed = 1,
    Denied = 2
}
