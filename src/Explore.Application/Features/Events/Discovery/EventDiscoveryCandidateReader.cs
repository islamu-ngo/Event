using System.Collections.Immutable;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.PublicExperience;
using Explore.Application.Exceptions;
using Explore.Application.Features.Events.Requests.Queries;
using Explore.Application.Features.PublicExperience;
using Explore.Application.Services.Federation;
using Explore.Domain;
using Explore.Domain.Services.Discovery;
using static Explore.Application.Features.Federation.Atproto.Handlers.Queries.EventDiscoveryCandidateMapping;

namespace Explore.Application.Features.Events.Discovery;

public sealed record EventDiscoveryCandidateCapture(
    ImmutableArray<EventDiscoverySnapshotItem> Membership,
    bool LocalSourceComplete,
    bool RemoteSourceComplete,
    bool Truncated,
    int ExaminedRows,
    int SourceSeeks,
    DateTimeOffset ValidUntilUtc);

/// <summary>
/// Runs only inside the caller's Serializable unit of work. The caller owns epoch fences, validation,
/// snapshot reservation, expiry and final response release. Captured membership contains no card data.
/// </summary>
public sealed class EventDiscoveryCandidateReader(
    EventDiscoveryLocalSource localSource,
    IEventRepository events,
    IAtprotoEventProjectionRepository projections,
    IEventDiscoveryIdentityRepository identities,
    AtprotoEventGovernanceResolver governanceResolver,
    ITenantContext tenantContext,
    TimeProvider clock,
    ITenantLifecycleAccessService lifecycle)
{
    private const int SourceWindow = 128;

    public async Task<EventDiscoveryCandidateCapture> CaptureAsync(
        GetEventListRequest criteria, EventDiscoveryTraversalLimits limits,
        CancellationToken cancellationToken = default)
    {
        var now = criteria.OperationNow ?? clock.GetUtcNow();
        criteria = criteria with { OperationNow = now };
        if (!await lifecycle.IsPublicAsync(tenantContext.TenantId, cancellationToken))
            return new([], true, true, false, 0, 0, now + limits.Lifetime);
        var specification = await localSource.PrepareAsync(criteria, cancellationToken);
        var governance = await governanceResolver.ResolveAsync(tenantContext.TenantId, null, cancellationToken);
        AtprotoEventProjectionQuery? remoteQuery = null;
        bool remoteComplete = !governance.EventsEnabled ||
            !TryCreateProjectionQuery(criteria, SourceWindow, now, out remoteQuery);
        bool localComplete = specification is null;
        var selected = new Dictionary<(string, Guid), Candidate>();
        var localBuffer = new Queue<Candidate>();
        var remoteBuffer = new Queue<Candidate>();
        EventDiscoverySourceCursor? localCursor = null;
        EventDiscoverySourceCursor? remoteCursor = null;
        int examined = 0, seeks = 0;
        var compare = CreateComparer(criteria.SortBy, criteria.SortDescending);
        var boundaries = new Dictionary<(EventDiscoverySourceKind, Guid), DateTimeOffset>();

        while (selected.Count < limits.MaxIdentities)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (localBuffer.Count == 0 && !localComplete && CanSeek())
            {
                int take = Math.Min(SourceWindow, limits.MaxExaminedRows - examined);
                var rows = await events.SeekPublicDiscoveryAsync(specification!, localCursor, take, cancellationToken);
                seeks++;
                examined += rows.Count;
                localComplete = rows.Count < take;
                if (rows.Count > 0)
                {
                    var last = rows[^1];
                    localCursor = new(last.Id, last.Title, last.TotalViews, last.CreatedAt,
                        last.Sessions.Single().StartTime);
                    var cards = await localSource.ProjectAsync(rows, specification!, cancellationToken);
                    var candidates = await BindAsync(cards.Select(card => MapOccurrence(card, governance.EventsEnabled)),
                        EventDiscoverySourceKind.LocalEvent, cancellationToken);
                    foreach (var candidate in candidates)
                        localBuffer.Enqueue(candidate);
                    foreach (var (id, boundary) in await localSource.GetValidityBoundariesAsync(rows, now, cancellationToken))
                        boundaries[(EventDiscoverySourceKind.LocalEvent, id)] = boundary;
                }
            }
            if (remoteBuffer.Count == 0 && !remoteComplete && CanSeek())
            {
                int take = Math.Min(SourceWindow, limits.MaxExaminedRows - examined);
                var rows = await projections.SeekPublicDiscoveryAsync(
                    remoteQuery! with { Take = take }, remoteCursor, cancellationToken);
                seeks++;
                examined += rows.Count;
                remoteComplete = rows.Count < take;
                if (rows.Count > 0)
                {
                    var last = rows[^1];
                    remoteCursor = new(last.AtprotoRecordId, last.Name, 0, last.CreatedAt.UtcDateTime, last.StartsAt);
                    foreach (var candidate in await BindAsync(rows.Select(MapFederated),
                        EventDiscoverySourceKind.AtprotoRecord, cancellationToken))
                        remoteBuffer.Enqueue(candidate);
                    foreach (var row in rows)
                    {
                        var instants = new[] { row.StartsAt, row.EndsAt }.Where(instant => instant > now).ToArray();
                        if (instants.Length > 0)
                            boundaries[(EventDiscoverySourceKind.AtprotoRecord, row.AtprotoRecordId)] = instants.Min()!.Value;
                    }
                }
            }

            // Tombstones and aliases still consume source rows. An empty eligible window is not
            // exhaustion: advance its complete source cursor and seek again within the shared budget.
            if (localBuffer.Count == 0 && !localComplete && CanSeek()
                || remoteBuffer.Count == 0 && !remoteComplete && CanSeek())
                continue;
            if (localBuffer.Count == 0 && remoteBuffer.Count == 0)
                break;
            Candidate next = remoteBuffer.Count == 0
                || localBuffer.Count > 0 && compare(localBuffer.Peek().Card, remoteBuffer.Peek().Card) <= 0
                    ? localBuffer.Dequeue() : remoteBuffer.Dequeue();
            var key = HomeDiscoveryAllocator.CanonicalIdentity(next.Card)
                ?? throw new EventDiscoveryUnavailableException();
            if (!selected.TryGetValue(key, out var previous) || Prefer(next, previous))
                selected[key] = next;
        }

        var ordered = selected.Values.ToList();
        ordered.Sort((left, right) => compare(left.Card, right.Card));
        bool localExhausted = localComplete && localBuffer.Count == 0;
        bool remoteExhausted = remoteComplete && remoteBuffer.Count == 0;
        var validUntil = now + limits.Lifetime;
        foreach (var candidate in ordered)
            if (boundaries.TryGetValue((candidate.Membership.SourceKind, candidate.Membership.SourceId), out var boundary)
                && boundary < validUntil)
                validUntil = boundary;
        return new(ordered.Select(candidate => candidate.Membership).ToImmutableArray(),
            localExhausted, remoteExhausted, !localExhausted || !remoteExhausted, examined, seeks, validUntil);

        bool CanSeek() => examined < limits.MaxExaminedRows && seeks < limits.MaxSourceSeeks;
    }

    /// <summary>
    /// Returns null when any stored member no longer matches current authority, criteria, identity,
    /// or its exact captured session. The caller must discard the entire page and restart, including metadata.
    /// </summary>
    public async Task<IReadOnlyList<EventDiscoveryItemDto>?> ReprojectAsync(
        GetEventListRequest criteria, IReadOnlyList<EventDiscoverySnapshotItem> membership,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(membership.Count, EventDiscoveryTraversalLimits.PageSizeCeiling);
        if (membership.Any(item => item.TenantId != Guid.Empty && item.TenantId != tenantContext.TenantId))
            return null;
        if (!await lifecycle.IsPublicAsync(tenantContext.TenantId, cancellationToken))
            return membership.Count == 0 ? Array.Empty<EventDiscoveryItemDto>() : null;
        criteria = criteria with { OperationNow = clock.GetUtcNow() };
        var specification = await localSource.PrepareAsync(criteria, cancellationToken);
        var governance = await governanceResolver.ResolveAsync(tenantContext.TenantId, null, cancellationToken);
        var current = new List<Candidate>(membership.Count);
        var localMembers = membership.Where(item => item.SourceKind == EventDiscoverySourceKind.LocalEvent).ToArray();
        if (localMembers.Length > 0)
        {
            if (specification is null || localMembers.Any(item => item.MatchingSessionId is null))
                return null;
            var references = localMembers.ToDictionary(item => item.SourceId, item => item.MatchingSessionId!.Value);
            var rows = await events.GetPublicDiscoveryMembersAsync(specification, references, cancellationToken);
            var cards = await localSource.ProjectAsync(rows, specification, cancellationToken);
            current.AddRange(await BindAsync(cards.Select(card => MapOccurrence(card, governance.EventsEnabled)),
                EventDiscoverySourceKind.LocalEvent, cancellationToken));
        }
        var remoteMembers = membership.Where(item => item.SourceKind == EventDiscoverySourceKind.AtprotoRecord).ToArray();
        if (remoteMembers.Length > 0)
        {
            if (!governance.EventsEnabled || !TryCreateProjectionQuery(
                criteria, remoteMembers.Length, criteria.OperationNow!.Value, out var query))
                return null;
            var rows = await projections.GetPublicDiscoveryMembersAsync(
                query, remoteMembers.Select(item => item.SourceId).ToArray(), cancellationToken);
            current.AddRange(await BindAsync(rows.Select(MapFederated),
                EventDiscoverySourceKind.AtprotoRecord, cancellationToken));
        }
        if (current.Count != membership.Count)
            return null;
        var bySource = current.ToDictionary(candidate =>
            (candidate.Membership.SourceKind, candidate.Membership.SourceId));
        var result = new List<EventDiscoveryItemDto>(membership.Count);
        foreach (var item in membership)
        {
            if (!bySource.TryGetValue((item.SourceKind, item.SourceId), out var candidate)
                || candidate.Membership.CanonicalKind != item.CanonicalKind
                || candidate.Membership.CanonicalId != item.CanonicalId
                || candidate.Membership.MatchingSessionId != item.MatchingSessionId)
                return null;
            result.Add(candidate.Card);
        }
        var recordIds = result.Where(item => item.Event is not null && item.Federation is not null)
            .Select(item => item.Federation!.AtprotoRecordId).Distinct().ToArray();
        var sourceLinks = (await projections.GetVisibleByRecordIdsAsync(recordIds, cancellationToken))
            .Where(projection => projection.SourceUrl is not null).Select(projection => projection.AtprotoRecordId).ToHashSet();
        foreach (var card in result.Where(item => item.Event is not null && item.Federation is not null))
            card.Federation!.HasSourceLink = sourceLinks.Contains(card.Federation.AtprotoRecordId);
        return result.AsReadOnly();
    }

    private async Task<IReadOnlyList<Candidate>> BindAsync(
        IEnumerable<EventDiscoveryItemDto> values, EventDiscoverySourceKind kind, CancellationToken cancellationToken)
    {
        var cards = values.ToArray();
        Guid SourceId(EventDiscoveryItemDto item) => item.Event?.Id ?? item.FederatedEvent!.Id;
        var bindings = await identities.GetBindingsAsync(tenantContext.TenantId, kind,
            cards.Select(item => SourceId(item).ToString("D")).ToArray(), cancellationToken);
        var byKey = bindings.ToDictionary(binding => binding.SourceKey, StringComparer.Ordinal);
        var candidates = new List<Candidate>(cards.Length);
        foreach (var original in cards)
        {
            var card = original;
            bool primary = false;
            if (byKey.TryGetValue(SourceId(card).ToString("D"), out var binding))
            {
                if (binding.TenantId != tenantContext.TenantId || binding.SourceKind != kind
                    || binding.Alias is { Primary: null }
                    || !binding.IsDeleted && EventDiscoveryIdentityRules.SelectRepresentation(
                        binding.Alias is { } alias ? [binding, alias.Primary] : [binding],
                        new HashSet<Guid> { binding.Id }) is null)
                    throw new EventDiscoveryUnavailableException();
                if (binding.IsDeleted)
                    continue;
                card = card with { DiscoveryIdentityId = binding.Alias?.PrimaryIdentityId ?? binding.Id };
                primary = binding.Alias is null;
            }
            var key = HomeDiscoveryAllocator.CanonicalIdentity(card)
                ?? throw new EventDiscoveryUnavailableException();
            var canonicalKind = key.Source switch
            {
                "reviewed" => EventDiscoveryCanonicalKind.ReviewedIdentity,
                "atproto" => EventDiscoveryCanonicalKind.AtprotoRecord,
                _ => EventDiscoveryCanonicalKind.LocalEvent
            };
            candidates.Add(new(card, EventDiscoverySnapshotItem.Create(
                kind, SourceId(card), canonicalKind, key.Id, card.Event?.MatchingSession?.Id), primary));
        }
        return candidates;
    }

    private static bool Prefer(Candidate next, Candidate previous) =>
        CompareRepresentations(next.Card, next.IsPrimary, previous.Card, previous.IsPrimary) < 0;

    private static EventDiscoveryItemDto MapOccurrence(
        Explore.Application.DTOs.Event.EventListDto card, bool includeFederationMetadata) =>
        MapLocal(card with { FirstSessionStartUtc = card.MatchingSession!.StartsAtUtc }, includeFederationMetadata);

    private sealed record Candidate(
        EventDiscoveryItemDto Card, EventDiscoverySnapshotItem Membership, bool IsPrimary);
}
