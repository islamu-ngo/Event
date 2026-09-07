using CarpaNet.Jetstream;
using Explore.Application.Contracts.Infrastructure;
using Explore.Infrastructure.Services.Federation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Tests.Federation;

public sealed class AtprotoJetstreamArchiveProbeTests
{
    private const string Did = "did:plc:active-owner";
    private const string SecondDid = "did:plc:quiet-owner";
    private const long SealedTip = 25_000_000_000;

    [Test]
    [Arguments(0L)]
    [Arguments(-1L)]
    public async Task ResolveChangedDids_WithoutUsableBaseline_IsInconclusive(long afterSeq)
    {
        var client = new FakeArchiveClient();
        AtprotoArchiveChangeScope scope = await CreateProbe(client)
            .ResolveChangedDidsAsync(afterSeq, [Did], CancellationToken.None);

        await Assert.That(scope.IsConclusive).IsFalse();
        await Assert.That(client.PlanCalls).IsEqualTo(0);
    }

    [Test]
    public async Task ResolveChangedDids_WithEmptyScope_IsInconclusiveWithoutCallingArchive()
    {
        var client = new FakeArchiveClient();
        AtprotoArchiveChangeScope scope = await CreateProbe(client)
            .ResolveChangedDidsAsync(100, [], CancellationToken.None);

        await Assert.That(scope.IsConclusive).IsFalse();
        await Assert.That(client.PlanCalls).IsEqualTo(0);
    }

    [Test]
    public async Task ResolveChangedDids_CursorAheadOfSealedTip_IsInconclusiveNotNoChanges()
    {
        // The archive simply has no record of this range yet. Reading that silence as "nothing changed"
        // would make recovery skip repositories that did change.
        var client = new FakeArchiveClient { Plan = Plan(SealedTip) };
        AtprotoArchiveChangeScope scope = await CreateProbe(client)
            .ResolveChangedDidsAsync(SealedTip + 1, [Did], CancellationToken.None);

        await Assert.That(scope.IsConclusive).IsFalse();
        await Assert.That(scope.ChangedDids).IsEmpty();
        await Assert.That(client.BlockCalls).IsEmpty();
    }

    [Test]
    public async Task ResolveChangedDids_EmptyPlan_IsConclusiveNoChanges()
    {
        var client = new FakeArchiveClient { Plan = Plan(SealedTip) };
        AtprotoArchiveChangeScope scope = await CreateProbe(client)
            .ResolveChangedDidsAsync(100, [Did, SecondDid], CancellationToken.None);

        await Assert.That(scope.IsConclusive).IsTrue();
        await Assert.That(scope.ChangedDids).IsEmpty();
        await Assert.That(client.BlockCalls).IsEmpty();
    }

    [Test]
    public async Task ResolveChangedDids_UnprunedWholeSegment_IsInconclusive()
    {
        var client = new FakeArchiveClient
        {
            Plan = Plan(SealedTip, new JetstreamPlannedSegment
            {
                Name = "seg_0.jss",
                Mode = JetstreamSegmentPlanMode.WholeSegment,
                Blocks = []
            })
        };

        AtprotoArchiveChangeScope scope = await CreateProbe(client)
            .ResolveChangedDidsAsync(100, [Did], CancellationToken.None);

        await Assert.That(scope.IsConclusive).IsFalse();
        await Assert.That(client.BlockCalls).IsEmpty();
    }

    [Test]
    public async Task ResolveChangedDids_BeyondBlockBudget_IsInconclusiveWithoutDownloading()
    {
        var client = new FakeArchiveClient { Plan = Plan(SealedTip, Segment("seg_0.jss", 0, 8)) };
        AtprotoArchiveChangeScope scope = await CreateProbe(client, maximumBlocks: 4)
            .ResolveChangedDidsAsync(100, [Did], CancellationToken.None);

        await Assert.That(scope.IsConclusive).IsFalse();
        await Assert.That(client.BlockCalls).IsEmpty();
    }

