using Explore.Application.Contracts.Services;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Application.UnitTests.Services.Registration;

public sealed class PaidCheckoutGovernanceOptionsTests
{
    [Test]
    public async Task DefaultsExposeNoUsableOperatorAndRemainSuspended()
    {
        var options = new PaidCheckoutGovernanceOptions();

        await Assert.That(options.IsComplete()).IsFalse();
        await Assert.That(options.IsActivated).IsFalse();
        await Assert.That(options.ChargeType).IsEqualTo("direct-charge");
    }

    [Test]
    public async Task CompleteOperatorOwnershipRequiresStartupApprovedActivation()
    {
        PaidCheckoutGovernanceOptions options = Complete();
        await Assert.That(options.IsComplete()).IsTrue();
        await Assert.That(options.IsActivated).IsTrue();
        options.ActivationStatus = "suspended";
        await Assert.That(options.IsComplete()).IsTrue();
        await Assert.That(options.IsActivated).IsFalse();
    }

    private static PaidCheckoutGovernanceOptions Complete() => new()
    {
        ComplaintOwner = "Trust and Safety",
        RefundOwner = "Payments Operations",
        DisputeOwner = "Dispute Operations",
        ReconciliationOwner = "Payment Reconciliation",
        ActivationStatus = "approved",
        RefundPolicyLanguageTag = "en-GB",
        StatementDescriptor = "EXAMPLE EVENT"
    };
}
