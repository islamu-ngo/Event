using Explore.Application.Features.Federation.Atproto.Models;
using Explore.Application.Services.Federation;
using Explore.Domain.Constants;

namespace Event.Application.UnitTests.Settings;

public sealed class AtprotoPdsRecoveryPolicyResolverTests
{
    private const string BackfillEnabled = GovernanceSettingKeys.Federation.AtprotoEventsBackfillEnabled;
    private const string BackfillMode = GovernanceSettingKeys.Federation.AtprotoEventsBackfillMode;
    private const string EmptyFingerprint = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    [Test]
    public async Task DedicatedInstanceRecoveryPolicyOverridesContradictoryGenericSettings()
    {
        using var fixture = new FederationReaderFixture();
        Guid tenantId = fixture.AddTenant();
        fixture.LegacyInstance(BackfillEnabled, "false", locked: true);
        fixture.LegacyInstance(BackfillMode, "\"downtime_only\"", locked: true);
        fixture.DedicatedInstance(BackfillEnabled, "true", locked: true);
        fixture.DedicatedInstance(BackfillMode, "\"full\"", locked: true);

        AtprotoPdsRecoveryPolicy policy = await Resolve(fixture);

        await Assert.That(policy.IsEnabled).IsTrue();
        await Assert.That(policy.Mode).IsEqualTo(AtprotoPdsRecoveryMode.Full);
        await Assert.That(policy.EffectiveTenantIds).IsEquivalentTo([tenantId]);
    }

    [Test]
    public async Task DedicatedOverridesSelectBothRecoveryKeysWithoutUsingPresentationPolicy()
    {
        using var fixture = new FederationReaderFixture();
        Guid fullTenant = fixture.AddTenant();
        Guid disabledTenant = fixture.AddTenant();
        fixture.Instance(BackfillEnabled, "false");
        fixture.Instance(BackfillMode, "\"downtime_only\"");
        fixture.DedicatedOverride(fullTenant, BackfillEnabled, "true");
        fixture.DedicatedOverride(fullTenant, BackfillMode, "\"full\"");
        fixture.DedicatedOverride(disabledTenant, BackfillEnabled, "false");
        fixture.DedicatedOverride(disabledTenant, BackfillMode, "\"full\"");
        fixture.DedicatedOverride(fullTenant, GovernanceSettingKeys.Federation.AtprotoEventsEnabled, "false");
        fixture.DedicatedOverride(disabledTenant, GovernanceSettingKeys.Federation.AtprotoEventsEnabled, "true");

        AtprotoPdsRecoveryPolicy policy = await Resolve(fixture);

        await Assert.That(policy.IsEnabled).IsTrue();
        await Assert.That(policy.Mode).IsEqualTo(AtprotoPdsRecoveryMode.Full);
        await Assert.That(policy.EffectiveTenantIds).IsEquivalentTo([fullTenant]);
    }

    [Test]
    [Arguments(false, false, true, AtprotoPdsRecoveryMode.Full)]
    [Arguments(false, true, true, AtprotoPdsRecoveryMode.DowntimeOnly)]
    [Arguments(true, false, false, AtprotoPdsRecoveryMode.DowntimeOnly)]
    [Arguments(true, true, false, AtprotoPdsRecoveryMode.DowntimeOnly)]
    public async Task MultiTenantInstanceLocksConstrainEnablementAndModeIndependently(
        bool enabledLocked, bool modeLocked, bool expectedEnabled, AtprotoPdsRecoveryMode expectedMode)
    {
        using var fixture = new FederationReaderFixture();
        Guid tenantId = fixture.AddTenant();
        fixture.Instance(BackfillEnabled, "false", enabledLocked);
        fixture.Instance(BackfillMode, "\"downtime_only\"", modeLocked);
        fixture.Override(tenantId, BackfillEnabled, "true");
        fixture.Override(tenantId, BackfillMode, "\"full\"");

        AtprotoPdsRecoveryPolicy policy = await Resolve(fixture);

        await Assert.That(policy.IsEnabled).IsEqualTo(expectedEnabled);
        await Assert.That(policy.Mode).IsEqualTo(expectedMode);
        await Assert.That(policy.EffectiveTenantIds).IsEquivalentTo(expectedEnabled ? [tenantId] : Array.Empty<Guid>());
    }