    [Test]
    public async Task ResolveChangedDids_WithinBudget_NarrowsToDidsHoldingCalendarRows()
    {
        var client = new FakeArchiveClient
        {
            Plan = Plan(SealedTip, Segment("seg_0.jss", 0, 1)),
            Rows =
            {
                ["seg_0.jss/0"] =
                [
                    Row(Did, AtprotoJetstreamConstants.EventCollection),
                    Row(SecondDid, "app.bsky.feed.like"),
                    Row("did:plc:not-requested", AtprotoJetstreamConstants.EventCollection)
                ],
                ["seg_0.jss/1"] = [Row(SecondDid, "app.bsky.feed.post")]
            }
        };

        AtprotoArchiveChangeScope scope = await CreateProbe(client)
            .ResolveChangedDidsAsync(100, [Did, SecondDid], CancellationToken.None);

        // Blocks are not pre-filtered, so unrelated collections and unrequested DIDs must be dropped here.
        await Assert.That(scope.IsConclusive).IsTrue();
        await Assert.That(scope.ChangedDids).IsEquivalentTo([Did]);
    }

    [Test]
    public async Task ResolveChangedDids_CountsRsvpDeletesAndResyncsAsActivity()
    {
        var client = new FakeArchiveClient
        {
            Plan = Plan(SealedTip, Segment("seg_0.jss", 0, 1)),
            Rows =
            {
                ["seg_0.jss/0"] =
                [
                    Row(Did, AtprotoJetstreamConstants.RsvpCollection, JetstreamSegmentRowKind.Delete)
                ],
                ["seg_0.jss/1"] =
                [
                    Row(SecondDid, AtprotoJetstreamConstants.EventCollection, JetstreamSegmentRowKind.CreateResync)
                ]
            }
        };

        AtprotoArchiveChangeScope scope = await CreateProbe(client)
            .ResolveChangedDidsAsync(100, [Did, SecondDid], CancellationToken.None);

        // A delete is a reconciliation trigger, and treating a resync as activity only costs a refetch.
        await Assert.That(scope.ChangedDids).IsEquivalentTo([Did, SecondDid]);
    }

    [Test]
    public async Task ResolveChangedDids_StopsDownloadingOnceEveryRequestedDidIsAccountedFor()
    {
        var client = new FakeArchiveClient
        {
            Plan = Plan(SealedTip, Segment("seg_0.jss", 0, 3)),
            Rows =
            {
                ["seg_0.jss/0"] = [Row(Did, AtprotoJetstreamConstants.EventCollection)],
                ["seg_0.jss/1"] = [Row(SecondDid, AtprotoJetstreamConstants.EventCollection)],
                ["seg_0.jss/2"] = [Row(Did, AtprotoJetstreamConstants.EventCollection)],
                ["seg_0.jss/3"] = [Row(Did, AtprotoJetstreamConstants.EventCollection)]
            }
        };

        AtprotoArchiveChangeScope scope = await CreateProbe(client)
            .ResolveChangedDidsAsync(100, [Did, SecondDid], CancellationToken.None);

        await Assert.That(scope.ChangedDids).IsEquivalentTo([Did, SecondDid]);
        await Assert.That(client.BlockCalls).IsEquivalentTo(["seg_0.jss/0", "seg_0.jss/1"]);
    }

    [Test]
    public async Task ResolveChangedDids_PlanFailure_IsInconclusive()
    {
        var client = new FakeArchiveClient { PlanException = new HttpRequestException("archive down") };
        AtprotoArchiveChangeScope scope = await CreateProbe(client)
            .ResolveChangedDidsAsync(100, [Did], CancellationToken.None);

        await Assert.That(scope.IsConclusive).IsFalse();
    }

    [Test]
    public async Task ResolveChangedDids_BlockDownloadFailure_IsInconclusive()
    {
        var client = new FakeArchiveClient
        {
            Plan = Plan(SealedTip, Segment("seg_0.jss", 0, 0)),
            BlockException = new JetstreamV2Exception("unavailable", JetstreamV2ErrorNames.ServiceUnavailable)
        };

        AtprotoArchiveChangeScope scope = await CreateProbe(client)
            .ResolveChangedDidsAsync(100, [Did], CancellationToken.None);

        // A partially scanned plan must never be reported as a complete answer.
        await Assert.That(scope.IsConclusive).IsFalse();
        await Assert.That(scope.ChangedDids).IsEmpty();
    }

