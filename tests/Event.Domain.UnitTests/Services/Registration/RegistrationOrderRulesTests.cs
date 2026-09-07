using Explore.Domain.Enums;
using Explore.Domain.Services.Registration;

namespace Event.Domain.UnitTests.Services.Registration;

public sealed class RegistrationOrderRulesTests
{
    [Test]
    public async Task DescribeLifecycleReturnsFailClosedStateDerivedAffordances()
    {
        RegistrationOrderLifecycleDecision awaitingRequirements = RegistrationOrderRules.DescribeLifecycle(
            RegistrationOrderStatusEnum.AwaitingRequirements);
        RegistrationOrderLifecycleDecision ready = RegistrationOrderRules.DescribeLifecycle(
            RegistrationOrderStatusEnum.ReadyForCheckout);
        RegistrationOrderLifecycleDecision awaitingPayment = RegistrationOrderRules.DescribeLifecycle(
            RegistrationOrderStatusEnum.AwaitingPayment);
        RegistrationOrderLifecycleDecision invalid = RegistrationOrderRules.DescribeLifecycle(
            (RegistrationOrderStatusEnum)int.MaxValue);

        await Assert.That(awaitingRequirements.CanContinue).IsTrue();
        await Assert.That(awaitingRequirements.CanViewRequirementProgress).IsTrue();
        await Assert.That(awaitingRequirements.CanManagePromotion).IsFalse();
        await Assert.That(awaitingRequirements.CanFinalize).IsFalse();
        await Assert.That(awaitingRequirements.CanViewPaymentStatus).IsFalse();
        await Assert.That(awaitingRequirements.CanCancel).IsTrue();
        await Assert.That(ready.CanContinue).IsTrue();
        await Assert.That(ready.CanViewRequirementProgress).IsFalse();
        await Assert.That(ready.CanManagePromotion).IsTrue();
        await Assert.That(ready.CanFinalize).IsTrue();
        await Assert.That(ready.CanCancel).IsTrue();
        await Assert.That(ready.CanViewPaymentStatus).IsFalse();
        await Assert.That(awaitingPayment.CanViewPaymentStatus).IsTrue();
        await Assert.That(awaitingPayment.CanCancel).IsTrue();
        await Assert.That(invalid).IsEqualTo(default(RegistrationOrderLifecycleDecision));
    }

    [Test]
    public async Task OrderLookupEnums_UseStableIntegerIdentifiers()
    {
        await Assert.That((int)RegistrationOrderStatusEnum.Draft).IsEqualTo(1);
        await Assert.That((int)RegistrationOrderStatusEnum.AwaitingIdentity).IsEqualTo(2);
        await Assert.That((int)RegistrationOrderStatusEnum.AwaitingParticipantDetails).IsEqualTo(3);
        await Assert.That((int)RegistrationOrderStatusEnum.AwaitingRequirements).IsEqualTo(4);
        await Assert.That((int)RegistrationOrderStatusEnum.ReadyForCheckout).IsEqualTo(5);
        await Assert.That((int)RegistrationOrderStatusEnum.AwaitingPayment).IsEqualTo(6);
        await Assert.That((int)RegistrationOrderStatusEnum.AwaitingApproval).IsEqualTo(7);
        await Assert.That((int)RegistrationOrderStatusEnum.Waitlisted).IsEqualTo(8);
        await Assert.That((int)RegistrationOrderStatusEnum.Confirmed).IsEqualTo(9);
        await Assert.That((int)RegistrationOrderStatusEnum.Rejected).IsEqualTo(10);
        await Assert.That((int)RegistrationOrderStatusEnum.Expired).IsEqualTo(11);
        await Assert.That((int)RegistrationOrderStatusEnum.Cancelled).IsEqualTo(12);
        await Assert.That((int)RegistrationOrderStatusEnum.NeedsReconciliation).IsEqualTo(13);
        await Assert.That((int)BookingPartyTypeEnum.Individual).IsEqualTo(1);
        await Assert.That((int)BookingPartyTypeEnum.Household).IsEqualTo(2);
        await Assert.That((int)BookingPartyTypeEnum.Organization).IsEqualTo(3);
        await Assert.That((int)BookingPartyTypeEnum.Company).IsEqualTo(4);
        await Assert.That((int)BookingPartyTypeEnum.CommunityGroup).IsEqualTo(5);
        await Assert.That((int)RegistrationInventoryHoldStatusEnum.Active).IsEqualTo(1);
        await Assert.That((int)RegistrationInventoryHoldStatusEnum.Consumed).IsEqualTo(2);
        await Assert.That((int)RegistrationInventoryHoldStatusEnum.Released).IsEqualTo(3);
        await Assert.That((int)RegistrationInventoryHoldStatusEnum.Expired).IsEqualTo(4);
        await Assert.That((int)RegistrationInventoryHoldStatusEnum.Cancelled).IsEqualTo(5);
        await Assert.That((int)CapacityHoldPolicyEnum.NoHoldUntilReady).IsEqualTo(1);
        await Assert.That((int)CapacityHoldPolicyEnum.TimedHoldOnSelection).IsEqualTo(2);
        await Assert.That((int)CapacityHoldPolicyEnum.ApprovalNoHold).IsEqualTo(3);
        await Assert.That((int)CapacityHoldPolicyEnum.WaitlistWhenFull).IsEqualTo(4);
    }

