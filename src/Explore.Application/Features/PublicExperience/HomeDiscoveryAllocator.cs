using System.Collections.Immutable;
using Explore.Application.DTOs.PublicExperience;
using Explore.Application.Features.Events.Requests.Queries;
using Explore.Application.Responses;

namespace Explore.Application.Features.PublicExperience;

/// <summary>
/// Owns identities for one home response. Call sections sequentially in their display priority.
/// Candidates retain the reader's order; exclusions apply before the section's final take.
/// </summary>
public sealed class HomeDiscoveryAllocator(DateTimeOffset operationNow)
{
    public const int CandidateBudget = 1000;
    public const int BatchSize = 100;
    private readonly HashSet<(string Source, Guid Id)> _allocated = [];

    public async Task<HomeDiscoveryAllocation> AllocateAsync(
        GetEventListRequest criteria,
        int limit,
        Func<GetEventListRequest, CancellationToken, Task<PaginatedResult<EventDiscoveryItemDto>>> read,
        CancellationToken cancellationToken)
    {
        var items = ImmutableArray.CreateBuilder<EventDiscoveryItemDto>();
        var selected = new HashSet<(string Source, Guid Id)>();
        var candidateCount = 0;
        var batchCount = 0;

        HomeDiscoveryAllocation Complete(HomeDiscoveryAllocationStopReason reason)
        {
            _allocated.UnionWith(selected);
            return new HomeDiscoveryAllocation(items.ToImmutable(), reason, candidateCount, batchCount);
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
                    || _allocated.Contains(identity)
                    || !selected.Add(identity))
                    continue;

                items.Add(item);
                if (items.Count == limit)
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
    /// Direct reviewed aliases can later supply the canonical DTO key at this seam.
    /// </summary>
    public static (string Source, Guid Id)? CanonicalIdentity(EventDiscoveryItemDto item)
    {
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
