namespace Explore.Infrastructure.Services.Moderation;

using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Settings;
using Explore.Application.Settings.Groups;
using Explore.Domain.Enums;
using Explore.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

public sealed class ReportingRoutingPolicyResolver(
    IHierarchicalSettingsResolver settingsResolver,
    ITenantContext tenantContext,
    IOptionsMonitor<ModerationProviderOptions> options) : IReportingRoutingPolicyResolver
{
    public async Task<ReportingRoutingPolicy> ResolveAsync(CancellationToken cancellationToken = default)
    {
        var context = new SettingContext(TenantId: tenantContext.TenantId);
        TenantDelegationSettingGroup delegation = await settingsResolver.ResolveGroupAsync<TenantDelegationSettingGroup>(
            context,
            cancellationToken);
        ReportingSettingGroup reporting = await settingsResolver.ResolveGroupAsync<ReportingSettingGroup>(
            context,
            cancellationToken);

        ModerationProviderOptions current = options.CurrentValue;
        bool externalSyncEnabled = !current.IsDisabled && current.SyncReports && !current.IsLocalOnly;
        bool tenantProvidersAllowed = externalSyncEnabled && reporting.TenantExternalSyncEnabled && !delegation.LockReportingProviders;
        bool tenantOspreyAllowed = tenantProvidersAllowed && !delegation.LockTenantOspreyProvider;
        bool tenantCoopAllowed = tenantProvidersAllowed && !delegation.LockTenantCoopProvider;
        bool instanceOspreyEnabled = externalSyncEnabled && current.ShouldEvaluateSignals;
        bool tenantOspreyEnabled = tenantOspreyAllowed && reporting.IsTenantOspreyConfigured;
        bool instanceCoopEnabled = externalSyncEnabled && current.ShouldMirrorReviewQueue;
        bool tenantCoopEnabled = tenantCoopAllowed && reporting.IsTenantCoopConfigured;
        string tenantTargetId = tenantContext.TenantId.ToString("N");
        IReadOnlyList<ReportingProviderTarget> ospreyTargets = BuildTargets(
            EventReportExternalProvider.Osprey,
            instanceOspreyEnabled,
            tenantOspreyEnabled,
            tenantTargetId,
            reporting.OspreyRoutingMode,
            reporting.OspreyEndpointUrl,
            reporting.OspreyApiKey);
        IReadOnlyList<ReportingProviderTarget> coopTargets = BuildTargets(
            EventReportExternalProvider.Coop,
            instanceCoopEnabled,
            tenantCoopEnabled,
            tenantTargetId,
            reporting.CoopRoutingMode,
            reporting.CoopEndpointUrl,
            reporting.CoopApiKey);

        return new ReportingRoutingPolicy(
            LocalCanonicalRequired: true,
            ExternalSyncEnabled: externalSyncEnabled,
            InstanceOspreyEnabled: instanceOspreyEnabled,
            TenantOspreyEnabled: tenantOspreyEnabled,
            InstanceCoopEnabled: instanceCoopEnabled,
            TenantCoopEnabled: tenantCoopEnabled,
            TenantProviderConfigurationLocked: delegation.LockReportingProviders,
            TenantOspreyProviderLocked: delegation.LockTenantOspreyProvider,
            TenantCoopProviderLocked: delegation.LockTenantCoopProvider,
            OspreyRoutingMode: reporting.OspreyRoutingMode,
            CoopRoutingMode: reporting.CoopRoutingMode,
            EvidenceMode: reporting.EvidenceMode,
            OspreyTargets: ospreyTargets,
            CoopTargets: coopTargets);
    }

    private static IReadOnlyList<ReportingProviderTarget> BuildTargets(
        EventReportExternalProvider provider,
        bool instanceEnabled,
        bool tenantEnabled,
        string tenantTargetId,
        string routingMode,
        string endpointUrl,
        string apiKey)
    {
        var targets = new List<ReportingProviderTarget>(capacity: 2);
        if (instanceEnabled)
        {
            targets.Add(new ReportingProviderTarget(provider, EventReportProviderTargetScope.Instance, "instance"));
        }

        if (tenantEnabled && !string.Equals(routingMode, ReportingRoutingMode.Instance, StringComparison.OrdinalIgnoreCase))
        {
            targets.Add(new ReportingProviderTarget(
                provider,
                EventReportProviderTargetScope.Tenant,
                tenantTargetId,
                endpointUrl,
                apiKey));
        }

        return targets;
    }
}
