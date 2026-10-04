using System.Collections.Immutable;
using Explore.Application.DTOs.PublicExperience;
using Explore.Application.Features.Events.Requests.Queries;
using Explore.Application.Responses;

namespace Explore.Application.Features.PublicExperience;

/// <summary>
/// Applies section-specific identity rules within one home response.
/// Featured and Upcoming are independent; Recently Added prefers novelty before overlap.
/// Other shelves exclude earlier assignments. Each shelf retains the reader's order.
/// </summary>
public sealed class HomeDiscoveryAllocator(DateTimeOffset operationNow)
{
    public const int CandidateBudget = 1000;
    public const int BatchSize = 100;
    private readonly HashSet<(string Source, Guid Id)> _allocated = [];
    private readonly HashSet<(string Source, Guid Id)> _featured = [];
    private readonly HashSet<(string Source, Guid Id)> _upcoming = [];

    public async Task<HomeDiscoveryAllocation> AllocateAsync(
        GetEventListRequest criteria,
        int limit,
        Func<GetEventListRequest, CancellationToken, Task<PaginatedResult<EventDiscoveryItemDto>>> read,
        CancellationToken cancellationToken,
        HomeDiscoverySectionPolicy policy = HomeDiscoverySectionPolicy.Exclusive)
    {
        List<(EventDiscoveryItemDto Item, (string Source, Guid Id) Identity, int Ordinal)>[] bands = [[], [], []];
        var selected = new HashSet<(string Source, Guid Id)>();
        var candidateCount = 0;
        var batchCount = 0;

        HomeDiscoveryAllocation Complete(HomeDiscoveryAllocationStopReason reason)
        {
            var chosen = bands.SelectMany(band => band).Take(limit)
                .OrderBy(candidate => candidate.Ordinal).ToArray();
            _allocated.UnionWith(chosen.Select(candidate => candidate.Identity));
            if (policy == HomeDiscoverySectionPolicy.Featured)
                _featured.UnionWith(chosen.Select(candidate => candidate.Identity));
            if (policy == HomeDiscoverySectionPolicy.Upcoming)
                _upcoming.UnionWith(chosen.Select(candidate => candidate.Identity));
            if (reason == HomeDiscoveryAllocationStopReason.Exhausted && chosen.Length == limit)
                reason = HomeDiscoveryAllocationStopReason.Full;
            return new HomeDiscoveryAllocation(
                chosen.Select(candidate => candidate.Item).ToImmutableArray(), reason, candidateCount, batchCount);
        }

        for (var pageNumber = 1; pageNumber <= CandidateBudget / BatchSize; pageNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = await read(
                criteria with { PageNumber = pageNumber, PageSize = BatchSize, OperationNow = operationNow },
                cancellationToken).WaitAsync(cancellationToken);
            batchCount++;
            foreach (var item in page.Items)
            {
                candidateCount++;
                if (CanonicalIdentity(item) is not { } identity
                    || (policy == HomeDiscoverySectionPolicy.Exclusive && _allocated.Contains(identity))
                    || !selected.Add(identity))
                    continue;

                var priority = policy == HomeDiscoverySectionPolicy.RecentlyAdded
                    ? _upcoming.Contains(identity) ? 2 : _featured.Contains(identity) ? 1 : 0
                    : 0;
                if (bands[priority].Count < limit)
                    bands[priority].Add((item, identity, candidateCount));
                if (bands[0].Count == limit)
                    return Complete(HomeDiscoveryAllocationStopReason.Full);
            }

            if (!page.HasNextPage)
                return Complete(HomeDiscoveryAllocationStopReason.Exhausted);

            // A counted pool with no next-page candidates is not proof of exhaustion.
            if (page.Items.Count == 0)
                return Complete(HomeDiscoveryAllocationStopReason.ExhaustionUnproven);
        }

        return Complete(HomeDiscoveryAllocationStopReason.CandidateBudgetExceeded);
    }

    /// <summary>
    /// Keeps source namespaces distinct while recognizing a local record's ATProto binding.
    /// Reviewed aliases use the same server-only root key as the discovery reader.
    /// </summary>
    public static (string Source, Guid Id)? CanonicalIdentity(EventDiscoveryItemDto item)
    {
        if (item.DiscoveryIdentityId is { } reviewedId && reviewedId != Guid.Empty)
            return ("reviewed", reviewedId);
        if (item.Event is { Id: var localId } && localId != Guid.Empty)
        {
            var recordId = item.Event.AtprotoRecordId ?? item.Federation?.AtprotoRecordId;
            return recordId is { } id && id != Guid.Empty
                ? ("atproto", id)
                : ("local", localId);
        }

        return item.FederatedEvent is { Id: var federatedId } && federatedId != Guid.Empty
            ? ("atproto", federatedId)
            : null;
    }
}

public enum HomeDiscoverySectionPolicy
{
    Exclusive,
    Featured,
    Upcoming,
    RecentlyAdded
}

public enum HomeDiscoveryAllocationStopReason
{
    Full,
    Exhausted,
    CandidateBudgetExceeded,
    ExhaustionUnproven
}

public sealed record HomeDiscoveryAllocation(
    ImmutableArray<EventDiscoveryItemDto> Items,
    HomeDiscoveryAllocationStopReason StopReason,
    int CandidateCount,
    int BatchCount);
