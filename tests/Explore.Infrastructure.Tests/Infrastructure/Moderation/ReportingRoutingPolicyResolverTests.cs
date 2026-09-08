namespace Explore.Infrastructure.Tests.Infrastructure.Moderation;

using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Features.EventReporting.Models;
using Explore.Application.Settings;
using Explore.Application.Settings.Groups;
using Explore.Infrastructure.Configuration;
using Explore.Infrastructure.Services.Moderation;
using Microsoft.Extensions.Options;
using NSubstitute;

public sealed class ReportingRoutingPolicyResolverTests
{
    [Test]
    public async Task ResolveAsync_WithInstanceOspreyAndTenantLock_KeepsInstanceBaselineOnly()
    {
        ReportingRoutingPolicyResolver resolver = CreateResolver(
            new ModerationProviderOptions
            {
                Mode = ModerationProviderOptions.ModeOsprey,
                SyncReports = true,
                EvaluateSignals = true
            },
            new TenantDelegationSettingGroup(),
            ConfiguredReporting());

        ReportingRoutingPolicy policy = await resolver.ResolveAsync();

        await Assert.That(policy.LocalCanonicalRequired).IsTrue();
        await Assert.That(policy.ExternalSyncEnabled).IsTrue();
        await Assert.That(policy.InstanceOspreyEnabled).IsTrue();
        await Assert.That(policy.TenantOspreyEnabled).IsFalse();
        await Assert.That(policy.TenantProviderConfigurationLocked).IsTrue();
        await Assert.That(policy.TenantOspreyProviderLocked).IsTrue();
        await Assert.That(policy.TenantCoopProviderLocked).IsTrue();
        await Assert.That(policy.OspreyTargets).Count().IsEqualTo(1);
        await Assert.That(policy.OspreyTargets.Single().Scope).IsEqualTo(Explore.Domain.Enums.EventReportProviderTargetScope.Instance);
        await Assert.That(policy.OspreyTargets.Single().TargetId).IsEqualTo("instance");
    }

    [Test]
    public async Task ResolveAsync_WithUnlockedConfiguredTenantProviders_AddsTenantTargets()
    {
        var delegation = new TenantDelegationSettingGroup();
        delegation.Populate(UnlockedProviderSettings());

        ReportingRoutingPolicyResolver resolver = CreateResolver(
            new ModerationProviderOptions
            {
                Mode = ModerationProviderOptions.ModeComposite,
                SyncReports = true,
                EvaluateSignals = true,
                MirrorReviewQueue = true
            },
            delegation,
            ConfiguredReporting());

        ReportingRoutingPolicy policy = await resolver.ResolveAsync();

        await Assert.That(policy.InstanceOspreyEnabled).IsTrue();
        await Assert.That(policy.TenantOspreyEnabled).IsTrue();
        await Assert.That(policy.InstanceCoopEnabled).IsTrue();
        await Assert.That(policy.TenantCoopEnabled).IsTrue();
        await Assert.That(policy.TenantProviderConfigurationLocked).IsFalse();
        await Assert.That(policy.TenantOspreyProviderLocked).IsFalse();
        await Assert.That(policy.TenantCoopProviderLocked).IsFalse();
        await Assert.That(policy.OspreyRoutingMode).IsEqualTo(ReportingRoutingMode.Tenant);
        await Assert.That(policy.CoopRoutingMode).IsEqualTo(ReportingRoutingMode.Both);
        await Assert.That(policy.EvidenceMode).IsEqualTo(EventReportProviderEvidenceMode.SafeSummaryOnly);
        await Assert.That(policy.OspreyTargets).Count().IsEqualTo(2);
        await Assert.That(policy.CoopTargets).Count().IsEqualTo(2);
        await Assert.That(policy.OspreyTargets.Any(target => target.Scope == Explore.Domain.Enums.EventReportProviderTargetScope.Instance)).IsTrue();
        await Assert.That(policy.OspreyTargets.Any(target => target.Scope == Explore.Domain.Enums.EventReportProviderTargetScope.Tenant && target.EndpointUrl == "https://osprey.example" && target.ApiKey == "secret")).IsTrue();
    }

