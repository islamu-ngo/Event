using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Settings;
using Explore.Domain.Settings.Definitions;

namespace Event.Application.UnitTests.Settings;

public sealed class AtprotoFederationSettingFactsTests
{
    private const string Events = GovernanceSettingKeys.Federation.AtprotoEventsEnabled;
    private const string Backfill = GovernanceSettingKeys.Federation.AtprotoEventsBackfillEnabled;
    private const string Mode = GovernanceSettingKeys.Federation.AtprotoEventsBackfillMode;
    private static readonly DateTime CreatedAt = new(2026, 10, 18, 0, 0, 0, DateTimeKind.Utc);

    [Test]
    [Arguments(Events, "true")]
    [Arguments(Events, "false")]
    [Arguments(Backfill, "true")]
    [Arguments(Backfill, "false")]
    [Arguments(Mode, "\"downtime_only\"")]
    [Arguments(Mode, "\"FuLl\"")]
    [Arguments(Mode, "\"DOWNTIME_ONLY\"")]
    public async Task FiniteValuesRetainTheirBytesAndRegistryMetadata(string key, string value)
    {
        var instance = Instance(key, value);
        var facts = AtprotoFederationSettingFacts.Create(key, instance, null, null);
        var definition = SettingRegistry.Get(key)!;
        var resolved = HierarchicalSettingMerge.Resolve(key, facts)!;

        await Assert.That(facts.System!.Value).IsEqualTo(value);
        await Assert.That(resolved.Value).IsEqualTo(value);
        await Assert.That(resolved.ValueType).IsEqualTo(definition.ValueType);
        await Assert.That(resolved.Description).IsEqualTo(definition.Description);
        await Assert.That(resolved.Category).IsEqualTo(AtprotoFederationSettingDefinitions.Category);
        await Assert.That(resolved.Source).IsEqualTo(SettingSource.SystemDefault);
        await Assert.That(facts.HasTenantContext).IsFalse();
        await Assert.That(facts.Tenant).IsNull();
        await Assert.That(facts.Organization).IsNull();
        await Assert.That(facts.Group).IsNull();
        await Assert.That(facts.User).IsNull();
        await Assert.That(facts.SmtpDelegationValue).IsNull();
        if (key == Mode)
        {
            var allowed = JsonSerializer.Deserialize<string[]>(facts.System.AllowedValues!)!;
            await Assert.That(allowed).IsEquivalentTo(["downtime_only", "full"]);
        }
        else
            await Assert.That(facts.System.AllowedValues).IsNull();
    }

    [Test]
    [Arguments(Events, "false")]
    [Arguments(Backfill, "false")]
    [Arguments(Mode, "\"downtime_only\"")]
    public async Task MissingRowsRemainAbsentAndUseTheRegisteredDefault(string key, string expected)
    {
        foreach (Guid? tenantId in new Guid?[] { null, Guid.CreateVersion7() })
        {
            var facts = AtprotoFederationSettingFacts.Create(key, null, null, tenantId);
            var resolved = HierarchicalSettingMerge.Resolve(key, facts)!;

            await Assert.That(facts.System).IsNull();
            await Assert.That(facts.Tenant).IsNull();
            await Assert.That(facts.HasTenantContext).IsEqualTo(tenantId.HasValue);
            await Assert.That(resolved.Value).IsEqualTo(expected);
            await Assert.That(resolved.Source).IsEqualTo(SettingSource.SystemDefault);
            await Assert.That(resolved.IsLocked).IsFalse();
            await Assert.That(resolved.Description).IsEqualTo(SettingRegistry.Get(key)!.Description);
        }
    }

