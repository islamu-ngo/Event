using Explore.Application.Contracts.Persistence;
using Explore.Application.Services.Federation;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Settings;

public sealed class AtprotoJetstreamTenantPresentationResolverTests
{
    private const string EventsEnabled = GovernanceSettingKeys.Federation.AtprotoEventsEnabled;

    [Test]
    public async Task ResolveEnabledTenantIds_UsesInstanceLockAndActiveTenantBoundary()
    {
        using var fixture = new FederationReaderFixture();
        Guid enabledOverrideTenantId = fixture.AddTenant();
        Guid disabledOverrideTenantId = fixture.AddTenant();
        Guid inactiveTenantId = fixture.AddTenant(active: false);
        fixture.Instance(EventsEnabled, "false");
        fixture.Override(enabledOverrideTenantId, EventsEnabled, "true");
        fixture.Override(disabledOverrideTenantId, EventsEnabled, "false");
        fixture.Override(inactiveTenantId, EventsEnabled, "true");
        AtprotoJetstreamTenantPresentationResolver sut = fixture.Resolve<AtprotoJetstreamTenantPresentationResolver>();

        IReadOnlyList<Guid> enabled = await sut.ResolveEnabledTenantIdsAsync(CancellationToken.None);

        await Assert.That(enabled).IsEquivalentTo([enabledOverrideTenantId]);

        fixture.Instance(EventsEnabled, "true", locked: true);

        enabled = await sut.ResolveEnabledTenantIdsAsync(CancellationToken.None);

        await Assert.That(enabled).IsEquivalentTo([enabledOverrideTenantId, disabledOverrideTenantId]);

        fixture.DeploymentMode("SingleTenant");
        fixture.Instance(EventsEnabled, "false", locked: true);

        enabled = await sut.ResolveEnabledTenantIdsAsync(CancellationToken.None);

        await Assert.That(enabled).IsEquivalentTo([enabledOverrideTenantId]);

        fixture.RemoveInstance(EventsEnabled);

        enabled = await sut.ResolveEnabledTenantIdsAsync(CancellationToken.None);

        await Assert.That(enabled).IsEmpty();
    }

    [Test]
    public async Task DedicatedInstancePolicyOverridesContradictoryGenericSettings()
    {
        using var fixture = new FederationReaderFixture();
        Guid tenantId = fixture.AddTenant();
        fixture.LegacyInstance(EventsEnabled, "false", locked: true);
        fixture.DedicatedInstance(EventsEnabled, "true", locked: true);

        IReadOnlyList<Guid> enabled = await fixture.Resolve<AtprotoJetstreamTenantPresentationResolver>()
            .ResolveEnabledTenantIdsAsync(CancellationToken.None);

        await Assert.That(enabled).IsEquivalentTo([tenantId]);
    }

    [Test]
    public async Task DedicatedTenantOverrideWinsWithoutLeakingUnrelatedFederationKeys()
    {
        using var fixture = new FederationReaderFixture();
        Guid enabledTenantId = fixture.AddTenant();
        Guid disabledTenantId = fixture.AddTenant();
        fixture.Instance(EventsEnabled, "false");
        fixture.DedicatedOverride(enabledTenantId, EventsEnabled, "true");
        fixture.DedicatedOverride(disabledTenantId, EventsEnabled, "false");
        fixture.DedicatedOverride(disabledTenantId, GovernanceSettingKeys.Federation.AtprotoEventsBackfillEnabled, "true");

        IReadOnlyList<Guid> enabled = await fixture.Resolve<AtprotoJetstreamTenantPresentationResolver>()
            .ResolveEnabledTenantIdsAsync(CancellationToken.None);

        await Assert.That(enabled).IsEquivalentTo([enabledTenantId]);
    }

    [Test]
    public async Task MissingDedicatedInstanceRemainsDisabledDespiteStaleGenericRowsAndTenantOptIn()
    {
        using var fixture = new FederationReaderFixture();
        Guid tenantId = fixture.AddTenant();
        fixture.LegacyInstance(EventsEnabled, "true");
        fixture.DedicatedOverride(tenantId, EventsEnabled, "true");

        IReadOnlyList<Guid> enabled = await fixture.Resolve<AtprotoJetstreamTenantPresentationResolver>()
            .ResolveEnabledTenantIdsAsync(CancellationToken.None);

        await Assert.That(enabled).IsEmpty();
    }