    [Test]
    public async Task ResolveAsync_WithLocalOnlyStaticMode_DisablesAllExternalTargetsButKeepsLocalCanonical()
    {
        var delegation = new TenantDelegationSettingGroup();
        delegation.Populate(UnlockedProviderSettings());

        ReportingRoutingPolicyResolver resolver = CreateResolver(
            new ModerationProviderOptions
            {
                Mode = ModerationProviderOptions.ModeLocalOnly,
                SyncReports = true,
                EvaluateSignals = true,
                MirrorReviewQueue = true
            },
            delegation,
            ConfiguredReporting());

        ReportingRoutingPolicy policy = await resolver.ResolveAsync();

        await Assert.That(policy.LocalCanonicalRequired).IsTrue();
        await Assert.That(policy.ExternalSyncEnabled).IsFalse();
        await Assert.That(policy.InstanceOspreyEnabled).IsFalse();
        await Assert.That(policy.TenantOspreyEnabled).IsFalse();
        await Assert.That(policy.InstanceCoopEnabled).IsFalse();
        await Assert.That(policy.TenantCoopEnabled).IsFalse();
        await Assert.That(policy.OspreyTargets).IsEmpty();
        await Assert.That(policy.CoopTargets).IsEmpty();
    }

    [Test]
    public async Task ResolveAsync_WithTenantOspreyLocked_BlocksOnlyTenantOspreyTarget()
    {
        var delegation = new TenantDelegationSettingGroup();
        delegation.Populate(CreateSettings(
            (Explore.Domain.Constants.GovernanceSettingKeys.TenantDelegation.LockReportingProviders, "false"),
            (Explore.Domain.Constants.GovernanceSettingKeys.TenantDelegation.LockTenantOspreyProvider, "true"),
            (Explore.Domain.Constants.GovernanceSettingKeys.TenantDelegation.LockTenantCoopProvider, "false")));

        ReportingRoutingPolicyResolver resolver = CreateResolver(
            new ModerationProviderOptions
            {
                Mode = ModerationProviderOptions.ModeComposite,
                SyncReports = true,
                EvaluateSignals = true,
                MirrorReviewQueue = true
            },
            delegation,
            ConfiguredReporting());

        ReportingRoutingPolicy policy = await resolver.ResolveAsync();

        await Assert.That(policy.InstanceOspreyEnabled).IsTrue();
        await Assert.That(policy.TenantOspreyEnabled).IsFalse();
        await Assert.That(policy.TenantCoopEnabled).IsTrue();
        await Assert.That(policy.TenantOspreyProviderLocked).IsTrue();
        await Assert.That(policy.TenantCoopProviderLocked).IsFalse();
    }

    [Test]
    public async Task ResolveAsync_WithTenantExternalSyncDisabled_KeepsInstanceBaselineButBlocksTenantTargets()
    {
        var delegation = new TenantDelegationSettingGroup();
        delegation.Populate(UnlockedProviderSettings());

        ReportingSettingGroup reporting = ConfiguredReporting();
        reporting.Populate(CreateSettings(
            (Explore.Domain.Constants.GovernanceSettingKeys.Reporting.TenantExternalSyncEnabled, "false"),
            (Explore.Domain.Constants.GovernanceSettingKeys.Reporting.EnableTenantOspreyProvider, "true"),
            (Explore.Domain.Constants.GovernanceSettingKeys.Reporting.OspreyEndpointUrl, "\"https://osprey.example\""),
            (Explore.Domain.Constants.InfrastructureSecretSettingKeys.Reporting.OspreyApiKey, "\"secret\""),
            (Explore.Domain.Constants.GovernanceSettingKeys.Reporting.EnableTenantCoopProvider, "true"),
            (Explore.Domain.Constants.GovernanceSettingKeys.Reporting.CoopEndpointUrl, "\"https://coop.example\""),
            (Explore.Domain.Constants.InfrastructureSecretSettingKeys.Reporting.CoopApiKey, "\"secret\"")));

        ReportingRoutingPolicyResolver resolver = CreateResolver(
            new ModerationProviderOptions
            {
                Mode = ModerationProviderOptions.ModeComposite,
                SyncReports = true,
                EvaluateSignals = true,
                MirrorReviewQueue = true
            },
            delegation,
            reporting);

        ReportingRoutingPolicy policy = await resolver.ResolveAsync();

        await Assert.That(policy.InstanceOspreyEnabled).IsTrue();
        await Assert.That(policy.InstanceCoopEnabled).IsTrue();
        await Assert.That(policy.TenantOspreyEnabled).IsFalse();
        await Assert.That(policy.TenantCoopEnabled).IsFalse();
    }