    [Test]
    public async Task CanTransition_CharacterizesEveryOrderStatePair()
    {
        foreach (RegistrationOrderStatusEnum current in Enum.GetValues<RegistrationOrderStatusEnum>())
        {
            foreach (RegistrationOrderStatusEnum desired in Enum.GetValues<RegistrationOrderStatusEnum>())
            {
                bool expected = current == desired || AllowedTransitions[current].Contains(desired);

                await Assert.That(RegistrationOrderRules.CanTransition(current, desired))
                    .IsEqualTo(expected);
            }
        }

        await Assert.That(RegistrationOrderRules.IsTerminal(RegistrationOrderStatusEnum.Rejected)).IsTrue();
        await Assert.That(RegistrationOrderRules.IsTerminalForCurrentWorkstream(RegistrationOrderStatusEnum.AwaitingPayment)).IsTrue();
        await Assert.That(() => RegistrationOrderRules.EnsureCanTransition(
                RegistrationOrderStatusEnum.Rejected,
                RegistrationOrderStatusEnum.AwaitingRequirements))
            .Throws<InvalidOperationException>();
    }

    private static Dictionary<RegistrationOrderStatusEnum, RegistrationOrderStatusEnum[]> AllowedTransitions { get; } =
        new Dictionary<RegistrationOrderStatusEnum, RegistrationOrderStatusEnum[]>
        {
            [RegistrationOrderStatusEnum.Draft] =
            [
                RegistrationOrderStatusEnum.AwaitingIdentity,
                RegistrationOrderStatusEnum.AwaitingParticipantDetails,
                RegistrationOrderStatusEnum.AwaitingRequirements,
                RegistrationOrderStatusEnum.Expired,
                RegistrationOrderStatusEnum.Cancelled
            ],
            [RegistrationOrderStatusEnum.AwaitingIdentity] =
            [
                RegistrationOrderStatusEnum.AwaitingParticipantDetails,
                RegistrationOrderStatusEnum.AwaitingRequirements,
                RegistrationOrderStatusEnum.Expired,
                RegistrationOrderStatusEnum.Cancelled,
                RegistrationOrderStatusEnum.NeedsReconciliation
            ],
            [RegistrationOrderStatusEnum.AwaitingParticipantDetails] =
            [
                RegistrationOrderStatusEnum.AwaitingIdentity,
                RegistrationOrderStatusEnum.AwaitingRequirements,
                RegistrationOrderStatusEnum.Expired,
                RegistrationOrderStatusEnum.Cancelled,
                RegistrationOrderStatusEnum.NeedsReconciliation
            ],
            [RegistrationOrderStatusEnum.AwaitingRequirements] =
            [
                RegistrationOrderStatusEnum.AwaitingIdentity,
                RegistrationOrderStatusEnum.AwaitingParticipantDetails,
                RegistrationOrderStatusEnum.ReadyForCheckout,
                RegistrationOrderStatusEnum.AwaitingApproval,
                RegistrationOrderStatusEnum.Expired,
                RegistrationOrderStatusEnum.Cancelled,
                RegistrationOrderStatusEnum.NeedsReconciliation
            ],
            [RegistrationOrderStatusEnum.ReadyForCheckout] =
            [
                RegistrationOrderStatusEnum.AwaitingRequirements,
                RegistrationOrderStatusEnum.AwaitingPayment,
                RegistrationOrderStatusEnum.AwaitingApproval,
                RegistrationOrderStatusEnum.Waitlisted,
                RegistrationOrderStatusEnum.Confirmed,
                RegistrationOrderStatusEnum.Expired,
                RegistrationOrderStatusEnum.Cancelled,
                RegistrationOrderStatusEnum.NeedsReconciliation
            ],
            [RegistrationOrderStatusEnum.AwaitingPayment] =
            [
                RegistrationOrderStatusEnum.Cancelled,
                RegistrationOrderStatusEnum.NeedsReconciliation
            ],
            [RegistrationOrderStatusEnum.AwaitingApproval] =
            [
                RegistrationOrderStatusEnum.ReadyForCheckout,
                RegistrationOrderStatusEnum.Confirmed,
                RegistrationOrderStatusEnum.Rejected,
                RegistrationOrderStatusEnum.Waitlisted,
                RegistrationOrderStatusEnum.Expired,
                RegistrationOrderStatusEnum.Cancelled,
                RegistrationOrderStatusEnum.NeedsReconciliation
            ],
            [RegistrationOrderStatusEnum.Waitlisted] =
            [
                RegistrationOrderStatusEnum.AwaitingApproval,
                RegistrationOrderStatusEnum.Confirmed,
                RegistrationOrderStatusEnum.Expired,
                RegistrationOrderStatusEnum.Cancelled,
                RegistrationOrderStatusEnum.NeedsReconciliation
            ],
            [RegistrationOrderStatusEnum.Confirmed] = [],
            [RegistrationOrderStatusEnum.Rejected] = [],
            [RegistrationOrderStatusEnum.Expired] = [],
            [RegistrationOrderStatusEnum.Cancelled] = [],
            [RegistrationOrderStatusEnum.NeedsReconciliation] =
            [
                RegistrationOrderStatusEnum.AwaitingIdentity,
                RegistrationOrderStatusEnum.AwaitingParticipantDetails,
                RegistrationOrderStatusEnum.AwaitingRequirements,
                RegistrationOrderStatusEnum.ReadyForCheckout,
                RegistrationOrderStatusEnum.AwaitingPayment,
                RegistrationOrderStatusEnum.AwaitingApproval,
                RegistrationOrderStatusEnum.Waitlisted,
                RegistrationOrderStatusEnum.Confirmed,
                RegistrationOrderStatusEnum.Rejected,
                RegistrationOrderStatusEnum.Expired,
                RegistrationOrderStatusEnum.Cancelled
            ]
        };
}
