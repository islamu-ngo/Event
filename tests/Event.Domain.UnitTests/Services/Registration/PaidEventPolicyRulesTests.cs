using Explore.Domain.Enums;
using Explore.Domain.Services.Registration;

namespace Event.Domain.UnitTests.Services.Registration;

public sealed class PaidEventPolicyRulesTests
{
    private static readonly Guid TenantId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000001");

    [Test]
    public async Task DefaultInstancePolicyIsDisabledOrganizationOnlyWithOrderedCurrencyCeiling()
    {
        PaidEventPolicyVersion policy = PaidEventPolicyVersion.CreateDefaultInstance();

        await Assert.That(policy.TenantId).IsNull();
        await Assert.That(policy.VersionNumber).IsEqualTo(1);
        await Assert.That(policy.IsActive).IsTrue();
        await Assert.That(policy.IsPaymentsEnabled).IsFalse();
        await Assert.That(policy.RequiresLocalVerification).IsFalse();
        await Assert.That(policy.AllowedOrganizerKinds.SequenceEqual([ActorTypeEnum.Organization])).IsTrue();
        await Assert.That(policy.AllowedCurrencyCodes.SequenceEqual(["EUR", "USD", "MAD", "SAR", "AED"])).IsTrue();
        await Assert.That(policy.RefundProtections.SequenceEqual(RequiredRefundFloor())).IsTrue();
        await Assert.That(policy.CurrencyRiskLimits).IsEmpty();
        await Assert.That(policy.DefaultCurrencyCode).IsNull();
        await Assert.That(policy.RequiresFirstPaidEventReview).IsFalse();
        await Assert.That(policy.FarFutureReviewThresholdDays).IsNull();
        await Assert.That(PaidEventPolicyRules.GetEffectiveCurrencyCodes(policy, null)).IsEmpty();
        await Assert.That(PaidEventPolicyRules.ResolveConfirmedCatalogCurrency(policy, null, suggestedCurrencyCode: null, confirmedCurrencyCode: "EUR")).IsNull();
    }

