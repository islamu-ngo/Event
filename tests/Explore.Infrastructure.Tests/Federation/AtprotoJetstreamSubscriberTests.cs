using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using CarpaNet;
using CarpaNet.Jetstream;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Federation.Atproto.Models;
using Explore.Application.Features.Federation.Atproto.Requests.Commands;
using Explore.Domain.Federation;
using Explore.Infrastructure.Services.Federation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Tests.Federation;

public sealed class AtprotoJetstreamSubscriberTests
{
    private static readonly DateTime ObservedAt = new(2026, 7, 19, 10, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// v2 splits the v1 microsecond cursor into a resume token (<c>seq</c>) and an ordering key
    /// (<c>time_us</c>). Envelopes derive their timestamp from this base so the two axes stay visibly
    /// distinct in assertions.
    /// </summary>
    private static readonly long BaseTimeUs = (ObservedAt - DateTime.UnixEpoch).Ticks / 10;

    private const string AllowedDid = "did:plc:remote-owner";

    [Test]
    public async Task RunSingleLease_UsesExactCollectionsAndAtomicallyAppliesEnabledTenantPresentation()
    {
        Guid tenantId = Guid.CreateVersion7();
        var store = new FakeRuntimeStore([tenantId]);
        var source = new FakeEventSource([EventEnvelope(100)]);
        using var subscriber = CreateSubscriber(store, source);

        bool consumed = await subscriber.RunSingleLeaseAsync(CancellationToken.None);

        await Assert.That(consumed).IsTrue();
        await Assert.That(source.Subscription!.Collections)
            .IsEquivalentTo([AtprotoJetstreamConstants.EventCollection, AtprotoJetstreamConstants.RsvpCollection]);
        await Assert.That(source.Subscription.Collections).DoesNotContain(collection => collection.Contains('*'));
        await Assert.That(source.Subscription.Dids.Select(did => did.Value)).IsEquivalentTo([AllowedDid]);
        await Assert.That(store.Applied).HasSingleItem();
        await Assert.That(store.Applied[0].Record!.Collection).IsEqualTo(AtprotoJetstreamConstants.EventCollection);
        await Assert.That(store.Applied[0].Presentations.Select(value => value.TenantId)).IsEquivalentTo([tenantId]);
        await Assert.That(store.Applied[0].EventProjection!.Name).IsEqualTo("Remote event");
        await Assert.That(store.Applied[0].EventProjection!.AtprotoRecordId)
            .IsEqualTo(store.Applied[0].Record!.Id);
    }

    [Test]
    public async Task RunSingleLease_AdvancesCursorBySeqButVersionsRecordByTimeUs()
    {
        var store = new FakeRuntimeStore([Guid.CreateVersion7()]);
        var source = new FakeEventSource([EventEnvelope(100)]);
        using var subscriber = CreateSubscriber(store, source);

        await subscriber.RunSingleLeaseAsync(CancellationToken.None);

        // Binding SourceVersion to seq would invert last-writer-wins against PDS snapshot versions,
        // which are unix microseconds.
        await Assert.That(store.Cursor).IsEqualTo(100);
        await Assert.That(store.Applied[0].Record!.SourceCursor).IsEqualTo(100);
        await Assert.That(store.Applied[0].Record!.SourceVersion).IsEqualTo(BaseTimeUs + 100);
        await Assert.That(store.Applied[0].EventProjection!.SourceVersion).IsEqualTo(BaseTimeUs + 100);
    }

    [Test]
    public async Task RunSingleLease_DisabledCapabilityDoesNotOpenStreamOrMaterialize()
    {
        var store = new FakeRuntimeStore([]);
        var source = new FakeEventSource([EventEnvelope(100)]);
        using var subscriber = CreateSubscriber(store, source);

        bool consumed = await subscriber.RunSingleLeaseAsync(CancellationToken.None);

        await Assert.That(consumed).IsFalse();
        await Assert.That(source.Subscription).IsNull();
        await Assert.That(store.Applied).IsEmpty();
    }

    [Test]
    public async Task RunSingleLease_EnabledCapabilityWithEmptyDidFilterOpensPublicCollectionStream()
    {
        var store = new FakeRuntimeStore([Guid.CreateVersion7()]);
        var source = new FakeEventSource([EventEnvelope(100)]);
        using var subscriber = CreateSubscriber(store, source, allowedDids: []);

        bool consumed = await subscriber.RunSingleLeaseAsync(CancellationToken.None);

        await Assert.That(consumed).IsTrue();
        await Assert.That(source.Subscription!.Dids.Select(did => did.Value)).IsEmpty();
        await Assert.That(store.Applied).HasSingleItem();
        await Assert.That(store.Applied[0].Record).IsNotNull();
    }

    [Test]
    public async Task EventSource_CancelledTokenDoesNotOpenSession()
    {
        var source = new CarpaNetJetstreamEventSource();
        var subscription = new AtprotoJetstreamSubscription(
            new Uri("https://jetstream.example.test"),
            AtprotoJetstreamConstants.Collections,
            Dids: [],
            LiveCursor: null,
            MaxMessageSizeBytes: 2_113_536);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => source.OpenSessionAsync(
            subscription,
            cancellation.Token));
    }

    [Test]
    public async Task RunSingleLease_CapabilityRevokedAfterConnectStopsNewPresentationAndCursor()
    {
        var store = new FakeRuntimeStore([Guid.CreateVersion7()]);
        var source = new FakeEventSource([EventEnvelope(100)])
        {
            BeforeYield = () => store.EnabledTenants = []
        };
        using var subscriber = CreateSubscriber(store, source);

        bool consumed = await subscriber.RunSingleLeaseAsync(CancellationToken.None);

        await Assert.That(consumed).IsFalse();
        await Assert.That(store.Cursor).IsEqualTo(0);
        await Assert.That(store.Applied).IsEmpty();
    }

    [Test]
    public async Task RunSingleLease_FailedApplyLeavesCursorForReplayAndSecondAttemptConverges()
    {
        var store = new FakeRuntimeStore([Guid.CreateVersion7()]) { FailNextApply = true };
        var source = new FakeEventSource([EventEnvelope(100)]);
        using var subscriber = CreateSubscriber(store, source);

        bool first = await subscriber.RunSingleLeaseAsync(CancellationToken.None);
        bool second = await subscriber.RunSingleLeaseAsync(CancellationToken.None);

        await Assert.That(first).IsFalse();
        await Assert.That(second).IsTrue();
        await Assert.That(store.Cursor).IsEqualTo(100);
        await Assert.That(store.Applied).Count().IsEqualTo(2);
    }

    [Test]
    public async Task RunSingleLease_CrashAfterAtomicApplyReplaysWithoutDuplicate()
    {
        var store = new FakeRuntimeStore([Guid.CreateVersion7()]) { ThrowAfterNextApply = true };
        var source = new FakeEventSource([EventEnvelope(100), EventEnvelope(100)]);
        using var subscriber = CreateSubscriber(store, source);

        await Assert.ThrowsAsync<InvalidOperationException>(() => subscriber.RunSingleLeaseAsync(CancellationToken.None));
        bool replayed = await subscriber.RunSingleLeaseAsync(CancellationToken.None);

        await Assert.That(replayed).IsFalse();
        await Assert.That(store.Cursor).IsEqualTo(100);
        await Assert.That(store.Applied).HasSingleItem();
    }

