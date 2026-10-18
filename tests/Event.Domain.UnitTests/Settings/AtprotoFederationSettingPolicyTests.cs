using Explore.Domain.Constants;
using Explore.Domain.Settings;

namespace Event.Domain.UnitTests.Settings;

public sealed class AtprotoFederationSettingPolicyTests
{
    private const string EventsEnabled = GovernanceSettingKeys.Federation.AtprotoEventsEnabled;
    private const string BackfillEnabled = GovernanceSettingKeys.Federation.AtprotoEventsBackfillEnabled;
    private const string BackfillMode = GovernanceSettingKeys.Federation.AtprotoEventsBackfillMode;
    private static readonly DateTime CreatedAt = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime UpdatedAt = CreatedAt.AddMinutes(1);

    [Test]
    [Arguments(EventsEnabled, "false")]
    [Arguments(EventsEnabled, "true")]
    [Arguments(BackfillEnabled, "false")]
    [Arguments(BackfillEnabled, "true")]
    [Arguments(BackfillMode, "\"downtime_only\"")]
    [Arguments(BackfillMode, "\"full\"")]
    [Arguments(BackfillMode, "\"DOWNTIME_ONLY\"")]
    [Arguments(BackfillMode, "\"FuLl\"")]
    public async Task DedicatedRowsAcceptOnlyTheMovedPolicyAtInstanceAndTenantScopes(string key, string value)
    {
        var tenantId = Guid.CreateVersion7();
        var actorId = Guid.CreateVersion7();

        var instance = AtprotoFederationInstanceSetting.Create(key, value, true, CreatedAt, actorId);
        var tenant = AtprotoFederationTenantSetting.Create(tenantId, key, value, false, CreatedAt, actorId);

        await Assert.That(AtprotoFederationSettingPolicy.IsDedicatedKey(key)).IsTrue();
        await Assert.That(instance.SettingKey).IsEqualTo(key);
        await Assert.That(tenant.SettingKey).IsEqualTo(key);
        await Assert.That(instance.Value).IsEqualTo(value);
        await Assert.That(tenant.Value).IsEqualTo(value);
        await Assert.That(instance.IsLocked).IsTrue();
        await Assert.That(tenant.IsLocked).IsFalse();
        await Assert.That(instance.Id.Version).IsEqualTo(7);
        await Assert.That(tenant.Id.Version).IsEqualTo(7);
        await Assert.That(tenant.TenantId).IsEqualTo(tenantId);
        await Assert.That(instance.CreatedAt).IsEqualTo(CreatedAt);
        await Assert.That(tenant.CreatedBy).IsEqualTo(actorId);
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("federation.unknown")]
    [Arguments("Federation.atproto_events_enabled")]
    [Arguments("federation.atproto_events_enabled ")]
    [Arguments(GovernanceSettingKeys.Federation.AtprotoEventValidationProfile)]
    [Arguments(GovernanceSettingKeys.Federation.AtprotoPublishMyEvents)]
    public async Task UnknownAndUnmovedKeysCannotEnterEitherDedicatedStore(string? key)
    {
        await Assert.That(AtprotoFederationSettingPolicy.IsDedicatedKey(key)).IsFalse();
        await Assert.That(() => AtprotoFederationInstanceSetting.Create(key!, "false", true, CreatedAt, null))
            .Throws<ArgumentException>();
        await Assert.That(() => AtprotoFederationTenantSetting.Create(Guid.CreateVersion7(), key!, "false", false, CreatedAt, null))
            .Throws<ArgumentException>();
    }

    [Test]
    [Arguments(SettingScope.Organization)]
    [Arguments(SettingScope.Group)]
    [Arguments(SettingScope.User)]
    [Arguments((SettingScope)(-1))]
    [Arguments((SettingScope)99)]
    public async Task DedicatedPolicyRejectsEveryScopeBelowTenantAndUndefinedScopes(SettingScope scope)
    {
        foreach (var pair in new[] { (EventsEnabled, "false"), (BackfillEnabled, "false"), (BackfillMode, "\"full\"") })
            await Assert.That(() => AtprotoFederationSettingPolicy.Validate(pair.Item1, pair.Item2, scope))
                .Throws<ArgumentException>();
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments(" ")]
    [Arguments("null")]
    [Arguments("\"null\"")]
    [Arguments("0")]
    [Arguments("{}")]
    [Arguments("[]")]
    [Arguments("\"secret://federation/policy\"")]
    [Arguments("{\"secretRef\":\"federation/policy\"}")]
    [Arguments("true false")]
    [Arguments("false trailing")]
    [Arguments("\"full\" true")]
    [Arguments("\"full")]
    [Arguments("/*policy*/true")]
    [Arguments(" true")]
    [Arguments("true ")]
    [Arguments("\tfalse\n")]
    [Arguments(" \"full\"")]
    [Arguments("\"full\" ")]
    [Arguments("\"\\u0066ull\"")]
    [Arguments("\"downtime\\u005fonly\"")]
    public async Task NullSecretReferenceWrongKindAndMalformedValuesAreRejectedAtBothScopes(string? value)
    {
        foreach (var key in new[] { EventsEnabled, BackfillEnabled, BackfillMode })
        {
            await Assert.That(() => AtprotoFederationInstanceSetting.Create(key, value!, true, CreatedAt, null))
                .Throws<ArgumentException>();
            await Assert.That(() => AtprotoFederationTenantSetting.Create(Guid.CreateVersion7(), key, value!, false, CreatedAt, null))
                .Throws<ArgumentException>();
        }
    }

