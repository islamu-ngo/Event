using Explore.Domain;
using Explore.Domain.Enums;

namespace Event.Domain.UnitTests.Entities;

public sealed class RefundCampaignTests
{
    private static readonly Guid TenantId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000001");
    private static readonly DateTime Now = new(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task CancellationCampaignUsesFencedLeaseAndRestartSafeCursor()
    {
        RefundCampaign campaign = RefundCampaign.CreateCancellation(
            Guid.CreateVersion7(), TenantId, Guid.CreateVersion7(), Guid.CreateVersion7(),
            "Organizer cancelled the event.", Now);

        RefundCampaignClaim first = campaign.Claim(Guid.CreateVersion7(), Now.AddSeconds(1), TimeSpan.FromMinutes(2));
        RefundCampaignClaim replacement = campaign.Claim(Guid.CreateVersion7(), Now.AddMinutes(3), TimeSpan.FromMinutes(2));
        const long lastPaymentCursor = 7;

        await Assert.That(() => campaign.CompleteBatch(
                first, lastPaymentCursor, new RefundCampaignBatchOutcome(2, 2, 0), true, Now.AddMinutes(3)))
            .Throws<InvalidOperationException>();

        campaign.CompleteBatch(
            replacement, lastPaymentCursor, new RefundCampaignBatchOutcome(2, 2, 0), false, Now.AddMinutes(3));

        await Assert.That(campaign.Status).IsEqualTo(RefundCampaignStatus.Completed);
        await Assert.That(campaign.Cursor).IsEqualTo(lastPaymentCursor);
        await Assert.That(campaign.TotalPaymentCount).IsEqualTo(2);
        await Assert.That(campaign.GeneratedCount).IsEqualTo(0);
        await Assert.That(campaign.PendingCount).IsEqualTo(0);
    }
}
