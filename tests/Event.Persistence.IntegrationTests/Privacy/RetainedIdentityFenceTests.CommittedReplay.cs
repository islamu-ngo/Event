using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Privacy;

public sealed partial class RetainedIdentityFenceTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CommittedReplayInputFollowsActualRetainedRangeAndIgnoresHeldRowsBelowFloor(bool colocated)
    {
        await using var fixture = await Fixture.CreateAsync(colocated);
        await fixture.Authority.ExecuteSerializedAsync(_ => Task.FromResult(true), Token);
        var empty = await fixture.Authority.ExecuteSerializedAsync(
            fixture.Authority.ReadSqliteCommittedReplayInputStateAsync, Token);
        await Assert.That(empty).IsNotNull();
        await Assert.That(empty!.AuthorityState.HighWaterSequence).IsEqualTo(0L);
        for (var index = 0; index < 3; index++)
            await fixture.AppendFingerprintAsync(Request(Guid.CreateVersion7()));

        var appended = await fixture.Authority.ExecuteSerializedAsync(async token =>
        {
            var input = await fixture.Authority.ReadSqliteCommittedReplayInputStateAsync(token);
            await Assert.That(input).IsEqualTo(await fixture.Authority.AcquireRecoveryStateAsync(token));
            return input!;
        }, Token);
        await Assert.That(appended.StoreId).IsEqualTo(empty.StoreId);
        await Assert.That(appended.AuthorityState.HighWaterSequence).IsEqualTo(3L);
        await Assert.That(appended.AuthorityState.RetainedFloorSequence).IsEqualTo(0L);

        fixture.Clock.Now += fixture.Options.AuthorityRetention + TimeSpan.FromTicks(1);
        await fixture.Authority.CompactExpiredIntentsAsync(
            new(fixture.Clock.Now.UtcDateTime, 100, [2L]), Token);
        var held = await fixture.Authority.ExecuteSerializedAsync(
            fixture.Authority.ReadSqliteCommittedReplayInputStateAsync, Token);
        await Assert.That(held).IsNotNull();
        await Assert.That(held!.AuthorityState.RetainedFloorSequence).IsEqualTo(2L);
        await Assert.That(held.AuthorityState.HighWaterSequence).IsEqualTo(3L);
        await Assert.That(held.Revision).IsGreaterThan(appended.Revision);
        await Assert.That(held.AuthorityState.IdentityKeyVerificationTag)
            .IsEqualTo(appended.AuthorityState.IdentityKeyVerificationTag);

        await fixture.Authority.CompactExpiredIntentsAsync(
            new(fixture.Clock.Now.UtcDateTime, 100, []), Token);
        var compacted = await fixture.Authority.ExecuteSerializedAsync(
            fixture.Authority.ReadSqliteCommittedReplayInputStateAsync, Token);
        await Assert.That(compacted).IsNotNull();
        await Assert.That(compacted!.AuthorityState.RetainedFloorSequence).IsEqualTo(3L);
        await Assert.That(compacted.AuthorityState.HighWaterSequence).IsEqualTo(3L);
        await Assert.That(compacted.StoreId).IsEqualTo(appended.StoreId);
        await Assert.That(compacted.Revision).IsGreaterThan(held.Revision);
    }

    [Test]
    [Arguments("first")]
    [Arguments("middle")]
    [Arguments("tail")]
    [Arguments("pseudonymized")]
    [Arguments("ahead")]
    public async Task AnUnchangedCommittedCounterCannotHideAnUnreplayableRetainedRange(string corruption)
    {
        await using var fixture = await Fixture.CreateAsync();
        for (var index = 0; index < 3; index++)
            await fixture.AppendFingerprintAsync(Request(Guid.CreateVersion7()));
        var before = await fixture.Authority.ExecuteSerializedAsync(
            fixture.Authority.ReadSqliteCommittedReplayInputStateAsync, Token);
        await Assert.That(before).IsNotNull();
        await using (var db = fixture.AuthorityFactory.CreateDbContext())
        {
            switch (corruption)
            {
                case "first":
                    await db.ErasureIntents.Where(row => row.AuthoritySequence == 1).ExecuteDeleteAsync(Token);
                    break;
                case "middle":
                    await db.ErasureIntents.Where(row => row.AuthoritySequence == 2).ExecuteDeleteAsync(Token);
                    break;
                case "tail":
                    await db.ErasureIntents.Where(row => row.AuthoritySequence == 3).ExecuteDeleteAsync(Token);
                    break;
                case "pseudonymized":
                    var held = await db.ErasureIntents.SingleAsync(row => row.AuthoritySequence == 2, Token);
                    held.PseudonymizeForLegalHold(Guid.CreateVersion7(), Guid.CreateVersion7());
                    await db.SaveChangesAsync(Token);
                    break;
                case "ahead":
                    var now = fixture.Clock.Now.UtcDateTime;
                    db.ErasureIntents.Add(PrivacyErasureIntent.Record(Guid.CreateVersion7(), 4,
                        PrivacyErasureSubjectKind.User, Guid.CreateVersion7(),
                        PrivacyErasureReasonCode.AccountDeletion, 1, now, now,
                        now + fixture.Options.AuthorityRetention));
                    await db.SaveChangesAsync(Token);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(corruption));
            }
        }

        await fixture.Authority.ExecuteSerializedAsync(async token =>
        {
            await Assert.That(await fixture.Authority.ReadSqliteCommittedRecoveryStateAsync(token)).IsEqualTo(before);
            await Assert.That(await fixture.Authority.ReadSqliteCommittedReplayInputStateAsync(token)).IsNull();
            await Assert.That(await fixture.Authority.AcquireRecoveryStateAsync(token)).IsEqualTo(before);
            return true;
        }, Token);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CommittedReplayInputRequiresOriginalGateAndRejectsItsFlushedUncommittedAppend(bool colocated)
    {
        await using var fixture = await Fixture.CreateAsync(colocated);
        await Assert.That(() => fixture.Authority.ReadSqliteCommittedReplayInputStateAsync(Token))
            .Throws<InvalidOperationException>();
        await Assert.That(() => fixture.Authority.ExecuteSerializedAsync(
            fixture.Authority.ReadSqliteCommittedReplayInputStateAsync, Token)).Throws<InvalidOperationException>();
        await fixture.AppendFingerprintAsync(Request(Guid.CreateVersion7()));
        var before = await fixture.Authority.ExecuteSerializedAsync(
            fixture.Authority.ReadSqliteCommittedReplayInputStateAsync, Token);
        await using var otherPrimary = fixture.CreatePrimary();
        var otherOwner = fixture.CreateAuthority(otherPrimary);
        await Assert.That(() => fixture.Authority.ExecuteSerializedAsync(async token =>
        {
            await Assert.That(() => otherOwner.ReadSqliteCommittedReplayInputStateAsync(token))
                .Throws<InvalidOperationException>();
            await fixture.Authority.AppendAsync(Request(Guid.CreateVersion7()), token);
            return await fixture.Authority.ReadSqliteCommittedReplayInputStateAsync(token);
        }, Token)).Throws<InvalidOperationException>();
        await Assert.That(await fixture.Authority.ExecuteSerializedAsync(
            fixture.Authority.ReadSqliteCommittedReplayInputStateAsync, Token)).IsEqualTo(before);
        await Assert.That((await fixture.Authority.ReadAfterAsync(0, 10, Token)).Count).IsEqualTo(1);
    }

    [Test]
    public async Task CommittedReplayInputCannotEscapeTheOriginalWriterGate()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AppendFingerprintAsync(Request(Guid.CreateVersion7()));
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<PrivacyErasureRecoveryState?>? escaped = null;
        await fixture.Authority.ExecuteSerializedAsync(async token =>
        {
            escaped = Task.Run(async () =>
            {
                await release.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
                return await fixture.Authority.ReadSqliteCommittedReplayInputStateAsync(token);
            }, token);
            return await fixture.Authority.ReadSqliteCommittedReplayInputStateAsync(token);
        }, Token);
        release.SetResult();
        await Assert.That(async () => await escaped!.WaitAsync(TimeSpan.FromSeconds(10), Token))
            .Throws<InvalidOperationException>();
    }
}
