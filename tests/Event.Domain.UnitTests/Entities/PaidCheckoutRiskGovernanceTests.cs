using Explore.Domain;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Domain.UnitTests.Entities;

public sealed class PaidCheckoutRiskGovernanceTests
{
    private static readonly DateTime Now = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task ConfiguredCeilingAllowsCheckoutBelowExposureAndBlocksOnlyWouldExceed()
    {
        PaidEventPolicyCurrencyRiskLimit limit = PaidEventPolicyCurrencyRiskLimit.Create(
            "EUR", 10_000, 10, 50_000, 100, 30, 5_000);
        var below = new PaidCheckoutReservedExposure("EUR", 8_000, 8, 40_000, 80);

        await Assert.That(limit.WouldExceed(below, 1_000)).IsFalse();
        await Assert.That(limit.WouldExceed(below, 3_000)).IsTrue();
        await Assert.That(() => limit.WouldExceed(below with { CurrencyCode = "USD" }, 1_000)).Throws<ArgumentException>();
    }

    [Test]
    public async Task RollingCeilingRequiresExplicitWindowAndNeverInventsOne()
    {
        await Assert.That(() => PaidEventPolicyCurrencyRiskLimit.Create(
            "EUR", null, null, 50_000, null, null, null)).Throws<ArgumentException>();
        PaidEventPolicyCurrencyRiskLimit noRollingLimit = PaidEventPolicyCurrencyRiskLimit.Create(
            "EUR", 10_000, null, null, null, null, null);
        await Assert.That(noRollingLimit.RollingOrganizerWindowDays).IsNull();
    }

    [Test]
    public async Task ReviewApprovalRequiresSeparationAndBindsPolicyCurrencyTriggerAndMaximumAmount()
    {
        Guid requester = Guid.CreateVersion7();
        Guid reviewer = Guid.CreateVersion7();
        PaidCheckoutReviewApproval review = PaidCheckoutReviewApproval.Request(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            "EUR", PaidCheckoutReviewTrigger.HighValue, 5_000, requester, "high_value_review", Now);

        await Assert.That(() => review.Approve(requester, "self_review", Now.AddMinutes(1))).Throws<InvalidOperationException>();
        review.Approve(reviewer, "risk_review_complete", Now.AddMinutes(1));

        await Assert.That(review.Authorizes(review.PaidEventPolicyVersionId, "EUR", PaidCheckoutReviewTrigger.HighValue, 5_000)).IsTrue();
        await Assert.That(review.Authorizes(review.PaidEventPolicyVersionId, "EUR", PaidCheckoutReviewTrigger.HighValue, 5_001)).IsFalse();
        await Assert.That(review.ReviewedByUserId).IsEqualTo(reviewer);
    }
}