    [Test]
    public async Task TenantOverrideCanSupplyTheGenericHierarchyWhenInstanceRowIsAbsent()
    {
        Guid tenantId = Guid.CreateVersion7();
        var tenant = Tenant(tenantId, Events, "true");

        var facts = AtprotoFederationSettingFacts.Create(Events, null, tenant, tenantId);
        var resolved = HierarchicalSettingMerge.Resolve(Events, facts)!;

        await Assert.That(facts.System).IsNull();
        await Assert.That(resolved.Value).IsEqualTo("true");
        await Assert.That(resolved.Source).IsEqualTo(SettingSource.TenantOverride);
        await Assert.That(resolved.IsLocked).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task InstanceLockDominatesTheTenantWithoutDeletingItsStoredValue(bool tenantLocked)
    {
        Guid tenantId = Guid.CreateVersion7();
        var instance = Instance(Events, "false", locked: true);
        var tenant = Tenant(tenantId, Events, "true", tenantLocked);

        var facts = AtprotoFederationSettingFacts.Create(Events, instance, tenant, tenantId);
        var resolved = HierarchicalSettingMerge.Resolve(Events, facts)!;

        await Assert.That(resolved.Value).IsEqualTo("false");
        await Assert.That(resolved.Source).IsEqualTo(SettingSource.SystemLocked);
        await Assert.That(resolved.IsLocked).IsTrue();
        await Assert.That(facts.Tenant!.Value).IsEqualTo("true");
        await Assert.That(facts.Tenant.IsLocked).IsEqualTo(tenantLocked);
        await Assert.That(tenant.Value).IsEqualTo("true");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task TenantOverrideRetainsItsOwnLockSourceAndModeCasing(bool locked)
    {
        Guid tenantId = Guid.CreateVersion7();
        var facts = AtprotoFederationSettingFacts.Create(Mode,
            Instance(Mode, "\"downtime_only\""), Tenant(tenantId, Mode, "\"FuLl\"", locked), tenantId);

        var resolved = HierarchicalSettingMerge.Resolve(Mode, facts)!;

        await Assert.That(resolved.Value).IsEqualTo("\"FuLl\"");
        await Assert.That(resolved.Source).IsEqualTo(locked ? SettingSource.TenantLocked : SettingSource.TenantOverride);
        await Assert.That(resolved.IsLocked).IsEqualTo(locked);
        await Assert.That(resolved.ValueType).IsEqualTo(SettingValueType.String);
        await Assert.That(resolved.Description).IsEqualTo(AtprotoFederationSettingDefinitions.EventsBackfillMode.Description);
    }

    [Test]
    [Arguments("")]
    [Arguments("federation.unknown")]
    [Arguments(GovernanceSettingKeys.Federation.AtprotoEventValidationProfile)]
    [Arguments(GovernanceSettingKeys.Federation.AtprotoPublishMyEvents)]
    public async Task UnmovedOrUnknownKeyCannotEnterTheDedicatedFacts(string key) =>
        await Assert.That(() => AtprotoFederationSettingFacts.Create(key, null, null, null))
            .Throws<ArgumentException>();

    [Test]
    public async Task InstanceRowForAnotherDedicatedKeyIsRejected() =>
        await Assert.That(() => AtprotoFederationSettingFacts.Create(Events, Instance(Backfill, "true"), null, null))
            .Throws<ArgumentException>();

    [Test]
    public async Task TenantRowForAnotherDedicatedKeyIsRejected()
    {
        Guid tenantId = Guid.CreateVersion7();
        await Assert.That(() => AtprotoFederationSettingFacts.Create(Events, null, Tenant(tenantId, Backfill, "true"), tenantId))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task AnotherTenantsOverrideCannotBeMappedToTheRequestedTenant() =>
        await Assert.That(() => AtprotoFederationSettingFacts.Create(Events, null,
                Tenant(Guid.CreateVersion7(), Events, "true"), Guid.CreateVersion7()))
            .Throws<ArgumentException>();

    [Test]
    public async Task TenantOverrideWithoutTenantContextIsRejected() =>
        await Assert.That(() => AtprotoFederationSettingFacts.Create(Events, null,
                Tenant(Guid.CreateVersion7(), Events, "true"), null))
            .Throws<ArgumentException>();

    [Test]
    public async Task EmptyTenantContextIsNotAnInstanceContext() =>
        await Assert.That(() => AtprotoFederationSettingFacts.Create(Events, null, null, Guid.Empty))
            .Throws<ArgumentException>();

    [Test]
    [Arguments(Events, "TRUE", true)]
    [Arguments(Events, "\"true\"", false)]
    [Arguments(Backfill, "false ", true)]
    [Arguments(Backfill, "null", false)]
    [Arguments(Mode, "\"incremental\"", true)]
    [Arguments(Mode, "true", false)]
    [Arguments(Mode, "\"full\" ", true)]
    [Arguments(Mode, "\"\\u0066ull\"", false)]
    [Arguments(Mode, "\"full\" true", true)]
    [Arguments(Events, "{\"secretRef\":\"policy\"}", false)]
    public async Task MaterializedInvalidValuesFailClosedInsteadOfBecomingDefaults(string key, string value, bool instanceRow)
    {
        Guid tenantId = Guid.CreateVersion7();
        string valid = key == Mode ? "\"downtime_only\"" : "false";
        var instance = Instance(key, valid);
        var tenant = Tenant(tenantId, key, valid);
        // Reproduce materialization of corrupt persisted data without relaxing the Domain factory.
        // This targets a fixed mapped property, not source text or a production reflection path.
        if (instanceRow)
            typeof(AtprotoFederationInstanceSetting).GetProperty(nameof(AtprotoFederationInstanceSetting.Value))!.SetValue(instance, value);
        else
        {
            instance.SetLock(true, CreatedAt, null);
            typeof(AtprotoFederationTenantSetting).GetProperty(nameof(AtprotoFederationTenantSetting.Value))!.SetValue(tenant, value);
        }

        await Assert.That(() => AtprotoFederationSettingFacts.Create(key, instance, tenant, tenantId))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task FactsAreAnImmutableObservationNotAnAliasOfMutableEntities()
    {
        Guid tenantId = Guid.CreateVersion7();
        var instance = Instance(Events, "false");
        var tenant = Tenant(tenantId, Events, "true");
        var facts = AtprotoFederationSettingFacts.Create(Events, instance, tenant, tenantId);

        instance.SetValue("true", CreatedAt, null);
        instance.SetLock(true, CreatedAt, null);
        tenant.SetValue("false", CreatedAt, null);
        tenant.SetLock(true, CreatedAt, null);

        await Assert.That(facts.System!.Value).IsEqualTo("false");
        await Assert.That(facts.System.IsLocked).IsFalse();
        await Assert.That(facts.Tenant!.Value).IsEqualTo("true");
        await Assert.That(facts.Tenant.IsLocked).IsFalse();
        await Assert.That(HierarchicalSettingMerge.Resolve(Events, facts)!.Value).IsEqualTo("true");
    }

    private static AtprotoFederationInstanceSetting Instance(string key, string value, bool locked = false) =>
        AtprotoFederationInstanceSetting.Create(key, value, locked, CreatedAt, null);

    private static AtprotoFederationTenantSetting Tenant(Guid tenantId, string key, string value, bool locked = false) =>
        AtprotoFederationTenantSetting.Create(tenantId, key, value, locked, CreatedAt, null);
}