    [Test]
    public async Task HostedSubscriber_StreamFailureReconnectsAndConverges()
    {
        var store = new FakeRuntimeStore([Guid.CreateVersion7()]);
        var source = new FakeEventSource([EventEnvelope(100)]) { FailuresRemaining = 1 };
        using var subscriber = CreateSubscriber(store, source);

        Task applyCompleted = store.ApplyCalled.Task;
        await subscriber.StartAsync(CancellationToken.None);
        try
        {
            await applyCompleted.WaitAsync(TimeSpan.FromSeconds(5));

            await Assert.That(store.Cursor).IsEqualTo(100);
            await Assert.That(source.SubscriptionCount).IsGreaterThanOrEqualTo(2);
            await Assert.That(store.Applied).HasSingleItem();
        }
        finally
        {
            await subscriber.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task RunSingleLease_ChangedAllowedDidsReopensSessionWithNewFilterUnderSameLease()
    {
        var store = new FakeRuntimeStore([Guid.CreateVersion7()]);
        var source = new FakeEventSource([]) { WaitForCancellation = true };
        var options = new FakeOptionsMonitor(TestOptions([AllowedDid]));
        var time = new ManualTimeProvider(ObservedAt);
        using var subscriber = new AtprotoJetstreamSubscriber(
            store,
            source,
            options,
            time,
            NullLogger<AtprotoJetstreamSubscriber>.Instance);
        using var cancellation = new CancellationTokenSource();
        Task<bool> run = subscriber.RunSingleLeaseAsync(cancellation.Token);

        try
        {
            await source.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            options.Set(TestOptions(["did:plc:updated-owner"]));
            await time.WaitForTimerAsync(TimeSpan.FromMilliseconds(100)).WaitAsync(TimeSpan.FromSeconds(5));
            time.Advance(TimeSpan.FromMilliseconds(100));
            await source.WaitForSessionCountAsync(2).WaitAsync(TimeSpan.FromSeconds(5));

            // v2 filters are immutable per connection, so the new scope has to arrive as a new session.
            await Assert.That(source.SubscriptionCount).IsEqualTo(2);
            await Assert.That(source.Subscriptions[0].Dids.Select(did => did.Value)).IsEquivalentTo([AllowedDid]);
            await Assert.That(source.Subscriptions[1].Dids.Select(did => did.Value)).IsEquivalentTo(["did:plc:updated-owner"]);
            await Assert.That(source.Sessions[0].Disposed).IsTrue();
            await Assert.That(store.ClaimCount).IsEqualTo(1);

            source.Sessions[1].Push(EventEnvelope(100, did: "did:plc:updated-owner"));
            await store.ApplyCalled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.That(store.Applied).HasSingleItem();
            await Assert.That(store.Applied[0].Record!.Did).IsEqualTo("did:plc:updated-owner");
        }
        finally
        {
            cancellation.Cancel();
            await IgnoreCancellationAsync(run);
        }
    }

    [Test]
    public async Task RunSingleLease_ReconnectResumesFromSavedCursor()
    {
        const long savedCursor = 123_456;
        var store = new FakeRuntimeStore([Guid.CreateVersion7()], savedCursor);
        var source = new FakeEventSource([]) { WaitForCancellation = true };
        var options = new FakeOptionsMonitor(TestOptions([AllowedDid]));
        var time = new ManualTimeProvider(ObservedAt);
        using var subscriber = new AtprotoJetstreamSubscriber(
            store,
            source,
            options,
            time,
            NullLogger<AtprotoJetstreamSubscriber>.Instance);
        using var cancellation = new CancellationTokenSource();
        Task<bool> run = subscriber.RunSingleLeaseAsync(cancellation.Token);

        try
        {
            await source.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            options.Set(TestOptions(["did:plc:latest-desired"]));
            await time.WaitForTimerAsync(TimeSpan.FromMilliseconds(100)).WaitAsync(TimeSpan.FromSeconds(5));
            time.Advance(TimeSpan.FromMilliseconds(100));
            await source.WaitForSessionCountAsync(2).WaitAsync(TimeSpan.FromSeconds(5));

            await Assert.That(source.Subscriptions.Select(value => value.LiveCursor).ToArray())
                .IsEquivalentTo(new long?[] { savedCursor, savedCursor });
            await Assert.That(source.Subscriptions[1].Dids.Select(did => did.Value)).IsEquivalentTo(["did:plc:latest-desired"]);
            await Assert.That(store.Cursor).IsEqualTo(savedCursor);
        }
        finally
        {
            cancellation.Cancel();
            await IgnoreCancellationAsync(run);
        }
    }

    [Test]
    public async Task RunSingleLease_CursorTooOldReopensFromLiveTipWithoutRewindingPersistedCursor()
    {
        const long savedCursor = 123_456;
        var store = new FakeRuntimeStore([Guid.CreateVersion7()], savedCursor);
        var source = new FakeEventSource([])
        {
            WaitForCancellation = true,
            FirstSessionReadException = new JetstreamV2Exception(
                "cursor predates the sealed archive",
                JetstreamV2ErrorNames.CursorTooOld)
        };
        var time = new ManualTimeProvider(ObservedAt);
        using var subscriber = new AtprotoJetstreamSubscriber(
            store,
            source,
            new FakeOptionsMonitor(TestOptions([AllowedDid])),
            time,
            NullLogger<AtprotoJetstreamSubscriber>.Instance);
        using var cancellation = new CancellationTokenSource();
        Task<bool> run = subscriber.RunSingleLeaseAsync(cancellation.Token);

        try
        {
            await source.WaitForSessionCountAsync(2).WaitAsync(TimeSpan.FromSeconds(5));

            // The gap is left to the governed PDS recovery pump; the fence value must not be rewound.
            await Assert.That(source.Subscriptions[0].LiveCursor).IsEqualTo(savedCursor);
            await Assert.That(source.Subscriptions[1].LiveCursor).IsNull();
            await Assert.That(store.Cursor).IsEqualTo(savedCursor);
            await Assert.That(store.ClaimCount).IsEqualTo(1);

            source.Sessions[1].Push(EventEnvelope(savedCursor + 10));
            await store.ApplyCalled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.That(store.Applied[0].ExpectedCursor).IsEqualTo(savedCursor);
            await Assert.That(store.Cursor).IsEqualTo(savedCursor + 10);
        }
        finally
        {
            cancellation.Cancel();
            await IgnoreCancellationAsync(run);
        }
    }

    [Test]
    public async Task RunSingleLease_ReorderedDuplicateEquivalentDidsDoNotReconnect()
    {
        string secondDid = "did:plc:second-owner";
        var source = new FakeEventSource([]) { WaitForCancellation = true };
        var options = new FakeOptionsMonitor(TestOptions([AllowedDid, secondDid]));
        var time = new ManualTimeProvider(ObservedAt);
        using var subscriber = new AtprotoJetstreamSubscriber(
            new FakeRuntimeStore([Guid.CreateVersion7()]),
            source,
            options,
            time,
            NullLogger<AtprotoJetstreamSubscriber>.Instance);
        using var cancellation = new CancellationTokenSource();
        Task<bool> run = subscriber.RunSingleLeaseAsync(cancellation.Token);

        try
        {
            await source.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            AtprotoJetstreamOptions equivalent = TestOptions([secondDid, AllowedDid, AllowedDid]);
            equivalent.Endpoint = "https://ignored-runtime-change.example.test";
            equivalent.MaxMessageSizeBytes++;
            options.Set(equivalent);

            await Assert.That(source.SubscriptionCount).IsEqualTo(1);
            await Assert.That(source.Sessions[0].Disposed).IsFalse();
        }
        finally
        {
            cancellation.Cancel();
            await IgnoreCancellationAsync(run);
        }
    }

    [Test]
    public async Task RunSingleLease_FilterChangeStormCoalescesToOneReconnectWithLatestValue()
    {
        var source = new FakeEventSource([]) { WaitForCancellation = true };
        var options = new FakeOptionsMonitor(TestOptions([AllowedDid]));
        var time = new ManualTimeProvider(ObservedAt);
        using var subscriber = new AtprotoJetstreamSubscriber(
            new FakeRuntimeStore([Guid.CreateVersion7()]),
            source,
            options,
            time,
            NullLogger<AtprotoJetstreamSubscriber>.Instance);
        using var cancellation = new CancellationTokenSource();
        Task<bool> run = subscriber.RunSingleLeaseAsync(cancellation.Token);

        try
        {
            await source.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            options.Set(TestOptions(["did:plc:first-change"]));
            await time.WaitForTimerAsync(TimeSpan.FromMilliseconds(100)).WaitAsync(TimeSpan.FromSeconds(5));
            options.Set(TestOptions(["did:plc:second-change"]));
            options.Set(TestOptions(["did:plc:final-change"]));
            time.Advance(TimeSpan.FromMilliseconds(100));
            await source.WaitForSessionCountAsync(2).WaitAsync(TimeSpan.FromSeconds(5));

            await Assert.That(source.SubscriptionCount).IsEqualTo(2);
            await Assert.That(source.Subscriptions[1].Dids.Select(did => did.Value)).IsEquivalentTo(["did:plc:final-change"]);
        }
        finally
        {
            cancellation.Cancel();
            await IgnoreCancellationAsync(run);
        }
    }

    [Test]
    public async Task RunSingleLease_RecoveryRunsAlongsideLiveStreamUnderCurrentClaim()
    {
        var store = new FakeRuntimeStore([Guid.CreateVersion7()]);
        var source = new FakeEventSource([]) { WaitForCancellation = true };
        var time = new ManualTimeProvider(ObservedAt);
        var releaseRecovery = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        store.RecoveryHandler = async (_, cancellationToken) =>
        {
            await releaseRecovery.Task.WaitAsync(cancellationToken);
            return new AtprotoPdsRecoveryResult(AtprotoPdsRecoveryOutcome.Completed, new string('a', 64), 1);
        };
        using var subscriber = new AtprotoJetstreamSubscriber(
            store,
            source,
            new FakeOptionsMonitor(TestOptions([AllowedDid])),
            time,
            NullLogger<AtprotoJetstreamSubscriber>.Instance);
        using var cancellation = new CancellationTokenSource();
        Task<bool> run = subscriber.RunSingleLeaseAsync(cancellation.Token);

        try
        {
            await source.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await store.RecoveryCalled.Task.WaitAsync(TimeSpan.FromSeconds(5));

            source.Sessions[0].Push(EventEnvelope(100));
            await store.ApplyCalled.Task.WaitAsync(TimeSpan.FromSeconds(5));

            await Assert.That(store.Applied).HasSingleItem();
            await Assert.That(store.RecoveryCommands).HasSingleItem();
            await Assert.That(store.RecoveryCommands[0].Claim.LeaseToken)
                .IsEqualTo(store.LastClaim!.LeaseToken);
            await Assert.That(store.RecoveryCommands[0].AllowedDids).IsEquivalentTo([AllowedDid]);
            await Assert.That(store.RecoveryCommands[0].SnapshotStartedAt)
                .IsEqualTo(ObservedAt);
        }
        finally
        {
            releaseRecovery.TrySetResult();
            cancellation.Cancel();
            await IgnoreCancellationAsync(run);
        }
    }

    [Test]
    [Arguments(AtprotoPdsRecoveryOutcome.Disabled)]
    [Arguments(AtprotoPdsRecoveryOutcome.DowntimeOnly)]
    [Arguments(AtprotoPdsRecoveryOutcome.Unchanged)]
    [Arguments(AtprotoPdsRecoveryOutcome.Completed)]
    public async Task RunSingleLease_SuccessfulRecoveryMemoizesFingerprintAndUsesChangedDidScope(
        AtprotoPdsRecoveryOutcome outcome)
    {
        string fingerprint = new('b', 64);
        var store = new FakeRuntimeStore([Guid.CreateVersion7()])
        {
            RecoveryHandler = (_, _) => Task.FromResult(new AtprotoPdsRecoveryResult(outcome, fingerprint))
        };
        var source = new FakeEventSource([]) { WaitForCancellation = true };
        var options = new FakeOptionsMonitor(TestOptions([AllowedDid]));
        var time = new ManualTimeProvider(ObservedAt);
        using var subscriber = new AtprotoJetstreamSubscriber(
            store,
            source,
            options,
            time,
            NullLogger<AtprotoJetstreamSubscriber>.Instance);
        using var cancellation = new CancellationTokenSource();
        Task<bool> run = subscriber.RunSingleLeaseAsync(cancellation.Token);

        try
        {
            await store.WaitForRecoveryCountAsync(1).WaitAsync(TimeSpan.FromSeconds(5));
            await time.WaitForTimerAsync(TimeSpan.FromMilliseconds(100)).WaitAsync(TimeSpan.FromSeconds(5));
            options.Set(TestOptions(["did:plc:changed-recovery-owner"]));
            time.Advance(TimeSpan.FromMilliseconds(100));
            await store.WaitForRecoveryCountAsync(2).WaitAsync(TimeSpan.FromSeconds(5));

            await Assert.That(store.RecoveryCommands[1].LastCompletedFingerprint).IsEqualTo(fingerprint);
            await Assert.That(store.RecoveryCommands[1].AllowedDids)
                .IsEquivalentTo(["did:plc:changed-recovery-owner"]);
        }
        finally
        {
            cancellation.Cancel();
            await IgnoreCancellationAsync(run);
        }
    }

    [Test]
    public async Task RunSingleLease_RecoveryBackoffIncreasesCapsAndResetsWithoutStoppingLiveIngestion()
    {
        int attempt = 0;
        var store = new FakeRuntimeStore([Guid.CreateVersion7()]);
        store.RecoveryHandler = async (_, cancellationToken) => ++attempt switch
        {
            1 => new AtprotoPdsRecoveryResult(
                AtprotoPdsRecoveryOutcome.ScopeRejected,
                new string('c', 64),
                FailedDids: 1),
            2 => new AtprotoPdsRecoveryResult(
                AtprotoPdsRecoveryOutcome.PartialFailure,
                new string('d', 64),
                AppliedDids: 1,
                FailedDids: 1),
            3 => throw new HttpRequestException("simulated_network_failure"),
            4 => new AtprotoPdsRecoveryResult(
                AtprotoPdsRecoveryOutcome.PartialFailure,
                new string('e', 64),
                FailedDids: 1),
            5 => new AtprotoPdsRecoveryResult(
                AtprotoPdsRecoveryOutcome.Completed,
                new string('f', 64),
                AppliedDids: 1),
            _ => await WaitForCancellationAsync(cancellationToken)
        };
        var source = new FakeEventSource([]) { WaitForCancellation = true };
        var time = new ManualTimeProvider(ObservedAt);
        AtprotoJetstreamOptions options = TestOptions([AllowedDid]);
        options.RetryMinimumMilliseconds = 100;
        options.RetryMaximumMilliseconds = 400;
        using var subscriber = new AtprotoJetstreamSubscriber(
            store,
            source,
            new FakeOptionsMonitor(options),
            time,
            NullLogger<AtprotoJetstreamSubscriber>.Instance);
        using var cancellation = new CancellationTokenSource();
        Task<bool> run = subscriber.RunSingleLeaseAsync(cancellation.Token);

        try
        {
            await store.WaitForRecoveryCountAsync(1).WaitAsync(TimeSpan.FromSeconds(5));
            await time.WaitForTimerCountAsync(2).WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.That(time.CreatedDueTimes[^1]).IsEqualTo(TimeSpan.FromMilliseconds(100));
            time.Advance(TimeSpan.FromMilliseconds(100));

            await store.WaitForRecoveryCountAsync(2).WaitAsync(TimeSpan.FromSeconds(5));
            await time.WaitForTimerCountAsync(3).WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.That(time.CreatedDueTimes[^1]).IsEqualTo(TimeSpan.FromMilliseconds(200));

            source.Sessions[0].Push(EventEnvelope(100));
            await store.ApplyCalled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            time.Advance(TimeSpan.FromMilliseconds(200));

            await store.WaitForRecoveryCountAsync(3).WaitAsync(TimeSpan.FromSeconds(5));
            await time.WaitForTimerCountAsync(4).WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.That(time.CreatedDueTimes[^1]).IsEqualTo(TimeSpan.FromMilliseconds(400));
            time.Advance(TimeSpan.FromMilliseconds(400));

            await store.WaitForRecoveryCountAsync(4).WaitAsync(TimeSpan.FromSeconds(5));
            await time.WaitForTimerCountAsync(5).WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.That(time.CreatedDueTimes[^1]).IsEqualTo(TimeSpan.FromMilliseconds(400));
            time.Advance(TimeSpan.FromMilliseconds(400));

            await store.WaitForRecoveryCountAsync(5).WaitAsync(TimeSpan.FromSeconds(5));
            await time.WaitForTimerCountAsync(6).WaitAsync(TimeSpan.FromSeconds(5));

            await Assert.That(time.CreatedDueTimes[^1]).IsEqualTo(TimeSpan.FromMilliseconds(100));
            await Assert.That(store.RecoveryCommands.Take(5).All(command => command.LastCompletedFingerprint is null))
                .IsTrue();
            await Assert.That(store.Applied).HasSingleItem();
            await Assert.That(source.SubscriptionCount).IsEqualTo(1);
        }
        finally
        {
            cancellation.Cancel();
            await IgnoreCancellationAsync(run);
        }
    }

    [Test]
    public async Task Execute_FenceRejectedReacquiresBeforeFurtherRecovery()
    {
        int attempt = 0;
        var store = new FakeRuntimeStore([Guid.CreateVersion7()]);
        store.RecoveryHandler = async (_, cancellationToken) =>
        {
            if (Interlocked.Increment(ref attempt) == 1)
            {
                return new AtprotoPdsRecoveryResult(
                    AtprotoPdsRecoveryOutcome.FenceRejected,
                    new string('a', 64),
                    FailedDids: 1);
            }

            return await WaitForCancellationAsync(cancellationToken);
        };
        var source = new FakeEventSource([]) { WaitForCancellation = true };
        var time = new ManualTimeProvider(ObservedAt);
        using var subscriber = new AtprotoJetstreamSubscriber(
            store,
            source,
            new FakeOptionsMonitor(TestOptions([AllowedDid])),
            time,
            NullLogger<AtprotoJetstreamSubscriber>.Instance);

        await subscriber.StartAsync(CancellationToken.None);
        try
        {
            await store.WaitForRecoveryCountAsync(1).WaitAsync(TimeSpan.FromSeconds(5));
            await time.WaitForTimerAsync(TimeSpan.FromMilliseconds(100)).WaitAsync(TimeSpan.FromSeconds(5));
            time.Advance(TimeSpan.FromMilliseconds(100));
            await store.WaitForRecoveryCountAsync(2).WaitAsync(TimeSpan.FromSeconds(5));
            await source.WaitForSessionCountAsync(2).WaitAsync(TimeSpan.FromSeconds(5));

            await Assert.That(store.RecoveryCommands[1].Claim.LeaseToken)
                .IsNotEqualTo(store.RecoveryCommands[0].Claim.LeaseToken);
            await Assert.That(store.RecoveryCommands[1].Claim.LeaseFence)
                .IsGreaterThan(store.RecoveryCommands[0].Claim.LeaseFence);
            await Assert.That(source.SubscriptionCount).IsEqualTo(2);
            await Assert.That(source.Sessions[0].Disposed).IsTrue();
        }
        finally
        {
            await subscriber.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task EventSource_SubscribeOptionsRequestExactKindsAndCollections()
    {
        var subscription = new AtprotoJetstreamSubscription(
            new Uri("https://jetstream.example.test"),
            AtprotoJetstreamConstants.Collections,
            Dids: [],
            LiveCursor: 42,
            MaxMessageSizeBytes: 2_113_536);

        JetstreamV2SubscribeOptions options = CarpaNetJetstreamEventSource.CreateSubscribeOptions(subscription);

        // Account is requested for the deletion purge signal; Identity and Sync change nothing we present.
        await Assert.That(options.Kinds)
            .IsEquivalentTo([JetstreamV2EventKind.Commit, JetstreamV2EventKind.Account]);
        await Assert.That(options.Kinds).DoesNotContain(JetstreamV2EventKind.Identity);
        await Assert.That(options.Kinds).DoesNotContain(JetstreamV2EventKind.Sync);
        await Assert.That(options.Collections)
            .IsEquivalentTo([AtprotoJetstreamConstants.EventCollection, AtprotoJetstreamConstants.RsvpCollection]);
        await Assert.That(options.Collections).DoesNotContain(collection => collection.Contains('*'));
        await Assert.That(options.Dids).IsEmpty();
        await Assert.That(options.LiveCursor).IsEqualTo(42);
        await Assert.That(options.MaxMessageSizeBytes).IsEqualTo(2_113_536);
        // Live tail only: archive replay would bypass the per-tenant backfill policy.
        await Assert.That(options.AfterSeq).IsNull();
        await Assert.That(options.SnapshotOnly).IsFalse();
    }

    [Test]
    public async Task RunSingleLease_RenewalExceptionCancelsActiveStream()
    {
        var store = new FakeRuntimeStore([Guid.CreateVersion7()])
        {
            RenewalException = new InvalidOperationException("simulated_renewal_failure")
        };
        var source = new FakeEventSource([]) { WaitForCancellation = true };
        var configuredOptions = TestOptions([AllowedDid]);
        configuredOptions.LeaseRenewalSeconds = 5;
        var time = new ManualTimeProvider(ObservedAt);
        using var subscriber = new AtprotoJetstreamSubscriber(
            store,
            source,
            new FakeOptionsMonitor(configuredOptions),
            time,
            NullLogger<AtprotoJetstreamSubscriber>.Instance);

        Task<bool> run = subscriber.RunSingleLeaseAsync(CancellationToken.None);
        await source.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await time.WaitForTimerAsync(TimeSpan.FromSeconds(5)).WaitAsync(TimeSpan.FromSeconds(5));
        time.Advance(TimeSpan.FromSeconds(5));

        await Assert.ThrowsAsync<InvalidOperationException>(() => run);
        await Assert.That(source.CancellationObserved).IsTrue();
    }

    [Test]
    public async Task Parser_InvalidCidIsQuarantinedWithoutThrowing()
    {
        JetstreamV2Event envelope = EventEnvelope(101);
        envelope.Commit!.Cid = "not-a-cid";

        AtprotoJetstreamParsedEnvelope outcome = AtprotoJetstreamEnvelopeParser.Parse(
            envelope,
            100,
            [AllowedDid],
            ObservedAt);

        await Assert.That(outcome.Record).IsNull();
        await Assert.That(outcome.Quarantine!.ReasonCode).IsEqualTo("invalid_record_cid");
    }

    [Test]
    public async Task Parser_EmptyDidFilterAcceptsOtherwiseValidPublicRecord()
    {
        AtprotoJetstreamParsedEnvelope outcome = AtprotoJetstreamEnvelopeParser.Parse(
            EventEnvelope(101),
            100,
            [],
            ObservedAt);

        await Assert.That(outcome.Record).IsNotNull();
        await Assert.That(outcome.Quarantine).IsNull();
    }

    [Test]
    [Arguments(JetstreamV2EventKind.Identity)]
    [Arguments(JetstreamV2EventKind.Account)]
    [Arguments(JetstreamV2EventKind.Sync)]
    public async Task Parser_NonCommitKindsAreIgnoredWithoutQuarantineEvidence(JetstreamV2EventKind kind)
    {
        // Collection filters never suppress these, so quarantining them would fill the table with the
        // whole network's identity churn.
        var envelope = new JetstreamV2Event
        {
            Did = AllowedDid,
            Seq = 101,
            TimeUs = BaseTimeUs + 101,
            Kind = kind
        };

        AtprotoJetstreamParsedEnvelope outcome = AtprotoJetstreamEnvelopeParser.Parse(
            envelope,
            100,
            [AllowedDid],
            ObservedAt);

        await Assert.That(outcome.Ignored).IsTrue();
        await Assert.That(outcome.Record).IsNull();
        await Assert.That(outcome.Quarantine).IsNull();
    }

    [Test]
    public async Task RunSingleLease_NonCommitKindsDoNotReachTheStoreOrMoveTheCursor()
    {
        var store = new FakeRuntimeStore([Guid.CreateVersion7()]);
        var source = new FakeEventSource(
        [
            AccountEnvelope(50, active: true, status: "active"),
            EventEnvelope(100)
        ]);
        using var subscriber = CreateSubscriber(store, source);

        bool consumed = await subscriber.RunSingleLeaseAsync(CancellationToken.None);

        await Assert.That(consumed).IsTrue();
        await Assert.That(store.Applied).HasSingleItem();
        await Assert.That(store.Applied[0].ExpectedCursor).IsEqualTo(0);
        await Assert.That(store.Cursor).IsEqualTo(100);
    }

    [Test]
    public async Task RunSingleLease_DeactivatedAccountPurgesThroughTheFencedStoreAndAdvancesCursor()
    {
        var store = new FakeRuntimeStore([Guid.CreateVersion7()]);
        var source = new FakeEventSource([AccountEnvelope(100, active: false, status: "deleted")]);
        using var subscriber = CreateSubscriber(store, source);

        bool consumed = await subscriber.RunSingleLeaseAsync(CancellationToken.None);

        await Assert.That(consumed).IsTrue();
        await Assert.That(store.Applied).HasSingleItem();
        AtprotoJetstreamApplyRequest applied = store.Applied[0];
        await Assert.That(applied.AccountPurge!.Did).IsEqualTo(AllowedDid);
        await Assert.That(applied.AccountPurge.Status).IsEqualTo("deleted");
        await Assert.That(applied.AccountPurge.SourceVersion).IsEqualTo(BaseTimeUs + 100);
        // Exactly one effect per envelope: a purge carries no record and no quarantine evidence.
        await Assert.That(applied.Record).IsNull();
        await Assert.That(applied.Quarantine).IsNull();
        await Assert.That(applied.Presentations).IsEmpty();
        await Assert.That(store.Cursor).IsEqualTo(100);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task Parser_AccountEventIsPurgeOnlyWhenDeactivated(bool active)
    {
        AtprotoJetstreamParsedEnvelope outcome = AtprotoJetstreamEnvelopeParser.Parse(
            AccountEnvelope(101, active, active ? "active" : "deactivated"),
            100,
            [AllowedDid],
            ObservedAt);

        await Assert.That(outcome.AccountPurge is null).IsEqualTo(active);
        await Assert.That(outcome.Ignored).IsEqualTo(active);
        await Assert.That(outcome.Quarantine).IsNull();
    }

    [Test]
    public async Task Parser_DeactivatedAccountOutsideCuratedAllowlistIsIgnored()
    {
        JetstreamV2Event envelope = AccountEnvelope(101, active: false, status: "deleted");
        envelope.Did = "did:plc:someone-else";

        AtprotoJetstreamParsedEnvelope outcome = AtprotoJetstreamEnvelopeParser.Parse(
            envelope,
            100,
            [AllowedDid],
            ObservedAt);

        await Assert.That(outcome.Ignored).IsTrue();
        await Assert.That(outcome.AccountPurge).IsNull();
    }

    [Test]
    public async Task Parser_DeactivatedAccountInPublicModeIsPurgedForAnyDid()
    {
        // With no curated allowlist any deleted account may own records we ingested publicly.
        JetstreamV2Event envelope = AccountEnvelope(101, active: false, status: "deleted");
        envelope.Did = "did:plc:someone-else";

        AtprotoJetstreamParsedEnvelope outcome = AtprotoJetstreamEnvelopeParser.Parse(
            envelope,
            100,
            [],
            ObservedAt);

        await Assert.That(outcome.AccountPurge!.Did).IsEqualTo("did:plc:someone-else");
    }

    [Test]
    public async Task Parser_MalformedDidOnAccountEventIsIgnoredNotPurged()
    {
        JetstreamV2Event envelope = AccountEnvelope(101, active: false, status: "deleted");
        envelope.Did = "not-a-did";

        AtprotoJetstreamParsedEnvelope outcome = AtprotoJetstreamEnvelopeParser.Parse(
            envelope,
            100,
            [],
            ObservedAt);

        await Assert.That(outcome.Ignored).IsTrue();
        await Assert.That(outcome.AccountPurge).IsNull();
    }

    [Test]
    [Arguments(JetstreamV2EventKind.Identity)]
    [Arguments(JetstreamV2EventKind.Sync)]
    public async Task Parser_IdentityAndSyncNeverPurge(JetstreamV2EventKind kind)
    {
        var envelope = new JetstreamV2Event
        {
            Did = AllowedDid,
            Seq = 101,
            TimeUs = BaseTimeUs + 101,
            Kind = kind,
            Account = new JetstreamV2Account { Did = AllowedDid, Active = false, Status = "deleted" }
        };

        AtprotoJetstreamParsedEnvelope outcome = AtprotoJetstreamEnvelopeParser.Parse(
            envelope,
            100,
            [AllowedDid],
            ObservedAt);

        await Assert.That(outcome.Ignored).IsTrue();
        await Assert.That(outcome.AccountPurge).IsNull();
    }

    [Test]
    public async Task Parser_QuarantinesWrongCollectionTypeSizeAndShapeWithoutRawPayload()
    {
        string oversized = new('x', AtprotoRecordSizeValidator.MaximumJsonBytes + 1);
        JetstreamV2Event[] invalid =
        [
            EventEnvelope(101, collection: "app.bsky.feed.post"),
            EventEnvelope(102, type: AtprotoJetstreamConstants.RsvpCollection),
            EventEnvelope(103, json: JsonSerializer.Serialize(new { type = AtprotoJetstreamConstants.EventCollection, name = oversized, createdAt = "2026-07-19T10:00:00Z" }).Replace("\"type\"", "\"$type\"", StringComparison.Ordinal)),
            EventEnvelope(104, json: "{\"$type\":\"community.lexicon.calendar.event\",\"createdAt\":\"2026-07-19T10:00:00Z\"}"),
            EventEnvelope(105)
        ];

        AtprotoJetstreamParsedEnvelope[] outcomes = invalid
            .Select((value, index) => AtprotoJetstreamEnvelopeParser.Parse(
                value,
                100,
                index == invalid.Length - 1 ? ["did:plc:another-owner"] : [AllowedDid],
                ObservedAt))
            .ToArray();

        await Assert.That(outcomes.All(outcome => outcome.Quarantine is not null)).IsTrue();
        await Assert.That(outcomes.Select(outcome => outcome.Quarantine!.ReasonCode))
            .IsEquivalentTo(["collection_not_allowed", "record_type_mismatch", "record_too_large", "invalid_record_shape", "did_not_allowed"]);
        await Assert.That(outcomes.All(outcome => outcome.Quarantine!.EnvelopeHash.Length == 64)).IsTrue();
        await Assert.That(outcomes[3].EventProjectionInvalidation).IsNotNull();
        await Assert.That(outcomes[3].EventProjectionInvalidation!.SourceVersion).IsEqualTo(BaseTimeUs + 104);
    }

    [Test]
    public async Task ParserMaterializesBoundedFieldsAndSelectsFirstSafeHttpsSource()
    {
        string json = """
            {
              "$type":"community.lexicon.calendar.event",
              "name":"Remote gathering",
              "description":"Public description",
              "createdAt":"2026-07-19T10:00:00Z",
              "startsAt":"2026-07-20T10:00:00Z",
              "endsAt":"2026-07-20T11:00:00Z",
              "mode":"community.lexicon.calendar.event#virtual",
              "status":"community.lexicon.calendar.event#scheduled",
              "rsvpExpected":true,
              "uris":[
                {"uri":"http://unsafe.example/event"},
                {"uri":"https://events.example/event"}
              ]
            }
            """;

        AtprotoJetstreamParsedEnvelope outcome = AtprotoJetstreamEnvelopeParser.Parse(
            EventEnvelope(106, json: json),
            105,
            [AllowedDid],
            ObservedAt);

        await Assert.That(outcome.Quarantine).IsNull();
        await Assert.That(outcome.EventProjection!.Name).IsEqualTo("Remote gathering");
        await Assert.That(outcome.EventProjection.Mode).IsEqualTo("virtual");
        await Assert.That(outcome.EventProjection.Status).IsEqualTo("scheduled");
        await Assert.That(outcome.EventProjection.RsvpExpected).IsTrue();
        await Assert.That(outcome.EventProjection.SourceUrl).IsEqualTo("https://events.example/event");
        await Assert.That(outcome.EventProjection.SourceVersion).IsEqualTo(BaseTimeUs + 106);
        await Assert.That(outcome.Cursor).IsEqualTo(106);
    }

    [Test]
    public async Task Parser_AcceptsRsvpAndBuildsEventTombstoneWithDependencyIdentity()
    {
        string cid = ATCid.FromSha256Hash(new byte[32]).Value;
        string subject = "at://did:plc:event-owner/community.lexicon.calendar.event/3m-event";
        JetstreamV2Event rsvp = EventEnvelope(
            101,
            AtprotoJetstreamConstants.RsvpCollection,
            AtprotoJetstreamConstants.RsvpCollection,
            $$"""{"$type":"community.lexicon.calendar.rsvp","subject":{"uri":"{{subject}}","cid":"{{cid}}"},"status":"community.lexicon.calendar.rsvp#interested"}""");
        JetstreamV2Event tombstone = EventEnvelope(
            102,
            operation: JetstreamV2CommitOperation.Delete,
            record: null);

        AtprotoJetstreamParsedEnvelope rsvpOutcome = AtprotoJetstreamEnvelopeParser.Parse(rsvp, 100, [AllowedDid], ObservedAt);
        AtprotoJetstreamParsedEnvelope tombstoneOutcome = AtprotoJetstreamEnvelopeParser.Parse(tombstone, 101, [AllowedDid], ObservedAt);

        await Assert.That(rsvpOutcome.Record!.SubjectUri).IsEqualTo(subject);
        await Assert.That(rsvpOutcome.Record.SubjectCid).IsEqualTo(cid);
        await Assert.That(tombstoneOutcome.Record!.TombstonedAt).IsEqualTo(ObservedAt);
        await Assert.That(tombstoneOutcome.Record.RecordJson).IsNull();
    }

    [Test]
    public async Task RunSingleLease_PublishesConnectedLivenessWhileStreamingAndClearsItOnTeardown()
    {
        var store = new FakeRuntimeStore([Guid.CreateVersion7()]);
        var source = new FakeEventSource([]) { WaitForCancellation = true };
        var liveness = new AtprotoJetstreamLiveness();
        var time = new ManualTimeProvider(ObservedAt);
        using var subscriber = new AtprotoJetstreamSubscriber(
            store,
            source,
            new FakeOptionsMonitor(TestOptions([AllowedDid])),
            time,
            NullLogger<AtprotoJetstreamSubscriber>.Instance,
            liveness);
        using var cancellation = new CancellationTokenSource();

        await Assert.That(liveness.Read().IsConnected).IsFalse();
        Task<bool> run = subscriber.RunSingleLeaseAsync(cancellation.Token);
        try
        {
            await source.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            source.Sessions[0].Push(EventEnvelope(100));
            await store.ApplyCalled.Task.WaitAsync(TimeSpan.FromSeconds(5));

            AtprotoJetstreamLivenessSnapshot connected = liveness.Read();
            await Assert.That(connected.IsConnected).IsTrue();
            await Assert.That(connected.ConnectedSince).IsEqualTo(ObservedAt);
        }
        finally
        {
            cancellation.Cancel();
            await IgnoreCancellationAsync(run);
        }

        AtprotoJetstreamLivenessSnapshot after = liveness.Read();
        await Assert.That(after.IsConnected).IsFalse();
        await Assert.That(after.DisconnectedSince).IsEqualTo(ObservedAt);
        await Assert.That(after.Cursor).IsEqualTo(100);
    }

    [Test]
    public async Task RunSingleLease_PropagatesCancellationToStream()
    {
        var store = new FakeRuntimeStore([Guid.CreateVersion7()]);
        var source = new FakeEventSource([]) { WaitForCancellation = true };
        using var subscriber = CreateSubscriber(store, source);
        using var cancellation = new CancellationTokenSource();

        Task<bool> run = subscriber.RunSingleLeaseAsync(cancellation.Token);
        await source.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => run);
        await Assert.That(source.CancellationObserved).IsTrue();
        await Assert.That(source.Sessions[0].Disposed).IsTrue();
    }

    [Test]
    public async Task RunSingleLease_ReplayedSeqBelowCursorIsSkippedWithoutStoreRoundTrip()
    {
        // The v2 cursor is inclusive and delivery is at-least-once, so the reconnect overlap is normal.
        var store = new FakeRuntimeStore([Guid.CreateVersion7()], initialCursor: 100);
        var source = new FakeEventSource([EventEnvelope(100), EventEnvelope(101)]);
        using var subscriber = CreateSubscriber(store, source);

        bool consumed = await subscriber.RunSingleLeaseAsync(CancellationToken.None);

        await Assert.That(consumed).IsTrue();
        await Assert.That(store.Applied).HasSingleItem();
        await Assert.That(store.Applied[0].NextCursor).IsEqualTo(101);
        await Assert.That(store.Cursor).IsEqualTo(101);
    }

    [Test]
    public async Task RunSingleLease_OutOfRangeSourceTimestampIsQuarantinedWithoutBlockingNextLegitimateEnvelope()
    {
        var store = new FakeRuntimeStore([Guid.CreateVersion7()]);
        var source = new FakeEventSource(
        [
            EventEnvelope(99, timeUs: long.MaxValue),
            EventEnvelope(100)
        ]);
        using var subscriber = CreateSubscriber(store, source);

        bool consumed = await subscriber.RunSingleLeaseAsync(CancellationToken.None);

        await Assert.That(consumed).IsTrue();
        await Assert.That(store.Cursor).IsEqualTo(100);
        await Assert.That(store.Applied).Count().IsEqualTo(2);
        await Assert.That(store.Applied[0].Quarantine!.ReasonCode).IsEqualTo("invalid_source_timestamp");
        await Assert.That(store.Applied[1].Record).IsNotNull();
    }

    [Test]
    public async Task Parser_NonPositiveSeqIsQuarantinedWithoutAdvancingTheCursor()
    {
        AtprotoJetstreamParsedEnvelope outcome = AtprotoJetstreamEnvelopeParser.Parse(
            EventEnvelope(0),
            0,
            [AllowedDid],
            ObservedAt);

        await Assert.That(outcome.Quarantine!.ReasonCode).IsEqualTo("invalid_cursor");
        await Assert.That(outcome.AdvanceCursor).IsFalse();
    }

    [Test]
    public async Task OptionsValidator_RejectsMutableOrUnboundedTransportConfiguration()
    {
        var validator = new AtprotoJetstreamOptionsValidator();
        var invalid = new AtprotoJetstreamOptions
        {
            Endpoint = "http://user:secret@jetstream.example.test/path?cursor=1",
            MaxMessageSizeBytes = int.MaxValue,
            LeaseDurationSeconds = 10,
            LeaseRenewalSeconds = 10,
            AllowedDids = ["not-a-did"]
        };

        ValidateOptionsResult result = validator.Validate(null, invalid);

        await Assert.That(result.Failed).IsTrue();
        await Assert.That(result.Failures).Count().IsGreaterThanOrEqualTo(4);
    }

    [Test]
    public async Task OptionsValidator_AllowsEmptyPublicDidFilter()
    {
        var validator = new AtprotoJetstreamOptionsValidator();

        ValidateOptionsResult result = validator.Validate(null, new AtprotoJetstreamOptions());

        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task OptionsValidator_DefaultEndpointTargetsTheVersionlessV2Host()
    {
        var options = new AtprotoJetstreamOptions();

        await Assert.That(options.Endpoint).IsEqualTo("https://jetstream.us-east.bsky.network");
        await Assert.That(options.EnableCompression).IsFalse();
    }

    [Test]
    public async Task OptionsValidator_EnforcesDidCountAndShapeBoundaries()
    {
        var validator = new AtprotoJetstreamOptionsValidator();
        string[] maximum = Enumerable.Range(0, 10_000).Select(index => $"did:plc:owner-{index}").ToArray();

        ValidateOptionsResult maximumResult = validator.Validate(
            null,
            new AtprotoJetstreamOptions { AllowedDids = maximum });
        ValidateOptionsResult oversizedResult = validator.Validate(
            null,
            new AtprotoJetstreamOptions { AllowedDids = [.. maximum, "did:plc:one-too-many"] });
        ValidateOptionsResult malformedResult = validator.Validate(
            null,
            new AtprotoJetstreamOptions { AllowedDids = ["not-a-did"] });
        ValidateOptionsResult duplicatesResult = validator.Validate(
            null,
            new AtprotoJetstreamOptions { AllowedDids = [AllowedDid, AllowedDid] });

        await Assert.That(maximumResult.Succeeded).IsTrue();
        await Assert.That(oversizedResult.Failed).IsTrue();
        await Assert.That(malformedResult.Failed).IsTrue();
        await Assert.That(duplicatesResult.Succeeded).IsTrue();
    }

    private static AtprotoJetstreamSubscriber CreateSubscriber(
        IAtprotoJetstreamRuntimeStore store,
        IAtprotoJetstreamEventSource source,
        int leaseRenewalSeconds = 10,
        string[]? allowedDids = null)
        => new(
            store,
            source,
            new FakeOptionsMonitor(new AtprotoJetstreamOptions
            {
                Endpoint = "https://jetstream.example.test",
                LeaseDurationSeconds = 30,
                LeaseRenewalSeconds = leaseRenewalSeconds,
                RetryMinimumMilliseconds = 10,
                RetryMaximumMilliseconds = 100,
                AllowedDids = allowedDids ?? [AllowedDid]
            }),
            TimeProvider.System,
            NullLogger<AtprotoJetstreamSubscriber>.Instance);

    private static AtprotoJetstreamOptions TestOptions(string[] allowedDids) => new()
    {
        Endpoint = "https://jetstream.example.test",
        LeaseDurationSeconds = 30,
        LeaseRenewalSeconds = 10,
        CapabilityPollMilliseconds = 100,
        RetryMinimumMilliseconds = 10,
        RetryMaximumMilliseconds = 100,
        AllowedDids = allowedDids
    };

    private static async Task IgnoreCancellationAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static async Task<AtprotoPdsRecoveryResult> WaitForCancellationAsync(
        CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return new AtprotoPdsRecoveryResult(
            AtprotoPdsRecoveryOutcome.DowntimeOnly,
            new string('0', 64));
    }

    private static JetstreamV2Event AccountEnvelope(long seq, bool active, string status) => new()
    {
        Did = AllowedDid,
        Seq = seq,
        TimeUs = BaseTimeUs + seq,
        Kind = JetstreamV2EventKind.Account,
        Account = new JetstreamV2Account
        {
            Did = AllowedDid,
            Active = active,
            Status = status,
            Seq = seq
        }
    };

    private static JetstreamV2Event EventEnvelope(
        long seq,
        string collection = AtprotoJetstreamConstants.EventCollection,
        string? type = AtprotoJetstreamConstants.EventCollection,
        string? json = null,
        JetstreamV2CommitOperation operation = JetstreamV2CommitOperation.Create,
        JsonElement? record = default,
        string did = "did:plc:remote-owner",
        long? timeUs = null)
    {
        JsonElement? payload = operation == JetstreamV2CommitOperation.Delete
            ? record
            : record ?? JsonDocument.Parse(json ?? $$"""{"$type":"{{type}}","name":"Remote event","createdAt":"2026-07-19T10:00:00Z"}""").RootElement.Clone();
        return new JetstreamV2Event
        {
            Did = did,
            Seq = seq,
            TimeUs = timeUs ?? BaseTimeUs + seq,
            Kind = JetstreamV2EventKind.Commit,
            Commit = new JetstreamV2Commit
            {
                Operation = operation,
                Collection = collection,
                Rkey = "3m-remote",
                Cid = operation == JetstreamV2CommitOperation.Delete ? null : ATCid.FromSha256Hash(new byte[32]).Value,
                Record = payload
            }
        };
    }

    private sealed class FakeEventSource(IReadOnlyList<JetstreamV2Event> events) : IAtprotoJetstreamEventSource
    {
        private readonly object _lock = new();
        private readonly List<(int Count, TaskCompletionSource Completion)> _sessionWaiters = [];
        public AtprotoJetstreamSubscription? Subscription { get; private set; }
        public List<AtprotoJetstreamSubscription> Subscriptions { get; } = [];
        public List<FakeSession> Sessions { get; } = [];
        public bool WaitForCancellation { get; init; }
        public bool CancellationObserved { get; private set; }
        public Exception? FirstSessionReadException { get; init; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Action? BeforeYield { get; init; }
        public int FailuresRemaining { get; set; }
        public int SubscriptionCount { get; private set; }

        public Task<IAtprotoJetstreamSession> OpenSessionAsync(
            AtprotoJetstreamSubscription subscription,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_lock)
            {
                Subscription = subscription;
                Subscriptions.Add(subscription);
                SubscriptionCount++;
                if (FailuresRemaining > 0)
                {
                    FailuresRemaining--;
                    throw new InvalidOperationException("simulated_stream_failure");
                }

                var session = new FakeSession(this, events)
                {
                    ReadException = Sessions.Count == 0 ? FirstSessionReadException : null
                };
                Sessions.Add(session);
                Started.TrySetResult();
                foreach ((int count, TaskCompletionSource completion) in _sessionWaiters.Where(value => value.Count <= Sessions.Count))
                {
                    completion.TrySetResult();
                }

                return Task.FromResult<IAtprotoJetstreamSession>(session);
            }
        }

        public Task WaitForSessionCountAsync(int count)
        {
            lock (_lock)
            {
                if (Sessions.Count >= count)
                {
                    return Task.CompletedTask;
                }

                var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _sessionWaiters.Add((count, completion));
                return completion.Task;
            }
        }

        public sealed class FakeSession(
            FakeEventSource source,
            IReadOnlyList<JetstreamV2Event> events) : IAtprotoJetstreamSession
        {
            private readonly Channel<JetstreamV2Event> _pushedEvents = Channel.CreateUnbounded<JetstreamV2Event>();
            public Exception? ReadException { get; init; }
            public bool Disposed { get; private set; }

            public async IAsyncEnumerable<JetstreamV2Event> ReadEventsAsync(
                [EnumeratorCancellation] CancellationToken cancellationToken)
            {
                foreach (JetstreamV2Event value in events)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    source.BeforeYield?.Invoke();
                    yield return value;
                }

                if (ReadException is not null)
                {
                    throw ReadException;
                }

                if (source.WaitForCancellation)
                {
                    using CancellationTokenRegistration registration = cancellationToken.Register(
                        () => source.CancellationObserved = true);
                    try
                    {
                        await foreach (JetstreamV2Event value in _pushedEvents.Reader.ReadAllAsync(cancellationToken))
                        {
                            yield return value;
                        }
                    }
                    finally
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            source.CancellationObserved = true;
                        }
                    }
                }
            }

            public void Push(JetstreamV2Event value) => _pushedEvents.Writer.TryWrite(value);

            public ValueTask DisposeAsync()
            {
                Disposed = true;
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class FakeRuntimeStore(
        IReadOnlyList<Guid> enabledTenants,
        long initialCursor = 0) : IAtprotoJetstreamRuntimeStore
    {
        private readonly Guid _stateId = Guid.CreateVersion7();
        private readonly object _recoveryLock = new();
        private readonly List<(int Count, TaskCompletionSource Completion)> _recoveryWaiters = [];
        private long _nextFence = initialCursor;
        public List<AtprotoJetstreamApplyRequest> Applied { get; } = [];
        public TaskCompletionSource ApplyCalled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool FailNextApply { get; set; }
        public bool ThrowAfterNextApply { get; set; }
        public Exception? RenewalException { get; set; }
        public Func<ReconcileAtprotoPdsSnapshotsCommand, CancellationToken, Task<AtprotoPdsRecoveryResult>>?
            RecoveryHandler
        { get; set; }
        public long Cursor { get; private set; } = initialCursor;
        public int ClaimCount { get; private set; }
        public IReadOnlyList<Guid> EnabledTenants { get; set; } = enabledTenants;
        public AtprotoJetstreamClaim? LastClaim { get; private set; }
        public List<ReconcileAtprotoPdsSnapshotsCommand> RecoveryCommands { get; } = [];
        public TaskCompletionSource RecoveryCalled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IReadOnlyList<Guid>> ResolveEnabledTenantIdsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(EnabledTenants);

        public Task<AtprotoJetstreamClaim?> TryClaimAsync(
            string service,
            string owner,
            DateTime claimedAt,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken)
        {
            ClaimCount++;
            LastClaim = new(
                _stateId,
                service,
                Cursor,
                Guid.CreateVersion7(),
                Interlocked.Increment(ref _nextFence));
            return Task.FromResult(LastClaim);
        }

        public Task<AtprotoPdsRecoveryResult> ReconcilePdsSnapshotsAsync(
            ReconcileAtprotoPdsSnapshotsCommand command,
            CancellationToken cancellationToken)
        {
            lock (_recoveryLock)
            {
                RecoveryCommands.Add(command);
                RecoveryCalled.TrySetResult();
                foreach ((int count, TaskCompletionSource completion) in
                    _recoveryWaiters.Where(value => value.Count <= RecoveryCommands.Count))
                {
                    completion.TrySetResult();
                }
            }

            return RecoveryHandler?.Invoke(command, cancellationToken)
                ?? WaitForCancellationAsync(cancellationToken);
        }

        private static async Task<AtprotoPdsRecoveryResult> WaitForCancellationAsync(
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new AtprotoPdsRecoveryResult(
                AtprotoPdsRecoveryOutcome.DowntimeOnly,
                new string('0', 64));
        }

        public Task WaitForRecoveryCountAsync(int count)
        {
            lock (_recoveryLock)
            {
                if (RecoveryCommands.Count >= count)
                {
                    return Task.CompletedTask;
                }

                var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _recoveryWaiters.Add((count, completion));
                return completion.Task;
            }
        }

        public Task<bool> TryRenewAsync(
            AtprotoJetstreamClaim claim,
            DateTime observedAt,
            DateTime leaseExpiresAt,
            CancellationToken cancellationToken) => RenewalException is null
                ? Task.FromResult(true)
                : Task.FromException<bool>(RenewalException);

        public Task<bool> TryApplyAndAdvanceAsync(
            AtprotoJetstreamApplyRequest request,
            CancellationToken cancellationToken)
        {
            Applied.Add(request);
            if (FailNextApply)
            {
                FailNextApply = false;
                return Task.FromResult(false);
            }

            if (request.AdvanceCursor)
            {
                Cursor = request.NextCursor;
            }
            if (ThrowAfterNextApply)
            {
                ThrowAfterNextApply = false;
                throw new InvalidOperationException("simulated_crash_after_commit");
            }

            ApplyCalled.TrySetResult();
            return Task.FromResult(true);
        }
    }

    private sealed class FakeOptionsMonitor : IOptionsMonitor<AtprotoJetstreamOptions>
    {
        private readonly object _lock = new();
        private readonly AtprotoJetstreamOptionsValidator _validator = new();
        private Action<AtprotoJetstreamOptions, string?>? _listener;
        private AtprotoJetstreamOptions _currentValue;

        public FakeOptionsMonitor(AtprotoJetstreamOptions currentValue)
        {
            EnsureValid(currentValue);
            _currentValue = currentValue;
        }

        public AtprotoJetstreamOptions CurrentValue
        {
            get
            {
                lock (_lock)
                {
                    return _currentValue;
                }
            }
        }

        public AtprotoJetstreamOptions Get(string? name) => CurrentValue;

        public IDisposable OnChange(Action<AtprotoJetstreamOptions, string?> listener)
        {
            lock (_lock)
            {
                _listener += listener;
            }

            return new CallbackDisposable(() =>
            {
                lock (_lock)
                {
                    _listener -= listener;
                }
            });
        }

        public void Set(AtprotoJetstreamOptions value)
        {
            EnsureValid(value);
            Action<AtprotoJetstreamOptions, string?>? listener;
            lock (_lock)
            {
                _currentValue = value;
                listener = _listener;
            }

            listener?.Invoke(value, null);
        }

        private void EnsureValid(AtprotoJetstreamOptions value)
        {
            ValidateOptionsResult result = _validator.Validate(null, value);
            if (result.Failed)
            {
                throw new OptionsValidationException(
                    Options.DefaultName,
                    typeof(AtprotoJetstreamOptions),
                    result.Failures);
            }
        }
    }

    private sealed class CallbackDisposable(Action callback) : IDisposable
    {
        private Action? _callback = callback;

        public void Dispose() => Interlocked.Exchange(ref _callback, null)?.Invoke();
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private readonly object _lock = new();
        private readonly List<ManualTimer> _timers = [];
        private readonly Dictionary<long, TaskCompletionSource> _timerWaiters = [];
        private readonly List<(int Count, TaskCompletionSource Completion)> _timerCountWaiters = [];
        private DateTimeOffset _utcNow = utcNow;

        public IReadOnlyList<TimeSpan> CreatedDueTimes
        {
            get
            {
                lock (_lock)
                {
                    return _timers.Select(timer => timer.InitialDueTime).ToArray();
                }
            }
        }

        public override DateTimeOffset GetUtcNow()
        {
            lock (_lock)
            {
                return _utcNow;
            }
        }

        public override long GetTimestamp()
        {
            lock (_lock)
            {
                return _utcNow.UtcTicks;
            }
        }

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            lock (_lock)
            {
                var timer = new ManualTimer(this, callback, state, _utcNow, dueTime, period);
                _timers.Add(timer);
                if (_timerWaiters.Remove(dueTime.Ticks, out TaskCompletionSource? waiter))
                {
                    waiter.TrySetResult();
                }

                foreach ((int count, TaskCompletionSource completion) in
                    _timerCountWaiters.Where(value => value.Count <= _timers.Count))
                {
                    completion.TrySetResult();
                }

                return timer;
            }
        }

        public Task WaitForTimerCountAsync(int count)
        {
            lock (_lock)
            {
                if (_timers.Count >= count)
                {
                    return Task.CompletedTask;
                }

                var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _timerCountWaiters.Add((count, completion));
                return completion.Task;
            }
        }

        public Task WaitForTimerAsync(TimeSpan dueTime)
        {
            lock (_lock)
            {
                if (_timers.Any(timer => timer.Active && timer.InitialDueTime == dueTime))
                {
                    return Task.CompletedTask;
                }

                var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _timerWaiters[dueTime.Ticks] = completion;
                return completion.Task;
            }
        }

        public int CountActiveTimers(TimeSpan dueTime)
        {
            lock (_lock)
            {
                return _timers.Count(timer => timer.Active && timer.InitialDueTime == dueTime);
            }
        }

        public void Advance(TimeSpan elapsed)
        {
            List<(TimerCallback Callback, object? State)> callbacks;
            lock (_lock)
            {
                _utcNow = _utcNow.Add(elapsed);
                callbacks = _timers
                    .Where(timer => timer.Active && timer.DueAt <= _utcNow)
                    .Select(timer => timer.Fire(_utcNow))
                    .ToList();
            }

            foreach ((TimerCallback callback, object? state) in callbacks)
            {
                callback(state);
            }
        }

        private bool Change(ManualTimer timer, TimeSpan dueTime, TimeSpan period)
        {
            lock (_lock)
            {
                timer.Set(_utcNow, dueTime, period);
                return true;
            }
        }

        private void Dispose(ManualTimer timer)
        {
            lock (_lock)
            {
                timer.Active = false;
            }
        }

        private sealed class ManualTimer(
            ManualTimeProvider owner,
            TimerCallback callback,
            object? state,
            DateTimeOffset now,
            TimeSpan dueTime,
            TimeSpan period) : ITimer
        {
            public bool Active { get; set; } = dueTime != Timeout.InfiniteTimeSpan;
            public DateTimeOffset DueAt { get; private set; } = now.Add(dueTime);
            public TimeSpan InitialDueTime { get; } = dueTime;
            private TimeSpan Period { get; set; } = period;

            public bool Change(TimeSpan newDueTime, TimeSpan newPeriod) =>
                owner.Change(this, newDueTime, newPeriod);

            public void Dispose() => owner.Dispose(this);

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }

            public (TimerCallback Callback, object? State) Fire(DateTimeOffset now)
            {
                if (Period == Timeout.InfiniteTimeSpan)
                {
                    Active = false;
                }
                else
                {
                    DueAt = now.Add(Period);
                }

                return (callback, state);
            }

            public void Set(DateTimeOffset now, TimeSpan newDueTime, TimeSpan newPeriod)
            {
                Active = newDueTime != Timeout.InfiniteTimeSpan;
                DueAt = now.Add(newDueTime);
                Period = newPeriod;
            }
        }
    }
}
