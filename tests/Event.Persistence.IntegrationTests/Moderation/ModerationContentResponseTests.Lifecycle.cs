using System.Data;
using System.Security.Cryptography;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using NoticeDatabase = Event.Persistence.IntegrationTests.Moderation.ModerationNoticeReadTests.NoticeDatabase;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationContentResponseTests
{
    private static readonly DateTimeOffset OriginalAt = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Response_uses_actual_acknowledgement_calendar_floor_but_does_not_require_acknowledgement(bool acknowledged)
    {
        await using var fixture = await NoticeDatabase.CreateAsync(content: true,
            representativeUntil: OriginalAt.AddMonths(7));
        if (acknowledged)
        {
            await using var reader = fixture.Reader(fixture.Database.SubjectId);
            await Assert.That((await reader.Acknowledge(fixture.SubjectNoticeId, Guid.CreateVersion7(),
                fixture.SnapshotHash)).IsSuccess).IsTrue();
        }
        // First acknowledgement could no longer supply six calendar months inside R.
        // The separately protected response still remains available.
        fixture.Clock.UtcNow = OriginalAt.AddMonths(2);
        await using var session = new ResponseSession(fixture, fixture.Database.SubjectId);
        await Assert.That((await session.Handler.ExecuteAsync(Command(fixture))).IsSuccess).IsTrue();
        ModerationContentResponseAvailability? observed = null;
        var outcome = await session.Repository.DiscloseContentResponseAvailabilityAsync(
            fixture.Database.TenantId, fixture.SubjectNoticeId,
            (available, _) => { observed = available; return Task.FromResult(ReadOnlyMemory<byte>.Empty); },
            (_, _) => Task.CompletedTask, default);
        await Assert.That(outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        await Assert.That(observed!.NotifiedAt).IsEqualTo(acknowledged ? OriginalAt : (DateTimeOffset?)null);
        await Assert.That(observed.RemedyUntil).IsEqualTo(acknowledged
            ? new DateTimeOffset(2027, 4, 8, 12, 0, 0, TimeSpan.Zero) : (DateTimeOffset?)null);
        await Assert.That(observed.RetainUntil).IsEqualTo(new DateTimeOffset(2027, 5, 8, 12, 0, 0, TimeSpan.Zero));
        await using var inspect = Read(fixture);
        await Assert.That(await inspect.Set<ModerationNoticeNotificationReceipt>().CountAsync()).IsEqualTo(acknowledged ? 1 : 0);
    }

    [Test]
    public async Task Availability_preparation_cannot_release_an_affordance_after_current_assignment_expires()
    {
        await using var fixture = await NoticeDatabase.CreateAsync(content: true);
        await using var session = new ResponseSession(fixture, fixture.Database.SubjectId);
        var released = false;
        var outcome = await session.Repository.DiscloseContentResponseAvailabilityAsync(
            fixture.Database.TenantId, fixture.SubjectNoticeId, (_, _) =>
            {
                fixture.Clock.UtcNow = OriginalAt.AddDays(1);
                return Task.FromResult<ReadOnlyMemory<byte>>(new byte[] { 1 });
            }, (_, _) => { released = true; return Task.CompletedTask; }, default);
        await Assert.That(outcome).IsEqualTo(ModerationQualificationWriteOutcome.Unavailable);
        await Assert.That(released).IsFalse();
    }

    [Test]
    public async Task Original_ceiling_rejects_new_submission_and_old_success_receipt_replay()
    {
        await using var fixture = await NoticeDatabase.CreateAsync(content: true,
            representativeUntil: OriginalAt.AddYears(1));
        await using var session = new ResponseSession(fixture, fixture.Database.SubjectId);
        var original = Command(fixture);
        await Assert.That((await session.Handler.ExecuteAsync(original)).IsSuccess).IsTrue();
        fixture.Clock.UtcNow = fixture.Approval.RetainUntil;
        await Assert.That((await session.Handler.ExecuteAsync(original)).IsSuccess).IsFalse();
        await Assert.That((await session.Handler.ExecuteAsync(Command(fixture))).IsSuccess).IsFalse();
        await using var inspect = Read(fixture);
        await Assert.That((await inspect.Set<ModerationContentResponse>().SingleAsync()).RetainUntil)
            .IsEqualTo(fixture.Approval.RetainUntil);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Native_subject_erasure_deletes_response_text_and_receipt_without_claiming_original_notice_cleanup(bool rollback)
    {
        await using var fixture = await NoticeDatabase.CreateAsync(content: true);
        var command = Command(fixture);
        await using (var session = new ResponseSession(fixture, fixture.Database.SubjectId))
            await Assert.That((await session.Handler.ExecuteAsync(command)).IsSuccess).IsTrue();
        var now = fixture.Clock.UtcNow.UtcDateTime;
        var fact = PrivacyErasureIntent.Record(Guid.CreateVersion7(), 1, PrivacyErasureSubjectKind.User,
            fixture.Database.SubjectId, PrivacyErasureReasonCode.SubjectErasureRequest, 1, now, now);
        await using (var context = fixture.Database.Context())
        {
            context.PrivacyErasureSagas.Add(PrivacyErasureSaga.Start(fact, fact.AuthoritySequence,
                RandomNumberGenerator.GetBytes(32), now.AddHours(1), now));
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await new UserLocationPrivacyErasureRepository(context).EraseModerationCaseNarrativesAsync(fact, default);
            if (!rollback) await transaction.CommitAsync();
        }
        await using var inspect = Read(fixture);
        await Assert.That(await inspect.Set<ModerationContentResponse>().AnyAsync()).IsEqualTo(rollback);
        await Assert.That(await inspect.ModerationNoticeSources.AnyAsync()).IsTrue();
        var effect = await inspect.ModerationEffectIntents.SingleAsync();
        await Assert.That(effect.OriginalNoticeCleanupId).IsNull();
        await Assert.That(effect.CompletedAt).IsNull();
        // Erasure is not a new success receipt and cannot resurrect the old private submission.
        await using var retry = new ResponseSession(fixture, fixture.Database.SubjectId);
        await Assert.That((await retry.Handler.ExecuteAsync(command)).IsSuccess).IsFalse();
    }
}
