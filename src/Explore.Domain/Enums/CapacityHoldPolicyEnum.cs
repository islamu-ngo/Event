namespace Explore.Domain.Enums;

public enum CapacityHoldPolicyEnum
{
    NoHoldUntilReady = 1,
    TimedHoldOnSelection = 2,
    ApprovalNoHold = 3,
    WaitlistWhenFull = 4
}
