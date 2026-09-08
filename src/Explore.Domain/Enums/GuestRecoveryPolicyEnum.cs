namespace Explore.Domain.Enums;

public enum GuestRecoveryPolicyEnum
{
    VerifiedEmailRequired = 1,
    UnverifiedEmailAccepted = 2,
    EmailOptional = 3,
    CapabilityLinkOnly = 4,
    NoRecovery = 5
}