    [Test]
    public async Task ResolveChangedDids_Cancellation_PropagatesInsteadOfDegrading()
    {
        var client = new FakeArchiveClient { Plan = Plan(SealedTip) };
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => CreateProbe(client)
            .ResolveChangedDidsAsync(100, [Did], cancellation.Token));
    }

    [Test]
    public async Task ResolveChangedDids_RequestsOnlyCalendarCommitsForTheGivenScope()
    {
        var client = new FakeArchiveClient { Plan = Plan(SealedTip) };
        await CreateProbe(client).ResolveChangedDidsAsync(4_242, [Did, SecondDid], CancellationToken.None);

        JetstreamSnapshotPlanRequest request = client.LastRequest!;
        await Assert.That(request.AfterSeq).IsEqualTo(4_242);
        await Assert.That(request.Kinds).IsEquivalentTo([JetstreamV2EventKind.Commit]);
        await Assert.That(request.Collections)
            .IsEquivalentTo([AtprotoJetstreamConstants.EventCollection, AtprotoJetstreamConstants.RsvpCollection]);
        await Assert.That(request.Collections).DoesNotContain(collection => collection.Contains('*'));
        await Assert.That(request.Dids).IsEquivalentTo([Did, SecondDid]);
    }

    private static AtprotoJetstreamArchiveProbe CreateProbe(
        IAtprotoJetstreamArchiveClient client,
        int maximumBlocks = 64) => new(
            client,
            Options.Create(new AtprotoJetstreamOptions { ArchiveProbeMaximumBlocks = maximumBlocks }),
            NullLogger<AtprotoJetstreamArchiveProbe>.Instance);

    private static JetstreamSnapshotPlan Plan(long sealedTip, params JetstreamPlannedSegment[] segments) => new()
    {
        SealedTipSeq = sealedTip,
        PlannedThroughSeq = sealedTip,
        Segments = segments
    };

    private static JetstreamPlannedSegment Segment(string name, int firstBlock, int lastBlock) => new()
    {
        Name = name,
        Mode = JetstreamSegmentPlanMode.Blocks,
        Blocks = [new JetstreamBlockRange { First = firstBlock, Last = lastBlock }]
    };

    private static JetstreamSegmentRow Row(
        string did,
        string collection,
        JetstreamSegmentRowKind kind = JetstreamSegmentRowKind.Create) => new()
        {
            Did = did,
            Collection = collection,
            Kind = kind,
            Rkey = "3m-remote"
        };

    private sealed class FakeArchiveClient : IAtprotoJetstreamArchiveClient
    {
        public JetstreamSnapshotPlan Plan { get; set; } = new();
        public Dictionary<string, IReadOnlyList<JetstreamSegmentRow>> Rows { get; } = [];
        public Exception? PlanException { get; set; }
        public Exception? BlockException { get; set; }
        public int PlanCalls { get; private set; }
        public List<string> BlockCalls { get; } = [];
        public JetstreamSnapshotPlanRequest? LastRequest { get; private set; }

        public Task<JetstreamSnapshotPlan> PlanSnapshotAsync(
            JetstreamSnapshotPlanRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PlanCalls++;
            LastRequest = request;
            return PlanException is null
                ? Task.FromResult(Plan)
                : Task.FromException<JetstreamSnapshotPlan>(PlanException);
        }

        public Task<IReadOnlyList<JetstreamSegmentRow>> GetBlockRowsAsync(
            string segmentName,
            int blockIndex,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BlockCalls.Add($"{segmentName}/{blockIndex}");
            return BlockException is null
                ? Task.FromResult(Rows.GetValueOrDefault($"{segmentName}/{blockIndex}", []))
                : Task.FromException<IReadOnlyList<JetstreamSegmentRow>>(BlockException);
        }
    }
}
