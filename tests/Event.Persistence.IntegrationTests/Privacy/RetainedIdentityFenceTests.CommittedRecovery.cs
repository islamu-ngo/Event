using Explore.Domain;

namespace Event.Persistence.IntegrationTests.Privacy;

public sealed partial class RetainedIdentityFenceTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CommittedRecoveryReaderRequiresOriginalOwnerAndPreviouslyCommittedGate(bool colocated)
    {
        await using var fixture = await Fixture.CreateAsync(colocated);
        await Assert.That(() => fixture.Authority.ReadSqliteCommittedRecoveryStateAsync(Token))
            .Throws<InvalidOperationException>();
        await Assert.That(() => fixture.Authority.ExecuteSerializedAsync(
            fixture.Authority.ReadSqliteCommittedRecoveryStateAsync, Token)).Throws<InvalidOperationException>();
        await fixture.Authority.ExecuteSerializedAsync(_ => Task.FromResult(true), Token);
        await using var otherPrimary = fixture.CreatePrimary();
        var other = fixture.CreateAuthority(otherPrimary);
        await fixture.Authority.ExecuteSerializedAsync(async token =>
        {
            var original = await fixture.Authority.AcquireRecoveryStateAsync(token);
            await Assert.That(await fixture.Authority.ReadSqliteCommittedRecoveryStateAsync(token))
                .IsEqualTo(original);
            await Assert.That(() => other.ReadSqliteCommittedRecoveryStateAsync(token))
                .Throws<InvalidOperationException>();
            return true;
        }, Token);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CommittedRecoveryReaderMatchesFullRetainedTupleIncludingBoundKey(bool colocated)
    {
        await using var fixture = await Fixture.CreateAsync(colocated);
        await fixture.AppendFingerprintAsync(Request(Guid.CreateVersion7()));
        var expected = await fixture.Authority.ExecuteSerializedAsync(
            fixture.Authority.AcquireRecoveryStateAsync, Token);
        await fixture.Authority.ExecuteSerializedAsync(async token =>
        {
            var committed = await fixture.Authority.ReadSqliteCommittedRecoveryStateAsync(token);
            await Assert.That(committed).IsEqualTo(expected);
            await Assert.That(committed!.AuthorityState.HighWaterSequence).IsEqualTo(1L);
            await Assert.That(committed.AuthorityState.IdentityKeyId).IsEqualTo("test-key");
            await Assert.That(committed.AuthorityState.IdentityKeyVerificationTag)
                .IsEqualTo(expected.AuthorityState.IdentityKeyVerificationTag);
            await Assert.That(await fixture.Authority.AcquireRecoveryStateAsync(token)).IsEqualTo(expected);
            return true;
        }, Token);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    public async Task CommittedRecoveryReaderRejectsFlushedMutationAndPreservesCommittedTupleOnRollback(
        bool colocated, bool bindKey)
    {
        await using var fixture = await Fixture.CreateAsync(colocated);
        await fixture.Authority.ExecuteSerializedAsync(_ => Task.FromResult(true), Token);
        var original = await fixture.Authority.ExecuteSerializedAsync(
            fixture.Authority.AcquireRecoveryStateAsync, Token);
        await Assert.That(() => fixture.Authority.ExecuteSerializedAsync(async token =>
        {
            if (bindKey)
            {
                using var keys = new TestKeyProvider();
                using var key = await keys.ResolveAsync(token);
                await fixture.Authority.ValidateKeyAsync(key.KeyId, key.VerificationTag, token);
            }
            else
                await fixture.Authority.AppendAsync(Request(Guid.CreateVersion7()), token);
            return await fixture.Authority.ReadSqliteCommittedRecoveryStateAsync(token);
        }, Token)).Throws<InvalidOperationException>();
        await Assert.That(await fixture.Authority.ExecuteSerializedAsync(
            fixture.Authority.ReadSqliteCommittedRecoveryStateAsync, Token)).IsEqualTo(original);
        await Assert.That(await fixture.Authority.ReadAfterAsync(0, 10, Token)).IsEmpty();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CommittedRecoveryReaderTracksCompactionAndHoldReleaseWithoutReusingOldRevision(bool colocated)
    {
        await using var fixture = await Fixture.CreateAsync(colocated);
        await fixture.AppendFingerprintAsync(Request(Guid.CreateVersion7()));
        var appended = await fixture.Authority.ExecuteSerializedAsync(
            fixture.Authority.ReadSqliteCommittedRecoveryStateAsync, Token);
        fixture.Clock.Now += fixture.Options.AuthorityRetention + TimeSpan.FromTicks(1);
        await fixture.Authority.CompactExpiredIntentsAsync(
            new(fixture.Clock.Now.UtcDateTime, 100, [1L]), Token);
        var held = await fixture.Authority.ExecuteSerializedAsync(async token =>
        {
            var committed = await fixture.Authority.ReadSqliteCommittedRecoveryStateAsync(token);
            await Assert.That(committed).IsEqualTo(await fixture.Authority.AcquireRecoveryStateAsync(token));
            return committed!;
        }, Token);
        await Assert.That(held.StoreId).IsEqualTo(appended!.StoreId);
        await Assert.That(held.RecoveryGeneration).IsEqualTo(appended.RecoveryGeneration);
        await Assert.That(held.Revision).IsGreaterThan(appended.Revision);
        await Assert.That(held.AuthorityState.HighWaterSequence).IsEqualTo(1L);
        await Assert.That(held.AuthorityState.RetainedFloorSequence).IsEqualTo(1L);
        await Assert.That(held.AuthorityState.IdentityKeyVerificationTag)
            .IsEqualTo(appended.AuthorityState.IdentityKeyVerificationTag);

        await fixture.Authority.CompactExpiredIntentsAsync(
            new(fixture.Clock.Now.UtcDateTime, 100, []), Token);
        var released = await fixture.Authority.ExecuteSerializedAsync(async token =>
        {
            var committed = await fixture.Authority.ReadSqliteCommittedRecoveryStateAsync(token);
            await Assert.That(committed).IsEqualTo(await fixture.Authority.AcquireRecoveryStateAsync(token));
            return committed!;
        }, Token);
        await Assert.That(released.Revision).IsGreaterThan(held.Revision);
        await Assert.That(released.StoreId).IsEqualTo(held.StoreId);
        await Assert.That(released.RecoveryGeneration).IsEqualTo(held.RecoveryGeneration);
        await Assert.That(released.AuthorityState).IsEqualTo(held.AuthorityState);
        await fixture.Authority.CompactExpiredIntentsAsync(
            new(fixture.Clock.Now.UtcDateTime, 100, []), Token);
        await Assert.That(await fixture.Authority.ExecuteSerializedAsync(
            fixture.Authority.ReadSqliteCommittedRecoveryStateAsync, Token)).IsEqualTo(released);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CommittedRecoveryReaderCannotReuseEscapedExecutionContextAfterOriginalGateEnds(bool colocated)
    {
        await using var fixture = await Fixture.CreateAsync(colocated);
        await fixture.Authority.ExecuteSerializedAsync(_ => Task.FromResult(true), Token);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<PrivacyErasureRecoveryState?>? escaped = null;
        await fixture.Authority.ExecuteSerializedAsync(async token =>
        {
            escaped = Task.Run(async () =>
            {
                await release.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
                return await fixture.Authority.ReadSqliteCommittedRecoveryStateAsync(token);
            }, token);
            return await fixture.Authority.AcquireRecoveryStateAsync(token);
        }, Token);
        release.SetResult();
        await Assert.That(async () => await escaped!.WaitAsync(TimeSpan.FromSeconds(10), Token))
            .Throws<InvalidOperationException>();
    }
}
