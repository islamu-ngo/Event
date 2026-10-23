using System.Collections.Immutable;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.EmailDispatch;
using Explore.Application.Features.Settings;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Settings;
using Explore.Persistence.Database;
using Explore.Persistence.Schema;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Persistence.Security;

/// <summary>
/// Retains the original human command, excluded dedicated rows and absences, and
/// independently verified private effects on one non-replaying physical attempt.
/// The public source interface is never accepted as authority by itself.
/// </summary>
public sealed partial class AtprotoFederationSettingsMutationStore(
    ExploreDbContext context, IServiceProvider services, TimeProvider clock)
    : IAtprotoFederationSettingsMutationStore, IDisposable
{
    private readonly ExploreDbContextExecutionLease selection = services.GetRequiredService<ExploreDbContextExecutionLease>();
    private readonly List<Source> sources = [];

    public async Task<IAtprotoFederationSettingsSource> ObserveAsync(
        OrdinarySettingsMutation mutation, HumanCommitSubject subject,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        if (!mutation.Effects.Any(effect => AtprotoFederationSettingPolicy.IsDedicatedKey(effect.Key)))
            throw new UnauthorizedAccessException("The full original command has no dedicated federation effects.");
        return await ObserveAsync(new Source(this, context, selection, mutation, subject,
            NormalizeTimestamp(clock.GetUtcNow().UtcDateTime)), cancellationToken);
    }

    public async Task<IAtprotoFederationSettingsSource> ObserveAsync(
        MixedSettingsBatchMutation mutation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        services.GetRequiredService<IHumanEmailMutationPreparation>().RequireMixedSource(mutation.Source);
        if (!mutation.Original.Values.Keys.Any(AtprotoFederationSettingPolicy.IsDedicatedKey))
            throw new UnauthorizedAccessException("The full original mixed command has no dedicated federation candidates.");
        return await ObserveAsync(new Source(this, context, selection, mutation,
            NormalizeTimestamp(clock.GetUtcNow().UtcDateTime)), cancellationToken);
    }

    private async Task<Source> ObserveAsync(Source issued, CancellationToken token)
    {
        sources.Add(issued);
        try
        {
            issued.RequireSelection();
            if (context.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null)
                throw new UnauthorizedAccessException("Federation observation must precede the original transaction.");
            if (!context.Database.IsRelational())
                throw new NotSupportedException("Federation mutation requires a relational selected owner.");
            await context.Database.OpenConnectionAsync(token);
            issued.CaptureOpenConnection();
            await RequirePermissionsAsync(issued, token);
            issued.Expected = await ReadAsync(issued, token);
            issued.Original = issued.Expected;
            issued.Discovery = issued.Ordinary is { } ordinary
                ? await SettingsDiscoveryDisclosureObservation.ObserveAsync(context, selection, ordinary, issued.Subject, token)
                : await SettingsDiscoveryDisclosureObservation.ObserveAsync(context, selection, issued.Mixed!.Original, issued.Subject, token);
            issued.RequireSelection();
            return issued;
        }
        catch { issued.Dispose(); throw; }
    }

    public async Task AcquireAsync(IAtprotoFederationSettingsSource source,
        CancellationToken cancellationToken = default)
    {
        var issued = RequireIssued(source);
        if (Interlocked.CompareExchange(ref issued.State, 1, 0) != 0)
            throw new UnauthorizedAccessException("A federation observation belongs to one physical attempt.");
        try
        {
            issued.Transaction = issued.RequireTransaction();
            await RequirePermissionsAsync(issued, cancellationToken);
            // The preparation already owns PolicySourceSetFence, including
            // SQLite's actual write reservation. Current target writes fence
            // rows and range absences without changing their audit metadata.
            if (issued.TenantId is { } tenant)
                await context.Set<AtprotoFederationTenantSetting>().Where(row => row.TenantId == tenant)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Value, row => row.Value), cancellationToken);
            if (issued.TenantId is null || issued.Mixed is not null)
                await context.Set<AtprotoFederationInstanceSetting>()
                    .Where(row => issued.TenantId == null || issued.ParentKeys.Contains(row.SettingKey))
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Value, row => row.Value), cancellationToken);
            // Runtime has no parent-instance UPDATE grant. Parent freshness also
            // depends on the shared original pre-BEGIN key locks held through
            // settlement. Direct Global edits that bypass the API's cooperative
            // protocol are outside this guarantee; no privileged parent lock is used.
            await RequireImageAsync(issued, cancellationToken);
            if (issued.Discovery is not null)
            {
                await issued.Discovery.BindAsync(cancellationToken);
                context.DisclosureMutations.EnlistSettingsSource(this, source);
            }
        }
        catch { issued.State = 2; throw; }
    }

    public async Task VerifyAsync(IAtprotoFederationSettingsSource source,
        CancellationToken cancellationToken = default)
    {
        var issued = RequireIssued(source);
        try
        {
            issued.RequireAcquired();
            if (issued.Writing)
                throw new UnauthorizedAccessException("A federation effect did not finish its private persisted-image proof.");
            await RequirePermissionsAsync(issued, cancellationToken);
            await RequireImageAsync(issued, cancellationToken);
        }
        catch { issued.State = 2; throw; }
    }

    public async Task RequireCompleteAsync(IAtprotoFederationSettingsSource source,
        CancellationToken cancellationToken = default)
    {
        var issued = RequireIssued(source);
        try
        {
            await RequireEffectsCompleteAsync(source, cancellationToken);
            if (issued.Discovery is not null)
                await context.DisclosureMutations.RequireSettingsCompleteAsync(this, source, cancellationToken);
        }
        catch { issued.State = 2; throw; }
    }

    private async Task RequireEffectsCompleteAsync(IAtprotoFederationSettingsSource source, CancellationToken cancellationToken)
    {
        var issued = RequireIssued(source);
        try
        {
            await VerifyAsync(issued, cancellationToken);
            if (issued.Effects is not { } effects)
                throw new UnauthorizedAccessException("The genuine mixed compiler did not freeze its accepted federation partition.");
            foreach (var effect in effects)
                if (!issued.Completed.ContainsKey(effect.Key) && !ProvesRefusal(issued, effect))
                    throw new UnauthorizedAccessException("An accepted federation effect has no private completion or excluded refusal.");
        }
        catch { issued.State = 2; throw; }
    }

    public void Expire(IAtprotoFederationSettingsSource source) => RequireIssued(source).Dispose();

    public void Dispose()
    {
        foreach (var source in sources) source.Dispose();
        sources.Clear();
    }

    private async Task RequirePermissionsAsync(Source issued, CancellationToken token)
    {
        issued.RequireSelection();
        await PostgresAtprotoFederationSettingPermissions.RequireReadAsync(context,
            selection.HumanScope?.Enrollment, globalOnly: issued.Mixed is not null || issued.TenantId is null, token);
        if (context.Database.IsNpgsql())
            await PostgresDiscoveryMetadataPermissions.RequireCurrentAsync(context,
                selection.HumanScope?.Enrollment
                    ?? throw new UnauthorizedAccessException("The original federation enrollment is missing."),
                globalOnly: issued.TenantId is null, token);
        issued.RequireSelection();
    }

    private async Task RequireImageAsync(Source issued, CancellationToken token)
    {
        issued.RequireAcquired();
        if (issued.Expected is null || !issued.Expected.Same(await ReadAsync(issued, token)))
            throw new UnauthorizedAccessException("The original federation parent, target, absence or canonical effects changed.");
        issued.RequireAcquired();
    }

    private async Task<DataImage> ReadAsync(Source issued, CancellationToken token)
    {
        issued.RequireSelection();
        var keys = issued.ParentKeys;
        var instance = await context.Set<AtprotoFederationInstanceSetting>().AsNoTracking()
            .Where(row => issued.TenantId == null || keys.Contains(row.SettingKey)).ToListAsync(token);
        var tenant = issued.TenantId is { } id
            ? await context.Set<AtprotoFederationTenantSetting>().AsNoTracking()
                .Where(row => row.TenantId == id).ToListAsync(token) : [];
        if (instance.Any(row => !AtprotoFederationSettingPolicy.IsDedicatedKey(row.SettingKey))
            || tenant.Any(row => !AtprotoFederationSettingPolicy.IsDedicatedKey(row.SettingKey)))
            throw new UnauthorizedAccessException("The dedicated federation row vocabulary changed.");
        var instanceByKey = instance.ToDictionary(row => row.SettingKey, StringComparer.Ordinal);
        var tenantByKey = tenant.ToDictionary(row => row.SettingKey, StringComparer.Ordinal);
        return new(instance.ToImmutableDictionary(row => row.SettingKey, AtprotoFederationSettingImage.Capture, StringComparer.Ordinal),
            tenant.ToImmutableDictionary(row => row.SettingKey, AtprotoFederationSettingImage.Capture, StringComparer.Ordinal),
            keys.ToImmutableDictionary(key => key, key => AtprotoFederationSettingFacts.Create(key,
                instanceByKey.GetValueOrDefault(key), tenantByKey.GetValueOrDefault(key), issued.TenantId), StringComparer.Ordinal),
            keys.ToImmutableDictionary(key => key, key => AtprotoFederationSettingFacts.Create(key,
                instanceByKey.GetValueOrDefault(key), null, null), StringComparer.Ordinal));
    }

    private DateTime NormalizeTimestamp(DateTime value) =>
        context.Database.IsNpgsql() || context.Database.ProviderName == RelationalNamedLock.MySqlProvider
            ? new DateTime(value.Ticks - value.Ticks % 10, DateTimeKind.Utc) : value;

    private Source RequireIssued(IAtprotoFederationSettingsSource source) =>
        source is Source issued && ReferenceEquals(issued.Store, this) ? issued
            : throw new UnauthorizedAccessException("The original scoped federation store must issue this source.");

    internal void FreezeMixedAccepted(IAtprotoFederationSettingsSource source, MixedSettingsBatchMutation mutation)
    {
        var issued = RequireIssued(source);
        issued.RequireAcquired();
        var originalOwner = MixedSettingsBatchStore.RequireOriginal(context, services, mutation);
        if (!ReferenceEquals(issued.Mixed, mutation)
            || !ReferenceEquals(mutation.FederationSource, source) || issued.Effects is not null)
            throw new UnauthorizedAccessException("Only the genuine active mixed compiler can freeze its dedicated partition.");
        var effects = mutation.SettingsEffects().Where(effect => AtprotoFederationSettingPolicy.IsDedicatedKey(effect.Key))
            .ToImmutableArray();
        foreach (var effect in effects) issued.RequireEffectShape(effect);
        issued.OccurredAt = NormalizeTimestamp(originalOwner.OccurredAt);
        issued.Effects = effects;
    }

    internal ResolvedSetting ResolveMixedOriginal(IAtprotoFederationSettingsSource source,
        MixedSettingsBatchMutation mutation, string key, bool instanceOnly = false)
    {
        var issued = RequireIssued(source);
        issued.RequireAcquired();
        mutation.RequireActive();
        if (!ReferenceEquals(issued.Mixed, mutation)
            || !ReferenceEquals(mutation.FederationSource, source) || !issued.ParentKeys.Contains(key, StringComparer.Ordinal)
            || issued.Original is null)
            throw new UnauthorizedAccessException("Dedicated resolution must retain its genuine original mixed observation.");
        return HierarchicalSettingMerge.Resolve(key,
            (instanceOnly ? issued.Original.InstanceFacts : issued.Original.Facts)[key])
            ?? throw new UnauthorizedAccessException("The original dedicated setting definition is missing.");
    }

    internal Task RequireMixedEffectsCompleteAsync(IAtprotoFederationSettingsSource source,
        MixedSettingsBatchMutation mutation, CancellationToken token)
    {
        var issued = RequireIssued(source);
        MixedSettingsBatchStore.RequireOriginal(context, services, mutation);
        if (!ReferenceEquals(issued.Mixed, mutation) || !ReferenceEquals(mutation.FederationSource, source))
            throw new UnauthorizedAccessException("The original mixed dedicated completion owner is required.");
        return RequireEffectsCompleteAsync(source, token);
    }

    internal SettingsDiscoveryDisclosureObservation RequireDiscoveryObservation(IAtprotoFederationSettingsSource source)
    {
        var issued = RequireIssued(source);
        issued.RequireAcquired();
        return issued.Discovery ?? throw new UnauthorizedAccessException("The original federation discovery observation is missing.");
    }

    internal async Task<ImmutableArray<HumanSettingsMutation>> RequireDisclosureEffectsAsync(
        IAtprotoFederationSettingsSource source, CancellationToken token)
    {
        await RequireEffectsCompleteAsync(source, token);
        return RequireIssued(source).Completed.Values.Where(receipt => receipt.Applied)
            .Select(receipt => receipt.Effect).ToImmutableArray();
    }

    private static bool ProvesRefusal(Source source, HumanSettingsMutation effect)
    {
        var before = source.Expected!.Target(effect);
        return effect.Scope == SettingScope.Tenant && source.Expected.Instance.GetValueOrDefault(effect.Key)?.IsLocked == true
            || effect.Kind == HumanSettingsMutationKind.Remove && before is null
            || effect.Kind == HumanSettingsMutationKind.SetLock
                && (before is null || effect.Scope == SettingScope.Tenant && before.IsLocked == effect.IsLocked);
    }

    private sealed record DataImage(
        ImmutableDictionary<string, AtprotoFederationSettingImage> Instance,
        ImmutableDictionary<string, AtprotoFederationSettingImage> Tenant,
        ImmutableDictionary<string, SettingMergeFacts> Facts,
        ImmutableDictionary<string, SettingMergeFacts> InstanceFacts)
    {
        internal AtprotoFederationSettingImage? Target(HumanSettingsMutation effect) =>
            (effect.Scope == SettingScope.Instance ? Instance : Tenant).GetValueOrDefault(effect.Key);

        internal bool Same(DataImage other) => Instance.Count == other.Instance.Count && Tenant.Count == other.Tenant.Count
            && Instance.All(pair => other.Instance.TryGetValue(pair.Key, out var row) && row == pair.Value)
            && Tenant.All(pair => other.Tenant.TryGetValue(pair.Key, out var row) && row == pair.Value);

        internal DataImage Apply(HumanSettingsMutation effect, AtprotoFederationSettingImage? after)
        {
            var rows = effect.Scope == SettingScope.Instance ? Instance : Tenant;
            rows = after is null ? rows.Remove(effect.Key) : rows.SetItem(effect.Key, after);
            return effect.Scope == SettingScope.Instance ? this with { Instance = rows } : this with { Tenant = rows };
        }
    }
}
