using Explore.Blazor.Client.Pages.Admin.Components;

namespace Explore.Blazor.Client.Tests.Pages.Admin;

public sealed class PaidEventPolicyEditModelTests
{
    [Test]
    public async Task FromPolicyToRequestRoundTripsEditableStateThroughGeneratedDtos()
    {
        PaidEventPolicyEditModel model = PaidEventPolicyEditModel.FromPolicy(Policy());

        RevisePaidEventPolicyDto request = model.ToRequest();

        await Assert.That(request.IsPaymentsEnabled).IsTrue();
        await Assert.That(request.RequiresLocalVerification).IsTrue();
        await Assert.That(request.AllowedOrganizerKindIds).IsEquivalentTo([1, 2, 4]);
        await Assert.That(request.AllowedCurrencyCodes).IsEquivalentTo(["EUR", "USD"]);
        await Assert.That(request.DefaultCurrencyCode).IsEqualTo("EUR");
        await Assert.That(request.RequiresFirstPaidEventReview).IsTrue();
        await Assert.That(request.FarFutureReviewThresholdDays).IsEqualTo(180);
        await Assert.That(request.CurrencyRiskLimits!.Select(limit => limit.CurrencyCode)).IsEquivalentTo(["EUR", "USD"]);
        await Assert.That(request.CurrencyRiskLimits!.Single(limit => limit.CurrencyCode == "EUR").PerEventSalesCeilingMinor).IsEqualTo(500_000);
    }

    [Test]
    public async Task ToRequestAlwaysEmitsMandatoryRefundFloor()
    {
        RevisePaidEventPolicyDto request = PaidEventPolicyEditModel.FromPolicy(Policy(refundProtectionIds: [1])).ToRequest();

        await Assert.That(request.RefundProtectionIds).IsEquivalentTo([1, 2, 3, 4, 5, 6, 7]);
    }

    [Test]
    public async Task FromTenantConfigurationExposesOnlyCeilingBackedOrganizerAndCurrencyChoices()
    {
        PaidEventPolicyDto ceiling = Policy(organizerKindIds: [2], currencyCodes: ["EUR"], defaultCurrencyCode: "EUR");
        var configuration = new HalResourceOfTenantPaidEventPolicyConfigurationDto
        {
            ActiveInstanceCeiling = ceiling,
            ActiveTenantOverride = Policy(organizerKindIds: [2], currencyCodes: ["EUR"], defaultCurrencyCode: "EUR")
        };

        PaidEventPolicyEditModel model = PaidEventPolicyEditModel.FromTenantConfiguration(configuration);

        await Assert.That(model.OrganizerKindOptions.Select(option => (option.Id, option.Label))).IsEquivalentTo([(2, "Organization")]);
        await Assert.That(model.CurrencyChoices).IsEquivalentTo(["EUR"]);
    }

