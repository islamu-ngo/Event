using Explore.Domain;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationPolicyPublicationTests
{
    [Test]
    public async Task Operator_context_reads_current_revision_not_notification_snapshot_and_rejects_other_recipient()
    {
        await using var fixture = await RecognitionErasureFixture.CreateAsync(lawfulDeadline: TimeSpan.FromHours(1));
        await using var context = fixture.Database.Context();
        var repository = fixture.Repository(context);
        await repository.ReceiveAsync(fixture.Request, fixture.ReceiptHash, Now.AddHours(2), CancellationToken.None);
        fixture.Keys.Available = false;
        await repository.ComputeAsync(fixture.Request, CancellationToken.None);
        var pending = (await repository.FindAsync(fixture.Request.IntentId, CancellationToken.None))!;
        fixture.Clock.UtcNow = pending.EscalateAt;
        var source = await context.Set<ModerationRecognitionPrivacySource>()
            .IgnoreAllFilters("Exact operator context fixture").Include(row => row.Retention).SingleAsync();
        var notice = ModerationRecognitionEscalation.Require(Guid.CreateVersion7(), pending, null, source,
            source.RecoveryOwnerHumanId, fixture.Clock.UtcNow);
        context.Set<ModerationRecognitionEscalation>().Add(notice);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        await repository.ComputeAsync(fixture.Request, CancellationToken.None);
        var current = (await repository.FindAsync(fixture.Request.IntentId, CancellationToken.None))!;
        await Assert.That(current.Revision).IsGreaterThan(notice.PendingRevision);
        var ownerContext = await repository.ReadOwnerContextAsync(notice.Id, notice.RecipientHumanId, CancellationToken.None);
        await Assert.That(ownerContext).IsNotNull();
        await Assert.That(ownerContext!.PendingRevision).IsEqualTo(current.Revision);
        await Assert.That(ownerContext.SourceId).IsEqualTo(source.Id);
        await Assert.That(ownerContext.CanAssess).IsTrue();
        await Assert.That(ownerContext.CanAuthorize).IsFalse();
        await Assert.That(await repository.ReadOwnerContextAsync(notice.Id, source.PrivacyOwnerHumanId,
            CancellationToken.None)).IsNull();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repository.AcknowledgeAsync(notice.Id, notice.RecipientHumanId, CancellationToken.None));
        await fixture.WithdrawPrivacySourceAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repository.ReadOwnerContextAsync(notice.Id, notice.RecipientHumanId, CancellationToken.None));
    }

    [Test]
    public async Task Operator_context_does_not_disclose_or_enable_another_sources_assessment()
    {
        await using var fixture = await RecognitionErasureFixture.CreateAsync(lawfulDeadline: TimeSpan.FromHours(1));
        await AddIndependentDispositionContributionAsync(fixture);
        await using var context = fixture.Database.Context();
        var repository = fixture.Repository(context);
        await repository.ReceiveAsync(fixture.Request, fixture.ReceiptHash, Now.AddHours(2), CancellationToken.None);
        fixture.Keys.Available = false;
        await repository.ComputeAsync(fixture.Request, CancellationToken.None);
        var pending = (await repository.FindAsync(fixture.Request.IntentId, CancellationToken.None))!;
        fixture.Clock.UtcNow = pending.EscalateAt;
        var sources = await context.Set<ModerationRecognitionPrivacySource>()
            .IgnoreAllFilters("Exact independently owned operator context").Include(row => row.Retention).ToArrayAsync();
        var notices = sources.Select(source => ModerationRecognitionEscalation.Require(Guid.CreateVersion7(), pending,
            null, source, source.RecoveryOwnerHumanId, fixture.Clock.UtcNow)).ToArray();
        context.Set<ModerationRecognitionEscalation>().AddRange(notices);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var first = await repository.ReadOwnerContextAsync(notices[0].Id, notices[0].RecipientHumanId, CancellationToken.None);
        await Assert.That(first!.RequiredSourceCount).IsEqualTo(2);
        await Assert.That(first.SourceId).IsEqualTo(sources[0].Id);
        await Assert.That(await repository.ReadOwnerContextAsync(notices[1].Id, notices[0].RecipientHumanId,
            CancellationToken.None)).IsNull();
    }
}
