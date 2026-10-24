using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationCaseNativeProofTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NativePreFindingPointerDisposalPreservesAnotherNotificationPurpose(bool withdrawal)
    {
        await using var fixture = await PreFindingFixture.CreateAsync();
        var unrelatedId = Guid.CreateVersion7();
        Guid custodyId;
        Guid privacyOwner;
        await using (var seed = fixture.Database.Context())
        {
            var custody = await seed.Set<ModerationPreFindingCustody>().SingleAsync();
            custodyId = custody.Id;
            privacyOwner = custody.PrivacyOwnerHumanId;
            // Unrelated historical inbox state supplies no private-case qualification.
            seed.NotificationIntents.Add(new NotificationIntent
            {
                Id = unrelatedId,
                TenantId = fixture.Database.TenantId,
                RecipientUserId = fixture.Database.CallerId,
                CategoryId = (int)NotificationCategoryEnum.TrustSafetyModeration,
                OwnershipTypeId = (int)NotificationOwnershipTypeEnum.IslamuEvent,
                RecipientKindId = (int)NotificationRecipientKindEnum.User,
                StatusId = (int)NotificationIntentStatusEnum.Resolved,
                TemplateKey = "system.notice.v1",
                DeduplicationKey = $"unrelated:{unrelatedId:N}",
                CreatedAt = fixture.Database.Clock.Now.UtcDateTime
            });
            await seed.SaveChangesAsync();
        }
        if (withdrawal)
            await Assert.That((await fixture.ExecuteAsync(privacyOwner,
                new WithdrawModerationPreFindingCustody(Guid.CreateVersion7(), fixture.Case.Id, 1, custodyId, 2))).Outcome)
                .IsEqualTo(ModerationQualificationWriteOutcome.Known);
        else
        {
            fixture.Database.Clock.Now = fixture.Until;
            await using var cleanup = fixture.Database.Context();
            await Assert.That(await fixture.Repository(cleanup, Guid.Empty).PurgeExpiredPreFindingAsync(100, default))
                .IsEqualTo(1);
        }
        await using var inspect = fixture.Database.Context();
        await AssertPreFindingPointerGraphAsync(inspect, fixture, retained: false);
        await Assert.That(await inspect.NotificationIntents.IgnoreQueryFilters().AnyAsync(row => row.Id == unrelatedId))
            .IsTrue();
        var notice = await inspect.Set<ModerationPreFindingNotice>().SingleAsync(row => row.Id == fixture.NoticeId);
        await Assert.That(notice.ProtectedNotice.IsEmpty).IsTrue();
        await Assert.That(notice.RetainUntil).IsEqualTo(fixture.Until);
        await Assert.That(notice.Withdrawn).IsEqualTo(withdrawal);
        await Assert.That(notice.AcknowledgedAt).IsNull();
    }
}
