using Explore.Application.Contracts.PrivacyErasure;
using Explore.Domain;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Privacy;

public sealed partial class RetainedIdentityFenceTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RecoveryAcquisitionRequiresOriginalGateAndPreviouslyCommittedStore(bool colocated)
    {
        await using var fixture = await Fixture.CreateAsync(colocated);
        IPrivacyErasureRecoveryAuthority recovery = fixture.Authority;
        await Assert.That(() => recovery.AcquireRecoveryStateAsync(Token))
            .Throws<InvalidOperationException>();
        await Assert.That(() => fixture.Authority.ExecuteSerializedAsync(
            recovery.AcquireRecoveryStateAsync, Token)).Throws<InvalidOperationException>();

        await fixture.Authority.ExecuteSerializedAsync(_ => Task.FromResult(true), Token);
        PrivacyErasureRecoveryState state = await fixture.Authority.ExecuteSerializedAsync(
            recovery.AcquireRecoveryStateAsync, Token);
        await Assert.That(state.StoreId.Version).IsEqualTo(7);
        await Assert.That(state.AuthorityState.HighWaterSequence).IsEqualTo(0);
        await Assert.That(() => recovery.AcquireRecoveryStateAsync(Token))
            .Throws<InvalidOperationException>();

        await using ExploreDbContext otherPrimary = fixture.CreatePrimary();
        var other = fixture.CreateAuthority(otherPrimary);
        await fixture.Authority.ExecuteSerializedAsync(async token =>
        {
            await Assert.That(() => other.AcquireRecoveryStateAsync(token))
                .Throws<InvalidOperationException>();
            return true;
        }, Token);
        await Assert.That(await other.ExecuteSerializedAsync(other.AcquireRecoveryStateAsync, Token))
            .IsEqualTo(state);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AppendAndKeyBindingRollbackCannotPublishRecoveryCoordinates(bool colocated)
    {
        await using var fixture = await Fixture.CreateAsync(colocated);
        await fixture.Authority.ExecuteSerializedAsync(_ => Task.FromResult(true), Token);
        PrivacyErasureRecoveryState original = await fixture.Authority.ExecuteSerializedAsync(
            fixture.Authority.AcquireRecoveryStateAsync, Token);
        using var keyProvider = new TestKeyProvider();
        using PrivacyIdentityFenceKey key = await keyProvider.ResolveAsync(Token);

        await Assert.That(() => fixture.Authority.ExecuteSerializedAsync(async token =>
        {
            await fixture.Authority.ValidateKeyAsync(key.KeyId, key.VerificationTag, token);
            await fixture.Authority.AppendAsync(Request(Guid.CreateVersion7()), token);
            return await fixture.Authority.AcquireRecoveryStateAsync(token);
        }, Token)).Throws<InvalidOperationException>();

        await Assert.That(await fixture.Authority.ExecuteSerializedAsync(
            fixture.Authority.AcquireRecoveryStateAsync, Token)).IsEqualTo(original);
        await Assert.That(await fixture.Authority.ReadAfterAsync(0, 10, Token)).IsEmpty();
        await fixture.Authority.ExecuteSerializedAsync(async token =>
        {
            await fixture.Authority.ValidateKeyAsync(key.KeyId, key.VerificationTag, token);
            return await fixture.Authority.AppendAsync(Request(Guid.CreateVersion7()), token);
        }, Token);
        PrivacyErasureRecoveryState committed = await fixture.Authority.ExecuteSerializedAsync(
            fixture.Authority.AcquireRecoveryStateAsync, Token);
        await Assert.That(committed.StoreId).IsEqualTo(original.StoreId);
        await Assert.That(committed.Revision).IsEqualTo(original.Revision + 2);
        await Assert.That(committed.AuthorityState.HighWaterSequence).IsEqualTo(1);
        await Assert.That(committed.AuthorityState.IdentityKeyVerificationTag).IsEqualTo(key.VerificationTag);
    }

    [Test]
    public async Task ReleasedHoldChangesCoordinatesEvenWhenHighWaterFloorAndKeyAreUnchanged()
    {
        await using var fixture = await Fixture.CreateAsync();
        var request = Request(Guid.CreateVersion7());
        await fixture.Authority.AppendAsync(request, Token);
        PrivacyErasureRecoveryState appended = await fixture.Authority.ExecuteSerializedAsync(
            fixture.Authority.AcquireRecoveryStateAsync, Token);
        await fixture.Authority.AppendAsync(request, Token);
        await Assert.That(await fixture.Authority.ExecuteSerializedAsync(
            fixture.Authority.AcquireRecoveryStateAsync, Token)).IsEqualTo(appended);
        fixture.Clock.Now += fixture.Options.AuthorityRetention + TimeSpan.FromTicks(1);
        await fixture.Authority.CompactExpiredIntentsAsync(new(fixture.Clock.Now.UtcDateTime, 100, [1L]), Token);
        PrivacyErasureRecoveryState held = await fixture.Authority.ExecuteSerializedAsync(
            fixture.Authority.AcquireRecoveryStateAsync, Token);

        await fixture.Authority.CompactExpiredIntentsAsync(new(fixture.Clock.Now.UtcDateTime, 100, []), Token);
        PrivacyErasureRecoveryState released = await fixture.Authority.ExecuteSerializedAsync(
            fixture.Authority.AcquireRecoveryStateAsync, Token);

        await Assert.That(released.AuthorityState).IsEqualTo(held.AuthorityState);
        await Assert.That(released.Revision).IsGreaterThan(held.Revision);
        await Assert.That(released.StoreId).IsEqualTo(held.StoreId);
        await Assert.That(released.RecoveryGeneration).IsEqualTo(held.RecoveryGeneration);
        await fixture.Authority.CompactExpiredIntentsAsync(new(fixture.Clock.Now.UtcDateTime, 100, []), Token);
        await Assert.That(await fixture.Authority.ExecuteSerializedAsync(
            fixture.Authority.AcquireRecoveryStateAsync, Token)).IsEqualTo(released);
    }

    [Test]
    public async Task FailedCompactionRollsBackRecoveryRevisionAndRetainedFloorTogether()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Authority.AppendAsync(Request(Guid.CreateVersion7()), Token);
        PrivacyErasureRecoveryState original = await fixture.Authority.ExecuteSerializedAsync(
            fixture.Authority.AcquireRecoveryStateAsync, Token);
        fixture.Clock.Now += fixture.Options.AuthorityRetention + TimeSpan.FromTicks(1);
        await using (var db = fixture.AuthorityFactory.CreateDbContext())
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TRIGGER reject_compaction BEFORE DELETE ON ie_erasure_intents
                BEGIN SELECT RAISE(ABORT, 'injected_compaction_failure'); END;
                """, Token);

        await Assert.That(() => fixture.Authority.CompactExpiredIntentsAsync(
            new(fixture.Clock.Now.UtcDateTime, 100, []), Token)).Throws<DbUpdateException>();

        await Assert.That(await fixture.Authority.ExecuteSerializedAsync(
            fixture.Authority.AcquireRecoveryStateAsync, Token)).IsEqualTo(original);
        await Assert.That((await fixture.Authority.ReadAfterAsync(0, 10, Token)).Count).IsEqualTo(1);
    }

    [Test]
    public async Task EscapedExecutionContextCannotReuseAnEndedRecoveryGate()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Authority.ExecuteSerializedAsync(_ => Task.FromResult(true), Token);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<PrivacyErasureRecoveryState>? escaped = null;
        await fixture.Authority.ExecuteSerializedAsync(async token =>
        {
            escaped = Task.Run(async () =>
            {
                await release.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
                return await fixture.Authority.AcquireRecoveryStateAsync(token);
            }, token);
            return await fixture.Authority.AcquireRecoveryStateAsync(token);
        }, Token);
        release.SetResult();
        await Assert.That(async () => await escaped!.WaitAsync(TimeSpan.FromSeconds(10), Token))
            .Throws<InvalidOperationException>();
    }
}
