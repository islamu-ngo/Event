using System.Collections.Immutable;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Settings;
using Explore.Domain;
using Explore.Domain.Settings;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Services;

/// <summary>
/// Joins only the original concrete settings source owners, not their authority or
/// inventories. It independently pulls private completions before one metadata CAS.
/// </summary>
internal sealed class ManagedSettingsDiscoveryDisclosure(ExploreDbContext context)
{
    private SettingsDiscoveryDisclosureObservation? observation;
    private (OrdinarySettingsMutationStore Store, IOrdinarySettingsSource Source)? ordinary;
    private (AtprotoFederationSettingsMutationStore Store, IAtprotoFederationSettingsSource Source)? federation;
    private (MixedSettingsBatchStore Store, MixedSettingsBatchMutation Mutation)? mixed;
    private ImmutableDictionary<Guid, SettingsDiscoveryDisclosureObservation.RevisionImage>? sealedRevisions;
    private bool failed;

    internal void Attach(OrdinarySettingsMutationStore store, IOrdinarySettingsSource source)
    {
        Attach(store.RequireDiscoveryObservation(source));
        if (mixed is not null || ordinary is not null)
            throw new UnauthorizedAccessException("Only one original ordinary source can join discovery completion.");
        ordinary = (store, source);
    }

    internal void Attach(AtprotoFederationSettingsMutationStore store, IAtprotoFederationSettingsSource source)
    {
        Attach(store.RequireDiscoveryObservation(source));
        if (federation is not null)
            throw new UnauthorizedAccessException("Only one original federation source can join discovery completion.");
        federation = (store, source);
    }

    internal void Attach(MixedSettingsBatchStore store, MixedSettingsBatchMutation mutation)
    {
        Attach(store.RequireDiscoveryObservation(mutation));
        if (ordinary is not null || mixed is not null)
            throw new UnauthorizedAccessException("A mixed discovery source cannot borrow another ordinary owner.");
        mixed = (store, mutation);
    }

    private void Attach(SettingsDiscoveryDisclosureObservation incoming)
    {
        incoming.RequireAcquired();
        if (failed || sealedRevisions is not null || observation is not null && !observation.SameOriginal(incoming))
            throw new UnauthorizedAccessException("Discovery participants must retain the same complete original observation.");
        observation ??= incoming;
    }

    internal bool OwnsPending(object entity) =>
        ordinary is { } ordinaryOwner && ordinaryOwner.Store.OwnsPendingDiscoveryRow(ordinaryOwner.Source, entity)
        || mixed is { } mixedOwner && mixedOwner.Store.OwnsPendingDiscoveryRow(mixedOwner.Mutation, entity);

    internal async Task FlushAsync(CancellationToken token)
    {
        if (failed) throw new UnauthorizedAccessException("A failed settings disclosure requires physical rollback.");
        if (sealedRevisions is not null) { await RequireSealedAsync(token); return; }
        var original = observation ?? throw new UnauthorizedAccessException("The original settings discovery observation is missing.");
        try
        {
            original.RequireAcquired();
            var effects = ImmutableArray.CreateBuilder<HumanSettingsMutation>();
            if (ordinary is { } ordinaryOwner)
                effects.AddRange(await ordinaryOwner.Store.RequireDisclosureEffectsAsync(ordinaryOwner.Source, token));
            if (federation is { } federationOwner)
                effects.AddRange(await federationOwner.Store.RequireDisclosureEffectsAsync(federationOwner.Source, token));
            SettingsDiscoveryEffects mixedEffects = new(false, []);
            if (mixed is { } mixedOwner)
                mixedEffects = await mixedOwner.Store.RequireDisclosureEffectsAsync(mixedOwner.Mutation, token);
            await original.RequireAudienceAsync(token);
            bool root = mixedEffects.AllTenants || effects.Any(effect => effect.Scope == SettingScope.Instance);
            var affected = (root ? original.Audience : effects.Where(effect => effect.TenantId.HasValue)
                    .Select(effect => effect.TenantId!.Value).Concat(mixedEffects.Tenants))
                .Distinct().OrderBy(id => id.ToString("N"), StringComparer.Ordinal).ToArray();
            if (affected.Any(id => !original.Audience.Contains(id)))
                throw new UnauthorizedAccessException("A settings receipt widened its original discovery audience.");

            // Parent/source lifetime is already retained. Take every stable epoch
            // resource BEFORE transaction coverage SELECTs or write conversions.
            foreach (Guid id in original.Audience)
                await RelationalNamedLock.AcquireTransactionAsync(context, $"discovery-epoch:{id:N}", token);
            var current = await original.ReadRevisionsAsync(original.Audience.ToArray(), token);
            SettingsDiscoveryDisclosureObservation.RequireCoverage(original.Audience.ToArray(), current);
            if (!Same(original.Revisions, current))
                throw new UnauthorizedAccessException("Original discovery revision metadata changed outside the settings receipts.");
            foreach (var before in current.Values.OrderBy(row => row.TenantId.ToString("N"), StringComparer.Ordinal))
                if (await RevisionTarget(before).ExecuteUpdateAsync(setters =>
                        setters.SetProperty(row => row.DisclosureEpoch, row => row.DisclosureEpoch), token) != 1)
                    throw new UnauthorizedAccessException("The original discovery metadata could not be current-write fenced.");
            // The no-op current write rejects a newer historical snapshot rather
            // than treating a Serializable label as current-version evidence.
            current = await original.ReadRevisionsAsync(original.Audience.ToArray(), token);
            if (!Same(original.Revisions, current))
                throw new UnauthorizedAccessException("Discovery metadata changed while its current-write fences were acquired.");
            var expected = current.ToBuilder();
            foreach (Guid id in affected)
            {
                var before = current[id];
                long next = checked(before.DisclosureEpoch + 1);
                if (await RevisionTarget(before).ExecuteUpdateAsync(setters =>
                        setters.SetProperty(row => row.DisclosureEpoch, next), token) != 1)
                    throw new UnauthorizedAccessException("Discovery disclosure did not advance exactly one original metadata row.");
                expected[id] = before with { DisclosureEpoch = next };
            }
            sealedRevisions = expected.ToImmutable();
            await RequireSealedAsync(token);
        }
        catch { failed = true; throw; }
    }

