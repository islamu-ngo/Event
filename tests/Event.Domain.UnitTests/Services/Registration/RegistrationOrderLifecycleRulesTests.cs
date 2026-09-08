using Explore.Domain.Enums;
using Explore.Domain.Services.Registration;

namespace Event.Domain.UnitTests.Services.Registration;

public sealed class RegistrationOrderLifecycleRulesTests
{
    [Test]
    public async Task CanTransition_WhenApprovalIsGranted_ReturnsToReadyForCheckout()
    {
        bool canTransition = RegistrationOrderRules.CanTransition(
            RegistrationOrderStatusEnum.AwaitingApproval,
            RegistrationOrderStatusEnum.ReadyForCheckout);

        await Assert.That(canTransition).IsTrue();
    }

    [Test]
    public async Task CanTransition_WhenPaymentHasNotBeenSettled_AllowsCancellation()
    {
        bool canTransition = RegistrationOrderRules.CanTransition(
            RegistrationOrderStatusEnum.AwaitingPayment,
            RegistrationOrderStatusEnum.Cancelled);

        await Assert.That(canTransition).IsTrue();
    }
}