    [Test]
    public async Task SingleTenantBackgroundRecoveryRetainsInstanceLockBypass()
    {
        using var fixture = new FederationReaderFixture();
        Guid tenantId = fixture.AddTenant();
        fixture.DeploymentMode("SingleTenant");
        fixture.Instance(BackfillEnabled, "false", locked: true);
        fixture.Instance(BackfillMode, "\"downtime_only\"", locked: true);
        fixture.Override(tenantId, BackfillEnabled, "true");
        fixture.Override(tenantId, BackfillMode, "\"full\"");

        AtprotoPdsRecoveryPolicy policy = await Resolve(fixture);

        await Assert.That(policy.IsEnabled).IsTrue();
        await Assert.That(policy.Mode).IsEqualTo(AtprotoPdsRecoveryMode.Full);
        await Assert.That(policy.EffectiveTenantIds).IsEquivalentTo([tenantId]);
    }

    [Test]
    public async Task MissingRecoverySettingsDefaultToDisabledDowntimeOnly()
    {
        using var fixture = new FederationReaderFixture();
        fixture.AddTenant();

        AtprotoPdsRecoveryPolicy policy = await Resolve(fixture);

        await Assert.That(policy.IsEnabled).IsFalse();
        await Assert.That(policy.Mode).IsEqualTo(AtprotoPdsRecoveryMode.DowntimeOnly);
        await Assert.That(policy.EffectiveTenantIds).IsEmpty();
        await Assert.That(policy.AudienceFingerprint).IsEqualTo(EmptyFingerprint);
    }

    [Test]
    public async Task MissingInstanceRowsDoNotPreventExplicitRecoveryOverrides()
    {
        using var fixture = new FederationReaderFixture();
        Guid tenantId = fixture.AddTenant();
        fixture.Override(tenantId, BackfillEnabled, "true");
        fixture.Override(tenantId, BackfillMode, "\"full\"");

        AtprotoPdsRecoveryPolicy policy = await Resolve(fixture);

        await Assert.That(policy.IsEnabled).IsTrue();
        await Assert.That(policy.Mode).IsEqualTo(AtprotoPdsRecoveryMode.Full);
        await Assert.That(policy.EffectiveTenantIds).IsEquivalentTo([tenantId]);
    }

