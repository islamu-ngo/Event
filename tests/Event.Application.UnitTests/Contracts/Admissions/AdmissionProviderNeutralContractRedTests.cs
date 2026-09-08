using ApplicationUnitTests.Contracts.Admissions.Support;
using Explore.Application.Contracts.Admissions;
using Explore.Application.Services.Registration;

namespace ApplicationUnitTests.Contracts.Admissions;

public sealed class AdmissionProviderNeutralContractRedTests
{
    private static readonly Type[] ExplicitContractTypes =
    [
        typeof(AdmissionIssuanceService),
        typeof(AdmissionIssuanceRequest),
        typeof(AdmissionIssuanceContext),
        typeof(AdmissionAssignmentFact),
        typeof(AdmissionIssuancePersistenceRequest),
        typeof(AdmissionIssuanceResult),
        typeof(AdmissionIssuanceOutcome),
        typeof(AdmissionDeliveryOutcome),
        typeof(AdmissionDeliveryFailure),
        typeof(AdmissionCredentialDeliveryEnvelope),
        typeof(AdmissionCredentialDirectDeliveryRequest),
        typeof(AdmissionCredentialDirectDeliveryOutcome),
        typeof(AdmissionCredentialDirectDeliveryResult),
        typeof(AdmissionCredentialCreateRequest),
        typeof(AdmissionCredentialVerificationRequest),
        typeof(AdmissionCredentialVerificationOutcome),
        typeof(AdmissionCredentialMaterial),
        typeof(AdmissionProtectedDeliveryMaterial),
        typeof(AdmissionDeliveryIntent),
        typeof(AdmissionDeliveryDispatchRequest),
        typeof(AdmissionDeliveryDispatchResult),
        typeof(IAdmissionIssuanceRepository),
        typeof(IAdmissionCredentialDigestService),
        typeof(IAdmissionDeliveryEnvelopeProtector),
        typeof(IAdmissionDeliveryDispatcher),
        typeof(IAdmissionCredentialDirectDeliveryChannel),
        typeof(IAdmissionCredentialDeliveryOutboxHandler)
    ];

    [Test]
    public async Task ExplicitAdmissionServiceSignaturesContainNoPaymentProviderOrSdkTypes()
    {
        Type nestedSentinel = typeof(Tuple<PaymentIntentSdkSentinel?, IReadOnlyList<StripeSdkSentinel[]>>);
        IReadOnlyCollection<Type> sentinelGraph = ProviderNeutralTypeGraph.Closure([nestedSentinel]);
        await Assert.That(sentinelGraph.Contains(typeof(PaymentIntentSdkSentinel))).IsTrue();
        await Assert.That(sentinelGraph.Contains(typeof(StripeSdkSentinel))).IsTrue();
        await Assert.That(sentinelGraph.Contains(typeof(ProviderSentinelBase))).IsTrue();
        await Assert.That(sentinelGraph.Contains(typeof(IStripeProviderSentinel))).IsTrue();

        IReadOnlyCollection<Type> constructorGraph = ProviderNeutralTypeGraph.Closure(
            ProviderNeutralTypeGraph.PublicSignatureTypes(typeof(AdditionalPublicConstructorSentinel)));
        await Assert.That(constructorGraph.Contains(typeof(PaymentIntentSdkSentinel))).IsTrue();
        await Assert.That(constructorGraph.Contains(typeof(StripeSdkSentinel))).IsTrue();
        await Assert.That(() => ProviderNeutralTypeGraph.EnsureProviderNeutralPublicConstructors(
            typeof(AdditionalPublicConstructorSentinel))).Throws<InvalidOperationException>();

        IReadOnlyCollection<Type> publicGraph = ProviderNeutralTypeGraph.Closure(
            ExplicitContractTypes.SelectMany(ProviderNeutralTypeGraph.PublicSignatureTypes));
        Type[] leaked = publicGraph
            .Where(ProviderNeutralTypeGraph.IsProviderSpecific)
            .Distinct()
            .ToArray();

        await Assert.That(leaked).IsEmpty();
    }

    private interface IStripeProviderSentinel;
    private abstract class ProviderSentinelBase;
    private sealed class StripeSdkSentinel : ProviderSentinelBase, IStripeProviderSentinel;
    private readonly record struct PaymentIntentSdkSentinel(int Value);

    private sealed class AdditionalPublicConstructorSentinel
    {
        public AdditionalPublicConstructorSentinel()
        {
        }

        public AdditionalPublicConstructorSentinel(
            Tuple<PaymentIntentSdkSentinel?, IReadOnlyList<StripeSdkSentinel[]>> providerShape)
        {
            GC.KeepAlive(providerShape);
        }
    }
}
