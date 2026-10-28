using System.Collections.Immutable;
using Explore.Domain;
using Explore.Persistence.Services;
using ISLAMU.Moderation.Domain;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

/// <summary>Discovery coordinates for early native anchors, never target approval or replay eligibility.</summary>
internal sealed class ModerationNativeTargetPlan(
    Guid hostId, Guid tenantId, Guid effectId, Guid? selectionId, long? selectionRevision,
    ImmutableArray<ModerationNativeTargetPlan.ResourceTarget> resources,
    ImmutableArray<ModerationNativeTargetPlan.EventTarget> events)
{
    private Guid? SelectionId => selectionId;
    private long? SelectionRevision => selectionRevision;
    private ImmutableArray<ResourceTarget> Resources => resources;
    private ImmutableArray<EventTarget> Events => events;
    internal EventAuthorityProviderOperations.AuthorityPlan? RepresentativeAuthority { get; init; }
    internal IEnumerable<int> RepresentativeRoles => RepresentativeAuthority?.Assignments.Select(row => row.RoleId) ?? [];
    internal IEnumerable<Guid> Actors => resources.SelectMany(row => new[]
        { (Guid?)row.Ancestry.ActorId, row.Ancestry.OrganizerActorId, row.Ancestry.StorageActorId })
        .Concat(events.SelectMany(row => new[] { (Guid?)row.Ancestry.ActorId, row.Ancestry.OrganizerActorId }))
        .Concat((RepresentativeAuthority?.Parents ?? []).SelectMany(row => new[] { (Guid?)row.ActorId, row.OrganizerActorId }))
        .OfType<Guid>().Where(id => id != Guid.Empty).Distinct();

    internal Task AcquireRepresentativePermissionsAsync(ExploreDbContext context, CancellationToken token) =>
        EventAuthoritySnapshotService.AcquirePermissionSourcesAsync(context, RepresentativeRoles.Distinct().Order().ToArray(), token);

    internal static Task<ModerationNativeTargetPlan> DiscoverAsync(
        ExploreDbContext context, Guid hostId, Guid tenantId, Guid effectId, CancellationToken token) =>
        EventAuthorityProviderOperations.ReadWithoutRetainingLocksAsync(context,
            source => ReadAsync(source, hostId, tenantId, effectId, token), token);

    internal async Task<bool> MatchesAsync(ExploreDbContext context, ModerationContributionSelection selection, CancellationToken token)
    {
        if (selection.Id != selectionId || selection.Revision != selectionRevision || selection.EffectId != effectId
            || selection.HostBindingId != hostId || selection.TenantId != tenantId) return false;
        var current = await ReadAsync(context, hostId, tenantId, effectId, token);
        return current.SelectionId == selectionId && current.SelectionRevision == selectionRevision
            && resources.SequenceEqual(current.Resources) && events.SequenceEqual(current.Events)
            && SameRepresentativeAuthority(RepresentativeAuthority, current.RepresentativeAuthority);
    }

    internal EventAuthorityProviderOperations.ResourceAncestry? Resource(
        Guid selected, ModerationResourceTargetAdoption adoption, ModerationResourceGeneration generation)
    {
        var entry = resources.SingleOrDefault(row => row.AdoptionId == adoption.Id && row.Ordinal == adoption.BindingOrdinal
            && row.GenerationId == generation.Id && row.GenerationId == adoption.GenerationId);
        return selected == selectionId && entry is not null
            && entry.Ancestry.ResourceId == generation.ResourceId && entry.Ancestry.EventId == generation.EventId
            && entry.Ancestry.ActorId == generation.ActorId && entry.Ancestry.OrganizerActorId == generation.OrganizerActorId
            && entry.Ancestry.StorageObjectId == generation.StorageObjectId ? entry.Ancestry : null;
    }

    internal EventAuthorityProviderOperations.Ownership? Event(
        Guid selected, ModerationEventTargetAdoption adoption, ModerationEventGeneration generation)
    {
        var entry = events.SingleOrDefault(row => row.AdoptionId == adoption.Id && row.Ordinal == adoption.BindingOrdinal
            && row.GenerationId == generation.Id && row.GenerationId == adoption.GenerationId);
        return selected == selectionId && entry is not null && entry.Ancestry.Id == generation.EventId
            && entry.Ancestry.ActorId == generation.ActorId && entry.Ancestry.OrganizerActorId == generation.OrganizerActorId
            ? entry.Ancestry : null;
    }

    private static async Task<ModerationNativeTargetPlan> ReadAsync(
        ExploreDbContext source, Guid hostId, Guid tenantId, Guid effectId, CancellationToken token)
    {
        var selection = await source.ModerationContributionSelections.AsNoTracking()
            .Where(row => row.HostBindingId == hostId && row.TenantId == tenantId && row.EffectId == effectId)
            .Select(row => new { row.Id, row.Revision, row.DecisionId }).SingleOrDefaultAsync(token);
        if (selection is null) return new(hostId, tenantId, effectId, null, null, [], []);
        var resourceRows = await source.ModerationResourceTargetAdoptions.AsNoTracking()
            .Where(row => row.SelectionId == selection.Id).OrderBy(row => row.Id).Take(65).ToArrayAsync(token);
        var eventRows = await source.ModerationEventTargetAdoptions.AsNoTracking()
            .Where(row => row.SelectionId == selection.Id).OrderBy(row => row.Id).Take(65).ToArrayAsync(token);
        if (resourceRows.Length + eventRows.Length > 64)
            return new(hostId, tenantId, effectId, selection.Id, selection.Revision, [], []);
        var resourceIds = resourceRows.Select(row => row.GenerationId).Distinct().ToArray();
        var eventIds = eventRows.Select(row => row.GenerationId).Distinct().ToArray();
        var resourceGenerations = await source.ModerationResourceGenerations.AsNoTracking()
            .Where(row => resourceIds.Contains(row.Id) && row.HostBindingId == hostId && row.TenantId == tenantId).ToArrayAsync(token);
        var eventGenerations = await source.ModerationEventGenerations.AsNoTracking()
            .Where(row => eventIds.Contains(row.Id) && row.HostBindingId == hostId && row.TenantId == tenantId).ToArrayAsync(token);
        var storageIds = resourceGenerations.Select(row => row.StorageObjectId).OfType<Guid>().Distinct().ToArray();
        var storage = await source.StorageObjects.AsNoTracking().Where(row => storageIds.Contains(row.Id)
            && row.TenantId == tenantId && !row.IsDeleted).Select(row => new { row.Id, row.ActorId }).ToArrayAsync(token);
        var plannedResources = ImmutableArray.CreateBuilder<ResourceTarget>();
        foreach (var adoption in resourceRows)
        {
            var generation = resourceGenerations.SingleOrDefault(row => row.Id == adoption.GenerationId);
            if (generation is null) continue;
            plannedResources.Add(new(adoption.Id, adoption.BindingOrdinal, generation.Id,
                new(generation.ResourceId, generation.EventId, generation.ActorId, generation.OrganizerActorId,
                    generation.StorageObjectId, storage.SingleOrDefault(row => row.Id == generation.StorageObjectId)?.ActorId)));
        }
        var plannedEvents = ImmutableArray.CreateBuilder<EventTarget>();
        foreach (var adoption in eventRows)
        {
            var generation = eventGenerations.SingleOrDefault(row => row.Id == adoption.GenerationId);
            if (generation is not null) plannedEvents.Add(new(adoption.Id, adoption.BindingOrdinal, generation.Id,
                new(generation.EventId, generation.ActorId, generation.OrganizerActorId)));
        }
        EventAuthorityProviderOperations.AuthorityPlan? representativeAuthority = null;
        var contentEventId = await source.ModerationDecisions.AsNoTracking()
            .Where(row => row.Id == selection.DecisionId && row.SubjectKind == ModerationSubjectKind.Content
                && row.ScopeTenantId == tenantId).Select(row => (Guid?)row.SubjectId).SingleOrDefaultAsync(token);
        if (contentEventId is { } contentEvent)
        {
            var approval = await source.ModerationNoticeDisclosureApprovals.AsNoTracking()
                .SingleOrDefaultAsync(row => row.EffectId == effectId && row.HostBindingId == hostId && row.TenantId == tenantId, token);
            if (approval is not null && !approval.Intent.ContentRepresentatives.IsDefaultOrEmpty
                && approval.Intent.ContentRepresentatives.Length <= 32)
            {
                var humans = approval.Intent.ContentRepresentatives.Select(row => row.HumanId).Distinct().Order().ToArray();
                representativeAuthority = new(await EventAuthorityProviderOperations.ReadOwnershipAsync(source, tenantId, [contentEvent], token),
                    await EventAuthorityProviderOperations.ReadAssignmentsAsync(source, tenantId, humans, [contentEvent], token));
            }
        }
        return new(hostId, tenantId, effectId, selection.Id, selection.Revision,
            plannedResources.ToImmutable(), plannedEvents.ToImmutable())
        { RepresentativeAuthority = representativeAuthority };
    }

    private static bool SameRepresentativeAuthority(EventAuthorityProviderOperations.AuthorityPlan? left,
        EventAuthorityProviderOperations.AuthorityPlan? right) => left is null ? right is null : right is not null
        && left.Parents.OrderBy(row => row.Id).SequenceEqual(right.Parents.OrderBy(row => row.Id))
        && left.Assignments.OrderBy(row => row.Id).SequenceEqual(right.Assignments.OrderBy(row => row.Id));

    internal sealed record ResourceTarget(Guid AdoptionId, int Ordinal, Guid GenerationId, EventAuthorityProviderOperations.ResourceAncestry Ancestry);
    internal sealed record EventTarget(Guid AdoptionId, int Ordinal, Guid GenerationId, EventAuthorityProviderOperations.Ownership Ancestry);
}