    internal Task RequireCompleteAsync(OrdinarySettingsMutationStore store, IOrdinarySettingsSource source, CancellationToken token) =>
        ordinary is { } owner && ReferenceEquals(owner.Store, store) && ReferenceEquals(owner.Source, source)
            ? RequireSealedAsync(token) : throw new UnauthorizedAccessException("The original ordinary discovery participant is required.");

    internal Task RequireCompleteAsync(AtprotoFederationSettingsMutationStore store, IAtprotoFederationSettingsSource source, CancellationToken token) =>
        federation is { } owner && ReferenceEquals(owner.Store, store) && ReferenceEquals(owner.Source, source)
            ? RequireSealedAsync(token) : throw new UnauthorizedAccessException("The original federation discovery participant is required.");

    internal Task RequireCompleteAsync(MixedSettingsBatchStore store, MixedSettingsBatchMutation mutation, CancellationToken token) =>
        mixed is { } owner && ReferenceEquals(owner.Store, store) && ReferenceEquals(owner.Mutation, mutation)
            ? RequireSealedAsync(token) : throw new UnauthorizedAccessException("The original mixed discovery participant is required.");

    private async Task RequireSealedAsync(CancellationToken token)
    {
        if (failed || sealedRevisions is null || observation is null)
            throw new UnauthorizedAccessException("The original settings discovery owner has not completed its atomic metadata effects.");
        await observation.RequireAudienceAsync(token);
        if (!Same(sealedRevisions, await observation.ReadRevisionsAsync(observation.Audience.ToArray(), token)))
            throw new UnauthorizedAccessException("The sealed discovery metadata changed after original settings completion.");
        observation.RequireAcquired();
    }

    private IQueryable<EventDiscoveryRevision> RevisionTarget(SettingsDiscoveryDisclosureObservation.RevisionImage before) =>
        context.Set<EventDiscoveryRevision>().IgnoreTenantFilter(TenantFilterBypassReasons.DiscoveryDisclosureMutation)
            .Where(row => row.Id == before.Id && row.TenantId == before.TenantId
                && row.IdentityEpoch == before.IdentityEpoch && row.DisclosureEpoch == before.DisclosureEpoch);

    private static bool Same(IReadOnlyDictionary<Guid, SettingsDiscoveryDisclosureObservation.RevisionImage> expected,
        IReadOnlyDictionary<Guid, SettingsDiscoveryDisclosureObservation.RevisionImage> actual) =>
        expected.Count == actual.Count && expected.All(pair => actual.TryGetValue(pair.Key, out var row) && row == pair.Value);
}

/// <summary>Dependency data derived only from a concrete source owner's verified effects.</summary>
internal sealed record SettingsDiscoveryEffects(bool AllTenants, ImmutableArray<Guid> Tenants);