    [Test]
    [Arguments("\"true\"")]
    [Arguments("\"false\"")]
    [Arguments("\"full\"")]
    [Arguments("True")]
    public async Task BooleanPoliciesRejectStringsAndNonJsonBooleanTokens(string value)
    {
        foreach (var key in new[] { EventsEnabled, BackfillEnabled })
            await Assert.That(() => AtprotoFederationSettingPolicy.Validate(key, value, SettingScope.Instance))
                .Throws<ArgumentException>();
    }

    [Test]
    [Arguments("true")]
    [Arguments("false")]
    [Arguments("\"incremental\"")]
    [Arguments("\"full \"")]
    [Arguments("\" downtime_only\"")]
    public async Task BackfillModeRejectsBooleanAndUnrecognizedModeValues(string value)
    {
        await Assert.That(() => AtprotoFederationSettingPolicy.Validate(BackfillMode, value, SettingScope.Tenant))
            .Throws<ArgumentException>();
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task SettingAValuePreservesTheExistingLockAndStampsOnlyTheUpdate(bool isLocked)
    {
        var creator = Guid.CreateVersion7();
        var updater = Guid.CreateVersion7();
        var instance = AtprotoFederationInstanceSetting.Create(EventsEnabled, "false", isLocked, CreatedAt, creator);
        var tenant = AtprotoFederationTenantSetting.Create(Guid.CreateVersion7(), BackfillMode, "\"downtime_only\"", isLocked, CreatedAt, creator);

        instance.SetValue("true", UpdatedAt, updater);
        tenant.SetValue("\"FuLl\"", UpdatedAt, updater);

        await Assert.That(instance.Value).IsEqualTo("true");
        await Assert.That(tenant.Value).IsEqualTo("\"FuLl\"");
        await Assert.That(instance.IsLocked).IsEqualTo(isLocked);
        await Assert.That(tenant.IsLocked).IsEqualTo(isLocked);
        await Assert.That(instance.UpdatedAt).IsEqualTo(UpdatedAt);
        await Assert.That(tenant.UpdatedBy).IsEqualTo(updater);
        await Assert.That(instance.CreatedAt).IsEqualTo(CreatedAt);
        await Assert.That(tenant.CreatedBy).IsEqualTo(creator);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task LockAndUnlockPreserveTheExactStoredValue(bool targetLock)
    {
        var actorId = Guid.CreateVersion7();
        var instance = AtprotoFederationInstanceSetting.Create(BackfillMode, "\"FuLl\"", !targetLock, CreatedAt, null);
        var tenant = AtprotoFederationTenantSetting.Create(Guid.CreateVersion7(), EventsEnabled, "true", !targetLock, CreatedAt, null);

        instance.SetLock(targetLock, UpdatedAt, actorId);
        tenant.SetLock(targetLock, UpdatedAt, actorId);

        await Assert.That(instance.IsLocked).IsEqualTo(targetLock);
        await Assert.That(tenant.IsLocked).IsEqualTo(targetLock);
        await Assert.That(instance.Value).IsEqualTo("\"FuLl\"");
        await Assert.That(tenant.Value).IsEqualTo("true");
        await Assert.That(instance.UpdatedBy).IsEqualTo(actorId);
        await Assert.That(tenant.UpdatedAt).IsEqualTo(UpdatedAt);
    }

    [Test]
    public async Task RejectedValueMutationLeavesValueLockAndAuditUntouched()
    {
        var instance = AtprotoFederationInstanceSetting.Create(EventsEnabled, "false", true, CreatedAt, null);
        var tenant = AtprotoFederationTenantSetting.Create(Guid.CreateVersion7(), BackfillMode, "\"full\"", false, CreatedAt, null);

        await Assert.That(() => instance.SetValue("\"true\"", UpdatedAt, Guid.CreateVersion7())).Throws<ArgumentException>();
        await Assert.That(() => tenant.SetValue("null", UpdatedAt, Guid.CreateVersion7())).Throws<ArgumentException>();

        await Assert.That(instance.Value).IsEqualTo("false");
        await Assert.That(instance.IsLocked).IsTrue();
        await Assert.That(instance.UpdatedAt).IsNull();
        await Assert.That(instance.UpdatedBy).IsNull();
        await Assert.That(tenant.Value).IsEqualTo("\"full\"");
        await Assert.That(tenant.IsLocked).IsFalse();
        await Assert.That(tenant.UpdatedAt).IsNull();
        await Assert.That(tenant.UpdatedBy).IsNull();
    }

    [Test]
    [Arguments(DateTimeKind.Local)]
    [Arguments(DateTimeKind.Unspecified)]
    public async Task DedicatedRowsRejectNonUtcCreationAuditTime(DateTimeKind kind)
    {
        DateTime invalidTime = DateTime.SpecifyKind(CreatedAt, kind);

        await Assert.That(() => AtprotoFederationInstanceSetting.Create(
                EventsEnabled, "false", true, invalidTime, Guid.CreateVersion7()))
            .Throws<ArgumentException>();
        await Assert.That(() => AtprotoFederationTenantSetting.Create(
                Guid.CreateVersion7(), BackfillMode, "\"full\"", false, invalidTime, Guid.CreateVersion7()))
            .Throws<ArgumentException>();
    }

    [Test]
    [Arguments(DateTimeKind.Local)]
    [Arguments(DateTimeKind.Unspecified)]
    public async Task RejectedNonUtcMutationPreservesBothCompletePersistedImages(DateTimeKind kind)
    {
        Guid creator = Guid.CreateVersion7(), previousUpdater = Guid.CreateVersion7();
        var instance = AtprotoFederationInstanceSetting.Create(EventsEnabled, "false", true, CreatedAt, creator);
        var tenant = AtprotoFederationTenantSetting.Create(
            Guid.CreateVersion7(), BackfillMode, "\"full\"", false, CreatedAt, creator);
        instance.SetValue("false", UpdatedAt, previousUpdater);
        tenant.SetValue("\"full\"", UpdatedAt, previousUpdater);
        var instanceBefore = (instance.Id, instance.SettingKey, instance.Value, instance.IsLocked,
            instance.CreatedAt, instance.CreatedBy, instance.UpdatedAt, instance.UpdatedBy);
        var tenantBefore = (tenant.Id, tenant.TenantId, tenant.SettingKey, tenant.Value, tenant.IsLocked,
            tenant.CreatedAt, tenant.CreatedBy, tenant.UpdatedAt, tenant.UpdatedBy);
        DateTime invalidTime = DateTime.SpecifyKind(UpdatedAt.AddMinutes(1), kind);
        Guid rejectedUpdater = Guid.CreateVersion7();

        await Assert.That(() => instance.SetValue("true", invalidTime, rejectedUpdater)).Throws<ArgumentException>();
        await Assert.That(() => tenant.SetValue("\"downtime_only\"", invalidTime, rejectedUpdater)).Throws<ArgumentException>();
        await Assert.That(() => instance.SetLock(false, invalidTime, rejectedUpdater)).Throws<ArgumentException>();
        await Assert.That(() => tenant.SetLock(true, invalidTime, rejectedUpdater)).Throws<ArgumentException>();

        await Assert.That((instance.Id, instance.SettingKey, instance.Value, instance.IsLocked,
            instance.CreatedAt, instance.CreatedBy, instance.UpdatedAt, instance.UpdatedBy)).IsEqualTo(instanceBefore);
        await Assert.That((tenant.Id, tenant.TenantId, tenant.SettingKey, tenant.Value, tenant.IsLocked,
            tenant.CreatedAt, tenant.CreatedBy, tenant.UpdatedAt, tenant.UpdatedBy)).IsEqualTo(tenantBefore);
    }

    [Test]
    public async Task TenantOverrideRequiresANonemptyTenantIdentity()
    {
        await Assert.That(() => AtprotoFederationTenantSetting.Create(Guid.Empty, EventsEnabled, "true", false, CreatedAt, null))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task OnlyTenantOverridesCanBeRemovedToRestoreInheritance()
    {
        await Assert.That(AtprotoFederationSettingPolicy.CanReset(SettingScope.Tenant)).IsTrue();
        foreach (var scope in new[] { SettingScope.Instance, SettingScope.Organization, SettingScope.Group, SettingScope.User, (SettingScope)99 })
            await Assert.That(AtprotoFederationSettingPolicy.CanReset(scope)).IsFalse();
    }
}
