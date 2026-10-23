using Explore.Domain.Enums;

namespace Event.Domain.UnitTests.Services.Registration;

public sealed class PaidEventPolicyRestorationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task EvaluationRestoresStoredScopeIdentityVersionAndPolicyValues(bool tenantScoped)
    {
        Guid id = Guid.CreateVersion7();
        Guid? tenant = tenantScoped ? Guid.CreateVersion7() : null;
        PaidEventPolicyVersion policy = Restore(id, tenant, 7);

        await Assert.That(policy.Id).IsEqualTo(id);
        await Assert.That(policy.TenantId).IsEqualTo(tenant);
        await Assert.That(policy.PolicyScopeKey).IsEqualTo(
            tenant is { } value ? $"tenant:{value:N}" : "instance");
        await Assert.That(policy.VersionNumber).IsEqualTo(7);
        await Assert.That(policy.IsActive).IsTrue();
        await Assert.That(policy.IsPaymentsEnabled).IsTrue();
        await Assert.That(policy.RequiresLocalVerification).IsTrue();
        await Assert.That(policy.AllowedOrganizerKinds).IsEquivalentTo([ActorTypeEnum.Organization]);
        await Assert.That(policy.AllowedCurrencyCodes).IsEquivalentTo(["EUR", "USD"]);
        await Assert.That(policy.DefaultCurrencyCode).IsEqualTo("EUR");
        await Assert.That(policy.RefundProtections)
            .IsEquivalentTo([
                PaidEventRefundProtection.OrganizerCancellationFullRefund,
                PaidEventRefundProtection.MaterialChangeBuyerChoiceOrFullRefund,
                PaidEventRefundProtection.DuplicateOrIncorrectChargeFullRefund,
                PaidEventRefundProtection.SubstantialNonDeliveryRemedy,
                PaidEventRefundProtection.AttendeeBuyerChangeTermsDisclosedSubjectToLaw,
                PaidEventRefundProtection.CardDisputeRightsNotWaived,
                PaidEventRefundProtection.CancelledEventPlatformAmountsRefundedByDefault
            ]);
        await Assert.That(policy.RequiresFirstPaidEventReview).IsTrue();
        await Assert.That(policy.FarFutureReviewThresholdDays).IsEqualTo(30);
    }

    [Test]
    public async Task EvaluationRejectsAnEmptyTenantInsteadOfTreatingItAsInstanceScope()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            Task.FromResult(Restore(Guid.CreateVersion7(), Guid.Empty, 7)));
    }

    [Test]
    public async Task EvaluationRejectsMissingStoredIdentityOrVersion()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            Task.FromResult(Restore(Guid.Empty, null, 7)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            Task.FromResult(Restore(Guid.CreateVersion7(), null, 0)));
    }

    private static PaidEventPolicyVersion Restore(Guid id, Guid? tenant, int version) =>
        PaidEventPolicyVersion.RestoreActiveForPolicyEvaluation(
            id, tenant, version, true, [ActorTypeEnum.Organization], true,
            ["EUR", "USD"], "EUR",
            PaidEventPolicyVersion.CreateDefaultInstance().RefundProtections,
            [], true, 30);
}