    [Test]
    public async Task InheritedEnablementIncludesOnlyActiveTenantsInStableOrder()
    {
        using var fixture = new FederationReaderFixture();
        fixture.AddTenant(FederationReaderFixture.TenantB);
        fixture.AddTenant(FederationReaderFixture.TenantA);
        Guid inactive = fixture.AddTenant(active: false);
        Guid optedOut = fixture.AddTenant();
        fixture.Instance(EventsEnabled, "true");
        fixture.Override(optedOut, EventsEnabled, "false");
        fixture.Override(inactive, EventsEnabled, "true");

        IReadOnlyList<Guid> enabled = await fixture.Resolve<AtprotoJetstreamTenantPresentationResolver>()
            .ResolveEnabledTenantIdsAsync(CancellationToken.None);

        await Assert.That(enabled.SequenceEqual([FederationReaderFixture.TenantA, FederationReaderFixture.TenantB])).IsTrue();
    }
}

internal sealed class FederationReaderFixture : IDisposable
{
    internal static readonly Guid TenantA = Guid.Parse("11111111-1111-7111-8111-111111111111");
    internal static readonly Guid TenantB = Guid.Parse("22222222-2222-7222-8222-222222222222");
    internal static readonly Guid TenantC = Guid.Parse("33333333-3333-7333-8333-333333333333");
    private static readonly DateTime ObservedAt = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);
    private readonly List<Tenant> _tenants = [];
    private readonly LegacySettingsStore _legacy = new();
    private readonly DedicatedSettingsStore _dedicated = new();
    private readonly ServiceProvider _services;

    internal FederationReaderFixture()
    {
        DeploymentMode("MultiTenant");
        var services = new ServiceCollection();
        services.AddSingleton<ITenantRepository>(new TenantDirectory(_tenants));
        services.AddSingleton<ISystemSettingRepository>(_legacy);
        services.AddSingleton<IAtprotoFederationGlobalReadRepository>(_dedicated);
        services.AddTransient<AtprotoJetstreamTenantPresentationResolver>();
        services.AddTransient<AtprotoPdsRecoveryPolicyResolver>();
        _services = services.BuildServiceProvider();
    }

    internal T Resolve<T>() where T : notnull => _services.GetRequiredService<T>();

    internal Guid AddTenant(Guid? id = null, bool active = true)
    {
        Guid tenantId = id ?? Guid.CreateVersion7();
        int statusId = (int)(active ? TenantStatusEnum.Active : TenantStatusEnum.Suspended);
        _tenants.Add(new Tenant
        {
            Id = tenantId,
            FullName = "Federation test tenant",
            Slug = tenantId.ToString("N"),
            TenantStatusId = statusId,
            TenantStatus = new TenantStatus
            {
                Id = statusId,
                MasterCode = active ? "ACTIVE" : "SUSPENDED",
                FullName = active ? "Active" : "Suspended",
                IsActiveState = active
            },
            CreatedAt = ObservedAt
        });
        return tenantId;
    }

    internal void DeploymentMode(string mode) =>
        LegacyInstance(GovernanceSettingKeys.Deployment.Mode, $"\"{mode}\"");

    internal void Instance(string key, string value, bool locked = false)
    {
        LegacyInstance(key, value, locked);
        DedicatedInstance(key, value, locked);
    }

    internal void LegacyInstance(string key, string value, bool locked = false)
    {
        _legacy.Instance.RemoveAll(row => row.SettingKey == key);
        _legacy.Instance.Add(new SystemSetting
        {
            Id = Guid.CreateVersion7(),
            SettingKey = key,
            Value = value,
            IsLocked = locked,
            CreatedAt = ObservedAt
        });
    }

    internal void DedicatedInstance(string key, string value, bool locked = false)
    {
        _dedicated.Instance.RemoveAll(row => row.SettingKey == key);
        _dedicated.Instance.Add(AtprotoFederationInstanceSetting.Create(key, value, locked, ObservedAt, null));
    }

    internal void RemoveInstance(string key)
    {
        _legacy.Instance.RemoveAll(row => row.SettingKey == key);
        _dedicated.Instance.RemoveAll(row => row.SettingKey == key);
    }

    internal void Override(Guid tenantId, string key, string value) => DedicatedOverride(tenantId, key, value);

    internal void DedicatedOverride(Guid tenantId, string key, string value)
    {
        _dedicated.Overrides.RemoveAll(row => row.TenantId == tenantId && row.SettingKey == key);
        _dedicated.Overrides.Add(AtprotoFederationTenantSetting.Create(tenantId, key, value, false, ObservedAt, null));
    }

    internal void ChangeRecoveryOverrideAfterNextObservation(Guid tenantId, string enabled, string mode)
    {
        (string Key, string Value)[] next =
        [
            (GovernanceSettingKeys.Federation.AtprotoEventsBackfillEnabled, enabled),
            (GovernanceSettingKeys.Federation.AtprotoEventsBackfillMode, mode)
        ];
        _dedicated.AfterObservation = next.Select(row => AtprotoFederationTenantSetting.Create(
            tenantId, row.Key, row.Value, false, ObservedAt, null)).ToList();
    }

    public void Dispose() => _services.Dispose();

    private sealed class DedicatedSettingsStore : IAtprotoFederationGlobalReadRepository
    {
        internal List<AtprotoFederationInstanceSetting> Instance { get; } = [];
        internal List<AtprotoFederationTenantSetting> Overrides { get; } = [];
        internal List<AtprotoFederationTenantSetting>? AfterObservation { get; set; }

        public Task<IReadOnlyList<AtprotoFederationInstanceSetting>> GetInstanceAsync(
            IReadOnlyCollection<string> keys, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<AtprotoFederationInstanceSetting>>(
                Instance.Where(row => keys.Contains(row.SettingKey, StringComparer.Ordinal)).ToArray());
        }

        public Task<IReadOnlyList<AtprotoFederationTenantSetting>> GetOverridesAsync(
            IReadOnlyCollection<string> keys, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AtprotoFederationTenantSetting[] observed = Overrides
                .Where(row => keys.Contains(row.SettingKey, StringComparer.Ordinal)).ToArray();
            if (AfterObservation is not null)
            {
                Overrides.Clear();
                Overrides.AddRange(AfterObservation);
                AfterObservation = null;
            }
            return Task.FromResult<IReadOnlyList<AtprotoFederationTenantSetting>>(observed);
        }
    }

    private sealed class LegacySettingsStore : ISystemSettingRepository
    {
        internal List<SystemSetting> Instance { get; } = [];

        public Task<SystemSetting?> GetByKey(string key, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Instance.SingleOrDefault(row => row.SettingKey == key));
        }

        public Task<List<SystemSetting>> GetAllSettings(string? category = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> IsLocked(string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string?> UpsertAsync(SystemSetting setting, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string?> UpsertInCurrentTransactionAsync(SystemSetting setting, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string?> UpsertLockAsync(SystemSetting setting, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class TenantDirectory(List<Tenant> tenants) : ITenantRepository
    {
        public Task<IReadOnlyList<Tenant>> GetActiveAsNoTrackingAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<Tenant>>(tenants.Where(tenant => tenant.TenantStatus.IsActiveState).ToArray());
        }

        public Task<Tenant?> GetById(Guid id) => throw new NotSupportedException();
        public Task<IReadOnlyList<Tenant>> GetAll() => throw new NotSupportedException();
        public Task<(IReadOnlyList<Tenant> Items, int TotalCount)> GetAllPaged(int pageNumber, int pageSize) => throw new NotSupportedException();
        public Task<bool> Exists(Guid id) => throw new NotSupportedException();
        public Task<Tenant> Create(Tenant entity) => throw new NotSupportedException();
        public Task Update(Tenant entity) => throw new NotSupportedException();
        public Task Delete(Tenant entity) => throw new NotSupportedException();
        public Task AcquireHumanDeletionReservationAsync(Guid tenantId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> DeleteHumanAsync(Guid tenantId, IPreparedHumanTenantMutation prepared, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Tenant?> GetTenantBySlug(string slug) => throw new NotSupportedException();
        public Task<IReadOnlyList<Tenant>> GetBySlugsAsNoTrackingAsync(IReadOnlyCollection<string> slugs, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> GetActiveTenantCountAsync() => throw new NotSupportedException();
        public Task<IReadOnlyList<Tenant>> GetAllActiveForConfigurationManifestExportAsync(int maximumCount, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Tenant?> GetByIdAsNoTrackingAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ApplyConfigurationImportIdentityAsync(Guid tenantId, string displayName, Guid actorUserId, DateTime occurredAt, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> TryTransitionStatusAsync(Guid id, int expectedStatusId, int newStatusId, DateTime updatedAt, Guid updatedBy, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
