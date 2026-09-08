using Explore.Domain;
using Explore.Domain.Enums;

namespace Event.Domain.UnitTests.Entities;

public sealed class EventCapacityPoolHoldPolicyTests
{
    [Test]
    [Arguments(CapacityHoldPolicyEnum.NoHoldUntilReady)]
    [Arguments(CapacityHoldPolicyEnum.TimedHoldOnSelection)]
    [Arguments(CapacityHoldPolicyEnum.ApprovalNoHold)]
    [Arguments(CapacityHoldPolicyEnum.WaitlistWhenFull)]
    public async Task CapacityPool_StoresEveryStableHoldPolicy(CapacityHoldPolicyEnum policy)
    {
        EventCapacityPool pool = EventCapacityPool.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "Policy pool",
            maximumQuantity: 10,
            holdDurationSeconds: 900,
            holdPolicy: policy,
            oversellPolicy: CapacityOversellPolicyEnum.Disallow,
            isActive: true);

        await Assert.That(pool.CapacityHoldPolicyId).IsEqualTo((int)policy);
    }
}
