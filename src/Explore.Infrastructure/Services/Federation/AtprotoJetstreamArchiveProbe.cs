using System.Diagnostics.Metrics;
using CarpaNet.Jetstream;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Services.Federation;

/// <summary>
/// Narrows governed PDS recovery to repositories that actually produced calendar records.
/// <para>
/// The plan endpoint prunes at block granularity and prunes hard for a rare lexicon: sampling the public
/// archive, a two-million-seq window of the firehose selected zero blocks for the calendar collections,
/// and a window that did select blocks yielded two calendar rows out of 8,192 decoded. Blocks are ~430KB
/// each and are <em>not</em> pre-filtered — a returned block contains every collection and kind in that
/// range — so rows are filtered here and the number of blocks scanned is capped.
/// </para>
/// <para>
/// This decides only <em>which</em> repositories to reconcile. Record content still comes from the PDS and
/// still goes through full commit-signature and MST verification, because archive rows carry no signed
/// commit chain and are not a substitute for verified repository data.
/// </para>
/// </summary>
internal sealed class AtprotoJetstreamArchiveProbe(
    IAtprotoJetstreamArchiveClient client,
    IOptions<AtprotoJetstreamOptions> options,
    ILogger<AtprotoJetstreamArchiveProbe> logger) : IAtprotoFederationArchiveProbe
{
    private static readonly Meter Meter = new("Explore.Atproto.Jetstream", "1.0.0");
    private static readonly Counter<long> ProbeCounter = Meter.CreateCounter<long>("atproto.archive.probe");

    public async Task<AtprotoArchiveChangeScope> ResolveChangedDidsAsync(
        long afterSeq,
        IReadOnlyList<string> dids,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dids);
        if (afterSeq <= 0 || dids.Count == 0)
        {
            return Report("no_baseline", AtprotoArchiveChangeScope.Inconclusive);
        }

        var parsedDids = new AtprotoDid[dids.Count];
        for (int index = 0; index < dids.Count; index++)
        {
            if (!AtprotoDid.TryParse(dids[index], out AtprotoDid parsedDid))
            {
                return Report("invalid_scope", AtprotoArchiveChangeScope.Inconclusive);
            }

            parsedDids[index] = parsedDid;
        }

        int maximumBlocks = options.Value.ArchiveProbeMaximumBlocks;
        try
        {
            JetstreamSnapshotPlan plan = await client.PlanSnapshotAsync(
                new JetstreamSnapshotPlanRequest
                {
                    Collections = [.. AtprotoJetstreamConstants.Collections],
                    Kinds = [JetstreamV2EventKind.Commit],
                    Dids = [.. parsedDids.Select(did => did.Value)],
                    AfterSeq = afterSeq
                },
                cancellationToken);

            // The archive only knows about sealed segments. A cursor past the sealed tip means the range
            // has not been archived yet, which is silence rather than evidence of absence.
            if (afterSeq > plan.SealedTipSeq)
            {
                return Report("ahead_of_sealed_tip", AtprotoArchiveChangeScope.Inconclusive);
            }

            var planned = new List<(string Segment, int Block)>();
            foreach (JetstreamPlannedSegment segment in plan.Segments ?? [])
            {
                if (segment.Blocks is not { Count: > 0 })
                {
                    // Whole-segment mode means the index could not prune; scanning it is unbounded work.
                    return Report("unpruned_segment", AtprotoArchiveChangeScope.Inconclusive);
                }

                foreach (JetstreamBlockRange range in segment.Blocks)
                {
                    for (int block = range.First; block <= range.Last; block++)
                    {
                        planned.Add((segment.Name, block));
                        if (planned.Count > maximumBlocks)
                        {
                            return Report("over_block_budget", AtprotoArchiveChangeScope.Inconclusive);
                        }
                    }
                }
            }

            if (planned.Count == 0)
            {
                return Report("no_changes", AtprotoArchiveChangeScope.NoChanges);
            }

            var requested = new HashSet<string>(parsedDids.Select(did => did.Value), StringComparer.Ordinal);
            var changed = new HashSet<string>(StringComparer.Ordinal);
            foreach ((string segment, int block) in planned)
            {
                IReadOnlyList<JetstreamSegmentRow> rows = await client.GetBlockRowsAsync(
                    segment,
                    block,
                    cancellationToken);
                foreach (JetstreamSegmentRow row in rows)
                {
                    // Every commit kind counts, deletes and resyncs included: each is a reason to
                    // reconcile that repository, and over-reporting only costs a verified refetch.
                    if (row.Collection is not null
                        && AtprotoJetstreamConstants.Collections.Contains(row.Collection, StringComparer.Ordinal)
                        && requested.Contains(row.Did))
                    {
                        changed.Add(row.Did);
                    }
                }

                if (changed.Count == requested.Count)
                {
                    break;
                }
            }

            return Report(
                changed.Count == 0 ? "no_changes" : "narrowed",
                new AtprotoArchiveChangeScope(true, [.. changed.Order(StringComparer.Ordinal)]));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // The archive is an optimisation. Never let it narrow or block recovery when it misbehaves.
            logger.LogWarning(
                "ATProto Jetstream archive probe failed with {FailureType}; recovery falls back to its full scope.",
                exception.GetType().Name);
            return Report("probe_failure", AtprotoArchiveChangeScope.Inconclusive);
        }
    }

    private static AtprotoArchiveChangeScope Report(string outcome, AtprotoArchiveChangeScope scope)
    {
        ProbeCounter.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
        return scope;
    }
}