    [Test]
    public async Task CreateRevisionRetiresOriginalAndCopiesImmutableCollections()
    {
        ActorTypeEnum[] callerKinds = [ActorTypeEnum.Organization, ActorTypeEnum.Group];
        string[] callerCurrencies = ["EUR", "USD"];
        PaidEventPolicyVersion original = PaidEventPolicyVersion.CreateDefaultInstance().CreateRevision(
            true,
            callerKinds,
            true,
            callerCurrencies,
            "EUR",
            RequiredRefundFloor(),
            [PaidEventPolicyCurrencyRiskLimit.Create("EUR", 500_000, null, 1_000_000, null, 30, 250_000)],
            true,
            180);

        callerKinds[0] = ActorTypeEnum.User;
        callerCurrencies[0] = "MAD";
        PaidEventPolicyVersion revision = original.CreateRevision(
            false,
            original.AllowedOrganizerKinds,
            true,
            original.AllowedCurrencyCodes,
            "EUR",
            original.RefundProtections,
            original.CurrencyRiskLimits,
            original.RequiresFirstPaidEventReview,
            original.FarFutureReviewThresholdDays);

        await Assert.That(revision.Id).IsNotEqualTo(original.Id);
        await Assert.That(revision.VersionNumber).IsEqualTo(3);
        await Assert.That(original.IsActive).IsFalse();
        await Assert.That(original.ActiveUniquenessSlot).IsEqualTo(2);
        await Assert.That(revision.IsActive).IsTrue();
        await Assert.That(revision.ActiveUniquenessSlot).IsEqualTo(0);
        await Assert.That(revision.PolicyScopeKey).IsEqualTo("instance");
        await Assert.That(revision.AllowedOrganizerKinds.SequenceEqual([ActorTypeEnum.Organization, ActorTypeEnum.Group])).IsTrue();
        await Assert.That(revision.AllowedCurrencyCodes.SequenceEqual(["EUR", "USD"])).IsTrue();
        await Assert.That(revision.RefundProtections.SequenceEqual(RequiredRefundFloor())).IsTrue();
        await Assert.That(revision.CurrencyRiskLimits.Single().CurrencyCode).IsEqualTo("EUR");
        await Assert.That(revision.CurrencyRiskLimits.Single().PerEventSalesCeilingMinor).IsEqualTo(500_000);
        await Assert.That(() => original.CreateRevision(false, [ActorTypeEnum.Organization], true, ["EUR"], "EUR", RequiredRefundFloor(), [], false, null))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task InstanceRevisionCannotRemoveRequiredRefundFloorProtections()
    {
        PaidEventPolicyVersion original = CreateEnabledInstance([ActorTypeEnum.Organization], ["EUR"], "EUR");
        PaidEventRefundProtection[] weakenedFloor = RequiredRefundFloor()
            .Where(protection => protection != PaidEventRefundProtection.CancelledEventPlatformAmountsRefundedByDefault)
            .ToArray();

        await Assert.That(() => original.CreateRevision(
                true,
                original.AllowedOrganizerKinds,
                true,
                original.AllowedCurrencyCodes,
                "EUR",
                weakenedFloor,
                original.CurrencyRiskLimits,
                true,
                180))
            .Throws<ArgumentException>();
        await Assert.That(original.IsActive).IsTrue();
    }

    [Test]
    public async Task CurrencyResolutionRequiresActiveEnabledInstanceAndTenantPolicies()
    {
        PaidEventPolicyVersion disabledInstance = PaidEventPolicyVersion.CreateDefaultInstance();
        PaidEventPolicyVersion activeInstance = CreateEnabledInstance([ActorTypeEnum.Organization], ["EUR", "USD"], "EUR");
        PaidEventPolicyVersion disabledTenant = CreateTenantPolicy(activeInstance, [ActorTypeEnum.Organization], ["EUR"], "EUR", enabled: false);
        PaidEventPolicyVersion inactiveInstance = CreateEnabledInstance([ActorTypeEnum.Organization], ["EUR"], "EUR");
        _ = inactiveInstance.CreateRevision(false, [ActorTypeEnum.Organization], true, ["EUR"], "EUR", RequiredRefundFloor(), [], true, 90);
        PaidEventPolicyVersion inactiveTenant = CreateTenantPolicy(PaidEventPolicyVersion.CreateDefaultInstance().CreateRevision(true, [ActorTypeEnum.Organization], true, ["EUR"], "EUR", RequiredRefundFloor(), [PaidEventPolicyCurrencyRiskLimit.Create("EUR", 500_000, null, 1_000_000, null, 30, 250_000)], true, 180), [ActorTypeEnum.Organization], ["EUR"], "EUR");
        inactiveTenant.Retire();

        await Assert.That(PaidEventPolicyRules.GetEffectiveCurrencyCodes(disabledInstance, null)).IsEmpty();
        await Assert.That(PaidEventPolicyRules.ResolveConfirmedCatalogCurrency(disabledInstance, null, null, "EUR")).IsNull();
        await Assert.That(PaidEventPolicyRules.GetEffectiveCurrencyCodes(activeInstance, disabledTenant)).IsEmpty();
        await Assert.That(PaidEventPolicyRules.ResolveConfirmedCatalogCurrency(activeInstance, disabledTenant, null, "EUR")).IsNull();
        await Assert.That(PaidEventPolicyRules.GetEffectiveCurrencyCodes(inactiveInstance, null)).IsEmpty();
        await Assert.That(PaidEventPolicyRules.GetEffectiveCurrencyCodes(activeInstance, inactiveTenant)).IsEmpty();
    }

    [Test]
    public async Task ValidateTenantPolicyAcceptsTenantSubsetIncludingBelgiumEuroLock()
    {
        PaidEventPolicyVersion instance = CreateEnabledInstance([ActorTypeEnum.Organization, ActorTypeEnum.Group], ["EUR", "USD", "MAD"], "EUR");
        PaidEventPolicyVersion tenant = CreateTenantPolicy(instance, [ActorTypeEnum.Organization], ["EUR"], "EUR");

        PaidEventPolicyRules.ValidateTenantPolicy(instance, tenant);

        await Assert.That(PaidEventPolicyRules.GetEffectiveCurrencyCodes(instance, tenant).SequenceEqual(["EUR"])).IsTrue();
    }

    [Test]
    public async Task ValidateTenantPolicyRejectsBroadeningCurrencyKindOrDisabledInstance()
    {
        PaidEventPolicyVersion instance = CreateEnabledInstance([ActorTypeEnum.Organization], ["EUR", "USD"], "EUR");
        PaidEventPolicyVersion disabledInstance = PaidEventPolicyVersion.CreateDefaultInstance();

        await Assert.That(() => PaidEventPolicyRules.ValidateTenantPolicy(instance, CreateTenantPolicy(instance, [ActorTypeEnum.Organization], ["USD", "EUR"], "USD")))
            .Throws<InvalidOperationException>();
        await Assert.That(() => PaidEventPolicyRules.ValidateTenantPolicy(instance, CreateTenantPolicy(instance, [ActorTypeEnum.Organization, ActorTypeEnum.Group], ["EUR"], "EUR")))
            .Throws<InvalidOperationException>();
        await Assert.That(() => PaidEventPolicyRules.ValidateTenantPolicy(disabledInstance, CreateTenantPolicy(disabledInstance, [ActorTypeEnum.Organization], ["EUR"], "EUR", enabled: true)))
            .Throws<InvalidOperationException>();
        await Assert.That(PaidEventPolicyRules.ResolveConfirmedCatalogCurrency(instance, CreateTenantPolicy(instance, [ActorTypeEnum.Organization], ["USD", "EUR"], "USD"), null, "USD"))
            .IsNull();
    }

    [Test]
    public async Task ValidateTenantPolicyRejectsWeakerVerificationAndReviewThresholds()
    {
        PaidEventPolicyVersion instance = CreateEnabledInstance([ActorTypeEnum.Organization], ["EUR"], "EUR");

        await Assert.That(() => PaidEventPolicyRules.ValidateTenantPolicy(instance, CreateTenantPolicy(instance, [ActorTypeEnum.Organization], ["EUR"], "EUR", requiresLocalVerification: false)))
            .Throws<InvalidOperationException>();
        await Assert.That(() => PaidEventPolicyRules.ValidateTenantPolicy(instance, CreateTenantPolicy(instance, [ActorTypeEnum.Organization], ["EUR"], "EUR", requiresFirstPaidEventReview: false)))
            .Throws<InvalidOperationException>();
        await Assert.That(() => PaidEventPolicyRules.ValidateTenantPolicy(instance, CreateTenantPolicy(instance, [ActorTypeEnum.Organization], ["EUR"], "EUR", farFutureReviewThresholdDays: 181)))
            .Throws<InvalidOperationException>();
        await Assert.That(() => PaidEventPolicyRules.ValidateTenantPolicy(instance, CreateTenantPolicy(instance, [ActorTypeEnum.Organization], ["EUR"], "EUR", farFutureReviewThresholdDays: null)))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ValidateTenantPolicyRejectsWeakenedRefundProtections()
    {
        PaidEventPolicyVersion instance = CreateEnabledInstance([ActorTypeEnum.Organization], ["EUR"], "EUR");
        PaidEventRefundProtection[] weakenedFloor = RequiredRefundFloor()
            .Where(protection => protection != PaidEventRefundProtection.CardDisputeRightsNotWaived)
            .ToArray();

        PaidEventPolicyRules.ValidateTenantPolicy(instance, CreateTenantPolicy(instance, [ActorTypeEnum.Organization], ["EUR"], "EUR", refundProtections: RequiredRefundFloor()));
        await Assert.That(() => PaidEventPolicyRules.ValidateTenantPolicy(instance, CreateTenantPolicy(instance, [ActorTypeEnum.Organization], ["EUR"], "EUR", refundProtections: weakenedFloor)))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task ValidateTenantPolicyRejectsRaisedRemovedOrOutOfSubsetCurrencyRiskLimits()
    {
        PaidEventPolicyVersion instance = CreateEnabledInstance([ActorTypeEnum.Organization], ["EUR", "USD"], "EUR");

        PaidEventPolicyVersion accepted = CreateTenantPolicy(
            instance,
            [ActorTypeEnum.Organization],
            ["EUR"],
            "EUR",
            riskLimits: [PaidEventPolicyCurrencyRiskLimit.Create("EUR", 400_000, null, 900_000, null, 30, 200_000)]);

        PaidEventPolicyRules.ValidateTenantPolicy(instance, accepted);
        await Assert.That(accepted.CurrencyRiskLimits.Single().CurrencyCode).IsEqualTo("EUR");

        await Assert.That(() => PaidEventPolicyRules.ValidateTenantPolicy(instance, CreateTenantPolicy(instance, [ActorTypeEnum.Organization], ["EUR"], "EUR", riskLimits: [])))
            .Throws<InvalidOperationException>();
        await Assert.That(() => PaidEventPolicyRules.ValidateTenantPolicy(instance, CreateTenantPolicy(instance, [ActorTypeEnum.Organization], ["EUR"], "EUR", riskLimits: [PaidEventPolicyCurrencyRiskLimit.Create("EUR", null, null, 900_000, null, 30, 200_000)])))
            .Throws<InvalidOperationException>();
        await Assert.That(() => PaidEventPolicyRules.ValidateTenantPolicy(instance, CreateTenantPolicy(instance, [ActorTypeEnum.Organization], ["EUR"], "EUR", riskLimits: [PaidEventPolicyCurrencyRiskLimit.Create("EUR", 500_001, null, 900_000, null, 30, 200_000)])))
            .Throws<InvalidOperationException>();
        await Assert.That(() => CreateTenantPolicy(instance, [ActorTypeEnum.Organization], ["EUR"], "EUR", riskLimits: [PaidEventPolicyCurrencyRiskLimit.Create("USD", 1, null, 1, null, 30, 1)]))
            .Throws<ArgumentException>();
        await Assert.That(() => PaidEventPolicyCurrencyRiskLimit.Create("XXX", 1, null, null, null, null, null)).Throws<ArgumentException>();
        await Assert.That(() => PaidEventPolicyCurrencyRiskLimit.Create("EUR", 0, null, null, null, null, null)).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task FactoriesMaterializeSingleUseEnumerablesOnce()
    {
        PaidEventPolicyVersion policy = PaidEventPolicyVersion.CreateDefaultInstance().CreateRevision(
            true,
            SingleUse([ActorTypeEnum.Organization]),
            true,
            SingleUse(["EUR"]),
            "EUR",
            SingleUse(RequiredRefundFloor()),
            SingleUse([PaidEventPolicyCurrencyRiskLimit.Create("EUR", 500_000, null, 1_000_000, null, 30, 250_000)]),
            true,
            180);

        await Assert.That(policy.AllowedCurrencyCodes.Single()).IsEqualTo("EUR");
        await Assert.That(policy.RefundProtections.SequenceEqual(RequiredRefundFloor())).IsTrue();
        await Assert.That(policy.CurrencyRiskLimits.Single().CurrencyCode).IsEqualTo("EUR");
    }

    [Test]
    [Arguments(ActorTypeEnum.Organization, true)]
    [Arguments(ActorTypeEnum.Group, true)]
    [Arguments(ActorTypeEnum.User, true)]
    [Arguments(ActorTypeEnum.Bot, false)]
    [Arguments(ActorTypeEnum.System, false)]
    [Arguments(ActorTypeEnum.ExternalUnclassified, false)]
    public async Task IsOrganizerKindEligibleRejectsNonOrganizerActorTypes(ActorTypeEnum actorType, bool expected)
    {
        await Assert.That(PaidEventPolicyRules.IsOrganizerKindEligible(actorType)).IsEqualTo(expected);
        if (!expected)
        {
            await Assert.That(() => PaidEventPolicyVersion.CreateDefaultInstance().CreateRevision(true, [actorType], true, ["EUR"], "EUR", RequiredRefundFloor(), [], true, 1))
                .Throws<ArgumentException>();
        }
    }

    [Test]
    public async Task CurrencyResolutionPreservesInstanceOrderAndRequiresExplicitAllowedConfirmation()
    {
        PaidEventPolicyVersion instance = CreateEnabledInstance([ActorTypeEnum.Organization], ["EUR", "USD", "MAD", "SAR", "AED"], "EUR");
        PaidEventPolicyVersion tenant = CreateTenantPolicy(instance, [ActorTypeEnum.Organization], ["EUR", "MAD", "AED"], "EUR");

        await Assert.That(PaidEventPolicyRules.GetEffectiveCurrencyCodes(instance, tenant).SequenceEqual(["EUR", "MAD", "AED"])).IsTrue();
        await Assert.That(PaidEventPolicyRules.ResolveConfirmedCatalogCurrency(instance, tenant, suggestedCurrencyCode: "MAD", confirmedCurrencyCode: null)).IsNull();
        await Assert.That(PaidEventPolicyRules.ResolveConfirmedCatalogCurrency(instance, tenant, suggestedCurrencyCode: "USD", confirmedCurrencyCode: "mad")).IsEqualTo("MAD");
        await Assert.That(PaidEventPolicyRules.ResolveConfirmedCatalogCurrency(instance, tenant, suggestedCurrencyCode: null, confirmedCurrencyCode: "USD")).IsNull();
        await Assert.That(PaidEventPolicyRules.ResolveConfirmedCatalogCurrency(instance, tenant, suggestedCurrencyCode: null, confirmedCurrencyCode: "XXX")).IsNull();
        await Assert.That(PaidEventPolicyRules.ResolveConfirmedCatalogCurrency(instance, tenant, suggestedCurrencyCode: null, confirmedCurrencyCode: "dirham")).IsNull();
        await Assert.That(PaidEventPolicyRules.ResolveConfirmedCatalogCurrency(instance, tenant, suggestedCurrencyCode: null, confirmedCurrencyCode: "Saudi dirham")).IsNull();
    }

    [Test]
    public async Task CurrencyResolutionReturnsNoCurrencyWhenIntersectionIsEmpty()
    {
        PaidEventPolicyVersion instance = CreateEnabledInstance([ActorTypeEnum.Organization], ["EUR"], "EUR");
        PaidEventPolicyVersion tenant = PaidEventPolicyVersion.CreateTenant(
            TenantId,
            true,
            [ActorTypeEnum.Organization],
            true,
            ["USD"],
            "USD",
            RequiredRefundFloor(),
            [PaidEventPolicyCurrencyRiskLimit.Create("USD", 100, null, 100, null, 30, 100)],
            true,
            1);

        await Assert.That(PaidEventPolicyRules.GetEffectiveCurrencyCodes(instance, tenant)).IsEmpty();
        await Assert.That(PaidEventPolicyRules.ResolveConfirmedCatalogCurrency(instance, tenant, null, "USD")).IsNull();
    }

    private static PaidEventPolicyVersion CreateEnabledInstance(
        IEnumerable<ActorTypeEnum> allowedOrganizerKinds,
        IEnumerable<string> allowedCurrencyCodes,
        string defaultCurrencyCode)
    {
        string[] materializedCurrencyCodes = allowedCurrencyCodes.ToArray();
        PaidEventPolicyCurrencyRiskLimit[] riskLimits = materializedCurrencyCodes
            .Where(currencyCode => currencyCode is "EUR" or "USD")
            .Select(currencyCode => PaidEventPolicyCurrencyRiskLimit.Create(currencyCode, 500_000, null, 1_000_000, null, 30, 250_000))
            .ToArray();

        return PaidEventPolicyVersion.CreateDefaultInstance().CreateRevision(
            true,
            allowedOrganizerKinds,
            true,
            materializedCurrencyCodes,
            defaultCurrencyCode,
            RequiredRefundFloor(),
            riskLimits,
            true,
            180);
    }

    private static PaidEventPolicyVersion CreateTenantPolicy(
        PaidEventPolicyVersion instance,
        IEnumerable<ActorTypeEnum> allowedOrganizerKinds,
        IEnumerable<string> allowedCurrencyCodes,
        string defaultCurrencyCode,
        bool enabled = true,
        bool requiresLocalVerification = true,
        IEnumerable<PaidEventRefundProtection>? refundProtections = null,
        IEnumerable<PaidEventPolicyCurrencyRiskLimit>? riskLimits = null,
        bool requiresFirstPaidEventReview = true,
        int? farFutureReviewThresholdDays = 180) => PaidEventPolicyVersion.CreateTenant(
            TenantId,
            enabled,
            allowedOrganizerKinds,
            requiresLocalVerification,
            allowedCurrencyCodes,
            defaultCurrencyCode,
            refundProtections ?? instance.RefundProtections,
            riskLimits ?? instance.CurrencyRiskLimits.Where(limit => allowedCurrencyCodes.Contains(limit.CurrencyCode)),
            requiresFirstPaidEventReview,
            farFutureReviewThresholdDays);

    private static PaidEventRefundProtection[] RequiredRefundFloor() =>
    [
        PaidEventRefundProtection.OrganizerCancellationFullRefund,
        PaidEventRefundProtection.MaterialChangeBuyerChoiceOrFullRefund,
        PaidEventRefundProtection.DuplicateOrIncorrectChargeFullRefund,
        PaidEventRefundProtection.SubstantialNonDeliveryRemedy,
        PaidEventRefundProtection.AttendeeBuyerChangeTermsDisclosedSubjectToLaw,
        PaidEventRefundProtection.CardDisputeRightsNotWaived,
        PaidEventRefundProtection.CancelledEventPlatformAmountsRefundedByDefault
    ];

    private static IEnumerable<T> SingleUse<T>(IEnumerable<T> values) => new SingleUseEnumerable<T>(values);

    private sealed class SingleUseEnumerable<T>(IEnumerable<T> values) : IEnumerable<T>
    {
        private bool wasEnumerated;

        public IEnumerator<T> GetEnumerator()
        {
            if (wasEnumerated)
            {
                throw new InvalidOperationException("Sequence was enumerated more than once.");
            }

            wasEnumerated = true;
            return values.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