    private static ReportingRoutingPolicyResolver CreateResolver(
        ModerationProviderOptions options,
        TenantDelegationSettingGroup delegation,
        ReportingSettingGroup reporting)
    {
        var settingsResolver = Substitute.For<IHierarchicalSettingsResolver>();
        settingsResolver.ResolveGroupAsync<TenantDelegationSettingGroup>(Arg.Any<SettingContext>(), Arg.Any<CancellationToken>())
            .Returns(delegation);
        settingsResolver.ResolveGroupAsync<ReportingSettingGroup>(Arg.Any<SettingContext>(), Arg.Any<CancellationToken>())
            .Returns(reporting);

        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(Guid.NewGuid());

        return new ReportingRoutingPolicyResolver(
            settingsResolver,
            tenantContext,
            new StaticOptionsMonitor<ModerationProviderOptions>(options));
    }

    private static ReportingSettingGroup ConfiguredReporting()
    {
        var group = new ReportingSettingGroup();
        group.Populate(CreateSettings(
            (Explore.Domain.Constants.GovernanceSettingKeys.Reporting.EnableTenantOspreyProvider, "true"),
            (Explore.Domain.Constants.GovernanceSettingKeys.Reporting.OspreyRoutingMode, "\"tenant\""),
            (Explore.Domain.Constants.GovernanceSettingKeys.Reporting.CoopRoutingMode, "\"both\""),
            (Explore.Domain.Constants.GovernanceSettingKeys.Reporting.EvidenceMode, "\"SafeSummaryOnly\""),
            (Explore.Domain.Constants.GovernanceSettingKeys.Reporting.OspreyEndpointUrl, "\"https://osprey.example\""),
            (Explore.Domain.Constants.InfrastructureSecretSettingKeys.Reporting.OspreyApiKey, "\"secret\""),
            (Explore.Domain.Constants.GovernanceSettingKeys.Reporting.EnableTenantCoopProvider, "true"),
            (Explore.Domain.Constants.GovernanceSettingKeys.Reporting.CoopEndpointUrl, "\"https://coop.example\""),
            (Explore.Domain.Constants.InfrastructureSecretSettingKeys.Reporting.CoopApiKey, "\"secret\"")));
        return group;
    }

    private static Dictionary<string, ResolvedSetting> UnlockedProviderSettings() =>
        CreateSettings(
            (Explore.Domain.Constants.GovernanceSettingKeys.TenantDelegation.LockReportingProviders, "false"),
            (Explore.Domain.Constants.GovernanceSettingKeys.TenantDelegation.LockTenantOspreyProvider, "false"),
            (Explore.Domain.Constants.GovernanceSettingKeys.TenantDelegation.LockTenantCoopProvider, "false"));

    private static Dictionary<string, ResolvedSetting> CreateSettings(params (string key, string value)[] entries) =>
        entries.ToDictionary(e => e.key, e => new ResolvedSetting { Value = e.value });

    private sealed class StaticOptionsMonitor<TOptions>(TOptions currentValue) : IOptionsMonitor<TOptions>
    {
        public TOptions CurrentValue { get; } = currentValue;
        public TOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<TOptions, string?> listener) => null;
    }
}