    [Test]
    public async Task ValidateTenantNarrowingRejectsOrganizerAndCurrencyBroadeningOrReordering()
    {
        PaidEventPolicyDto ceiling = Policy(organizerKindIds: [2], currencyCodes: ["EUR", "USD"], defaultCurrencyCode: "EUR");
        PaidEventPolicyEditModel model = PaidEventPolicyEditModel.FromPolicy(Policy(organizerKindIds: [2, 4], currencyCodes: ["USD", "EUR"], defaultCurrencyCode: "USD"));

        PaidEventPolicyValidationResult result = model.ValidateTenantNarrowing(ceiling);

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Errors).Contains("Tenant organizer kinds must stay inside the instance policy.");
        await Assert.That(result.Errors).Contains("Tenant currencies must stay inside the instance policy and keep its order.");
    }

    [Test]
    public async Task ValidateTenantNarrowingAllowsStricterVerificationReviewRiskAndFarFutureLimits()
    {
        PaidEventPolicyDto ceiling = Policy(requiresLocalVerification: false, requiresFirstPaidEventReview: false, farFutureDays: 180);
        PaidEventPolicyEditModel model = PaidEventPolicyEditModel.FromPolicy(Policy(
            requiresLocalVerification: true,
            requiresFirstPaidEventReview: true,
            farFutureDays: 90,
            riskLimits:
            [
                RiskLimit("EUR", 400_000, 900_000, 200_000),
                RiskLimit("USD", 300_000, 800_000, 100_000)
            ]));

        PaidEventPolicyValidationResult result = model.ValidateTenantNarrowing(ceiling);

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.Errors).IsEmpty();
    }

    [Test]
    public async Task ValidateTenantNarrowingRejectsDisabledInstancePaymentsAndWeakenedFloors()
    {
        PaidEventPolicyDto ceiling = Policy(
            paymentsEnabled: false,
            requiresLocalVerification: true,
            requiresFirstPaidEventReview: true,
            farFutureDays: 180);
        PaidEventPolicyEditModel model = PaidEventPolicyEditModel.FromPolicy(Policy(
            paymentsEnabled: true,
            requiresLocalVerification: false,
            requiresFirstPaidEventReview: false,
            farFutureDays: null));

        PaidEventPolicyValidationResult result = model.ValidateTenantNarrowing(ceiling);

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Errors).Contains("Tenant paid events cannot be enabled when instance paid events are disabled.");
        await Assert.That(result.Errors).Contains("Tenant local verification cannot be weaker than the instance policy.");
        await Assert.That(result.Errors).Contains("Tenant first-paid-event review cannot be weaker than the instance policy.");
        await Assert.That(result.Errors).Contains("Tenant far-future review threshold cannot exceed or remove the instance threshold.");
    }

    [Test]
    public async Task ValidateTenantNarrowingReturnsSafeResultForInvalidRiskAndDefaultCurrency()
    {
        PaidEventPolicyDto ceiling = Policy();
        PaidEventPolicyEditModel model = PaidEventPolicyEditModel.FromPolicy(Policy(defaultCurrencyCode: "GBP"));
        model.CurrencyRiskLimits.Single(limit => limit.CurrencyCode == "EUR").PerEventSalesCeilingMinor = 600_000;

        PaidEventPolicyValidationResult result = model.ValidateTenantNarrowing(ceiling);

        RevisePaidEventPolicyDto request = model.ToRequest();

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Errors).Contains("Default currency must be one of the selected currencies.");
        await Assert.That(result.Errors).Contains("Tenant per-event sales ceiling cannot exceed or remove the instance ceiling.");
        await Assert.That(request.RefundProtectionIds).IsEquivalentTo([1, 2, 3, 4, 5, 6, 7]);
    }

    private static PaidEventPolicyDto Policy(
        bool paymentsEnabled = true,
        bool requiresLocalVerification = true,
        int[]? organizerKindIds = null,
        string[]? currencyCodes = null,
        string? defaultCurrencyCode = "EUR",
        int[]? refundProtectionIds = null,
        PaidEventPolicyCurrencyRiskLimitDto[]? riskLimits = null,
        bool requiresFirstPaidEventReview = true,
        int? farFutureDays = 180)
        => new()
        {
            IsPaymentsEnabled = paymentsEnabled,
            RequiresLocalVerification = requiresLocalVerification,
            AllowedOrganizerKindIds = organizerKindIds ?? [1, 2, 4],
            AllowedCurrencyCodes = currencyCodes ?? ["EUR", "USD"],
            DefaultCurrencyCode = defaultCurrencyCode,
            RefundProtectionIds = refundProtectionIds ?? [1, 2, 3, 4, 5, 6, 7],
            CurrencyRiskLimits = riskLimits ?? [RiskLimit("EUR", 500_000, 1_000_000, 250_000), RiskLimit("USD", 400_000, 900_000, 200_000)],
            RequiresFirstPaidEventReview = requiresFirstPaidEventReview,
            FarFutureReviewThresholdDays = farFutureDays
        };

    private static PaidEventPolicyCurrencyRiskLimitDto RiskLimit(
        string currencyCode,
        long? perEventSalesCeilingMinor,
        long? rollingOrganizerSalesCeilingMinor,
        long? highValueReviewThresholdMinor)
        => new()
        {
            CurrencyCode = currencyCode,
            PerEventSalesCeilingMinor = perEventSalesCeilingMinor,
            RollingOrganizerSalesCeilingMinor = rollingOrganizerSalesCeilingMinor,
            HighValueReviewThresholdMinor = highValueReviewThresholdMinor
        };
}