    [Test]
    public async Task MissingModeDefaultsToDowntimeOnlyForEnabledTenants()
    {
        using var fixture = new FederationReaderFixture();
        Guid tenantId = fixture.AddTenant();
        fixture.Instance(BackfillEnabled, "true");

        AtprotoPdsRecoveryPolicy policy = await Resolve(fixture);

        await Assert.That(policy.IsEnabled).IsTrue();
        await Assert.That(policy.Mode).IsEqualTo(AtprotoPdsRecoveryMode.DowntimeOnly);
        await Assert.That(policy.EffectiveTenantIds).IsEquivalentTo([tenantId]);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task FullRecoveryAudienceExcludesDowntimeTenantsButFingerprintIncludesTheirPolicy(bool reverseOrder)
    {
        using var fixture = new FederationReaderFixture();
        Guid[] order = reverseOrder
            ? [FederationReaderFixture.TenantB, FederationReaderFixture.TenantA]
            : [FederationReaderFixture.TenantA, FederationReaderFixture.TenantB];
        foreach (Guid tenantId in order)
            fixture.AddTenant(tenantId);
        fixture.AddTenant(FederationReaderFixture.TenantC, active: false);
        Guid disabled = fixture.AddTenant();
        fixture.Instance(BackfillEnabled, "true");
        fixture.Instance(BackfillMode, "\"downtime_only\"");
        fixture.Override(FederationReaderFixture.TenantB, BackfillMode, "\"full\"");
        fixture.Override(FederationReaderFixture.TenantC, BackfillMode, "\"full\"");
        fixture.Override(disabled, BackfillEnabled, "false");
        fixture.Override(disabled, BackfillMode, "\"full\"");

        AtprotoPdsRecoveryPolicy policy = await Resolve(fixture);

        await Assert.That(policy.IsEnabled).IsTrue();
        await Assert.That(policy.Mode).IsEqualTo(AtprotoPdsRecoveryMode.Full);
        await Assert.That(policy.EffectiveTenantIds).IsEquivalentTo([FederationReaderFixture.TenantB]);
        // Fixed SHA-256 vector: tenant A in downtime-only mode, then tenant B in full mode, no trailing newline.
        await Assert.That(policy.AudienceFingerprint).IsEqualTo("3e569531298691f9ca3af26ed80515f3897f4d836208913b62a8ca5b5ed461f0");
    }

    [Test]
    public async Task DowntimeOnlyRecoveryRetainsAllEnabledActiveTenantsInStableOrder()
    {
        using var fixture = new FederationReaderFixture();
        fixture.AddTenant(FederationReaderFixture.TenantB);
        fixture.AddTenant(FederationReaderFixture.TenantA);
        fixture.AddTenant(FederationReaderFixture.TenantC, active: false);
        Guid disabled = fixture.AddTenant();
        fixture.Instance(BackfillEnabled, "true");
        fixture.Instance(BackfillMode, "\"downtime_only\"");
        fixture.Override(disabled, BackfillEnabled, "false");

        AtprotoPdsRecoveryPolicy policy = await Resolve(fixture);

        await Assert.That(policy.IsEnabled).IsTrue();
        await Assert.That(policy.Mode).IsEqualTo(AtprotoPdsRecoveryMode.DowntimeOnly);
        await Assert.That(policy.EffectiveTenantIds.SequenceEqual([FederationReaderFixture.TenantA, FederationReaderFixture.TenantB])).IsTrue();
        await Assert.That(policy.AudienceFingerprint).IsEqualTo("ad19c8c92e138938cef825fbea985f7a64920e0e6eae42f958bcd6b3e1f23143");
    }

    [Test]
    [Arguments("\"FULL\"", AtprotoPdsRecoveryMode.Full)]
    [Arguments("\"DoWnTiMe_OnLy\"", AtprotoPdsRecoveryMode.DowntimeOnly)]
    public async Task AcceptedModeCasingKeepsItsMeaning(string storedMode, AtprotoPdsRecoveryMode expectedMode)
    {
        using var fixture = new FederationReaderFixture();
        Guid tenantId = fixture.AddTenant();
        fixture.Instance(BackfillEnabled, "true");
        fixture.Instance(BackfillMode, storedMode);

        AtprotoPdsRecoveryPolicy policy = await Resolve(fixture);

        await Assert.That(policy.IsEnabled).IsTrue();
        await Assert.That(policy.Mode).IsEqualTo(expectedMode);
        await Assert.That(policy.EffectiveTenantIds).IsEquivalentTo([tenantId]);
    }

    [Test]
    public async Task MissingDedicatedRecoveryRowsDoNotResurrectStaleGenericEnablement()
    {
        using var fixture = new FederationReaderFixture();
        fixture.AddTenant();
        fixture.LegacyInstance(BackfillEnabled, "true");
        fixture.LegacyInstance(BackfillMode, "\"full\"");

        AtprotoPdsRecoveryPolicy policy = await Resolve(fixture);

        await Assert.That(policy.IsEnabled).IsFalse();
        await Assert.That(policy.Mode).IsEqualTo(AtprotoPdsRecoveryMode.DowntimeOnly);
        await Assert.That(policy.EffectiveTenantIds).IsEmpty();
        await Assert.That(policy.AudienceFingerprint).IsEqualTo(EmptyFingerprint);
    }

    [Test]
    public async Task RecoveryOverrideObservationDoesNotCombineEnablementAndModeFromDifferentStates()
    {
        using var fixture = new FederationReaderFixture();
        Guid tenantId = fixture.AddTenant();
        fixture.Instance(BackfillEnabled, "false");
        fixture.Instance(BackfillMode, "\"downtime_only\"");
        fixture.Override(tenantId, BackfillEnabled, "true");
        fixture.Override(tenantId, BackfillMode, "\"downtime_only\"");
        fixture.ChangeRecoveryOverrideAfterNextObservation(tenantId, "false", "\"full\"");

        AtprotoPdsRecoveryPolicy observedBeforeChange = await Resolve(fixture);
        AtprotoPdsRecoveryPolicy observedAfterChange = await Resolve(fixture);

        await Assert.That(observedBeforeChange.IsEnabled).IsTrue();
        await Assert.That(observedBeforeChange.Mode).IsEqualTo(AtprotoPdsRecoveryMode.DowntimeOnly);
        await Assert.That(observedBeforeChange.EffectiveTenantIds).IsEquivalentTo([tenantId]);
        await Assert.That(observedAfterChange.IsEnabled).IsFalse();
        await Assert.That(observedAfterChange.EffectiveTenantIds).IsEmpty();
        await Assert.That(observedAfterChange.AudienceFingerprint).IsEqualTo(EmptyFingerprint);
    }

    private static Task<AtprotoPdsRecoveryPolicy> Resolve(FederationReaderFixture fixture) =>
        fixture.Resolve<AtprotoPdsRecoveryPolicyResolver>().ResolveAsync(CancellationToken.None);
}
