namespace Explore.Application.Settings.Groups;

using Explore.Application.Contracts.Infrastructure;
using Explore.Domain.Constants;

public class RenderPolicySettingGroup : ISettingGroup
{
    public string Version { get; private set; } = "1";
    public string Preset { get; private set; } = "balanced";
    public bool AdvancedEnabled { get; private set; }
    public bool DisallowInteractiveServerOnOnboarding { get; private set; }
    public bool AllowTenantOverride { get; private set; }
    public bool LockTenantPublicSeo { get; private set; }
    public bool LockTenantOperational { get; private set; }
    public bool LockTenantAdmin { get; private set; }

    // Fallback (Global)
    public string FallbackRenderMode { get; private set; } = "InteractiveAuto";
    public bool FallbackPrerenderEnabled { get; private set; } = true;

    // Per-context render modes
    public string PublicSeoRenderMode { get; private set; } = "InteractiveAuto";
    public bool PublicSeoPrerenderEnabled { get; private set; } = true;
    public string OperationalRenderMode { get; private set; } = "InteractiveAuto";
    public bool OperationalPrerenderEnabled { get; private set; } = true;
    public string AdminRenderMode { get; private set; } = "InteractiveAuto";
    public bool AdminPrerenderEnabled { get; private set; } = true;
    public string OnboardingRenderMode { get; private set; } = "InteractiveAuto";
    public bool OnboardingPrerenderEnabled { get; private set; } = true;

    public static IEnumerable<string> SettingKeys =>
    [
        GovernanceSettingKeys.Routing.RenderPolicy.Version,
        GovernanceSettingKeys.Routing.RenderPolicy.Preset,
        GovernanceSettingKeys.Routing.RenderPolicy.AdvancedEnabled,
        GovernanceSettingKeys.Routing.RenderPolicy.DisallowInteractiveServerOnOnboarding,
        GovernanceSettingKeys.Routing.RenderPolicy.AllowTenantOverride,
        GovernanceSettingKeys.Routing.RenderPolicy.LockTenantPublicSeo,
        GovernanceSettingKeys.Routing.RenderPolicy.LockTenantOperational,
        GovernanceSettingKeys.Routing.RenderPolicy.LockTenantAdmin,
        GovernanceSettingKeys.Routing.RenderPolicy.Fallback.RenderMode,
        GovernanceSettingKeys.Routing.RenderPolicy.Fallback.PrerenderEnabled,
        GovernanceSettingKeys.Routing.RenderPolicy.PublicSeo.RenderMode,
        GovernanceSettingKeys.Routing.RenderPolicy.PublicSeo.PrerenderEnabled,
        GovernanceSettingKeys.Routing.RenderPolicy.Operational.RenderMode,
        GovernanceSettingKeys.Routing.RenderPolicy.Operational.PrerenderEnabled,
        GovernanceSettingKeys.Routing.RenderPolicy.Admin.RenderMode,
        GovernanceSettingKeys.Routing.RenderPolicy.Admin.PrerenderEnabled,
        GovernanceSettingKeys.Routing.RenderPolicy.Onboarding.RenderMode,
        GovernanceSettingKeys.Routing.RenderPolicy.Onboarding.PrerenderEnabled
    ];

    public void Populate(IReadOnlyDictionary<string, ResolvedSetting> settings)
    {
        if (settings.TryGetValue(GovernanceSettingKeys.Routing.RenderPolicy.Version, out var ver))
            Version = SettingValueSerializer.Deserialize(ver.Value, "1");
        if (settings.TryGetValue(GovernanceSettingKeys.Routing.RenderPolicy.Preset, out var preset))
            Preset = SettingValueSerializer.Deserialize(preset.Value, "balanced");
        if (settings.TryGetValue(GovernanceSettingKeys.Routing.RenderPolicy.AdvancedEnabled, out var adv))
            AdvancedEnabled = SettingValueSerializer.Deserialize(adv.Value, false);
        if (settings.TryGetValue(GovernanceSettingKeys.Routing.RenderPolicy.DisallowInteractiveServerOnOnboarding, out var disallow))
            DisallowInteractiveServerOnOnboarding = SettingValueSerializer.Deserialize(disallow.Value, false);
        if (settings.TryGetValue(GovernanceSettingKeys.Routing.RenderPolicy.AllowTenantOverride, out var tenantOverride))
            AllowTenantOverride = SettingValueSerializer.Deserialize(tenantOverride.Value, false);
        if (settings.TryGetValue(GovernanceSettingKeys.Routing.RenderPolicy.LockTenantPublicSeo, out var lockPub))
            LockTenantPublicSeo = SettingValueSerializer.Deserialize(lockPub.Value, false);
        if (settings.TryGetValue(GovernanceSettingKeys.Routing.RenderPolicy.LockTenantOperational, out var lockOp))
            LockTenantOperational = SettingValueSerializer.Deserialize(lockOp.Value, false);
        if (settings.TryGetValue(GovernanceSettingKeys.Routing.RenderPolicy.LockTenantAdmin, out var lockAdmin))
            LockTenantAdmin = SettingValueSerializer.Deserialize(lockAdmin.Value, false);

        // Fallback (Global)
        if (settings.TryGetValue(GovernanceSettingKeys.Routing.RenderPolicy.Fallback.RenderMode, out var fallbackMode))
            FallbackRenderMode = SettingValueSerializer.Deserialize(fallbackMode.Value, "InteractiveAuto");
        if (settings.TryGetValue(GovernanceSettingKeys.Routing.RenderPolicy.Fallback.PrerenderEnabled, out var fallbackPre))
            FallbackPrerenderEnabled = SettingValueSerializer.Deserialize(fallbackPre.Value, true);

        // Per-context
        if (settings.TryGetValue(GovernanceSettingKeys.Routing.RenderPolicy.PublicSeo.RenderMode, out var pubMode))
            PublicSeoRenderMode = SettingValueSerializer.Deserialize(pubMode.Value, "InteractiveAuto");
        if (settings.TryGetValue(GovernanceSettingKeys.Routing.RenderPolicy.PublicSeo.PrerenderEnabled, out var pubPre))
            PublicSeoPrerenderEnabled = SettingValueSerializer.Deserialize(pubPre.Value, true);
        if (settings.TryGetValue(GovernanceSettingKeys.Routing.RenderPolicy.Operational.RenderMode, out var opMode))
            OperationalRenderMode = SettingValueSerializer.Deserialize(opMode.Value, "InteractiveAuto");
        if (settings.TryGetValue(GovernanceSettingKeys.Routing.RenderPolicy.Operational.PrerenderEnabled, out var opPre))
            OperationalPrerenderEnabled = SettingValueSerializer.Deserialize(opPre.Value, true);
        if (settings.TryGetValue(GovernanceSettingKeys.Routing.RenderPolicy.Admin.RenderMode, out var adminMode))
            AdminRenderMode = SettingValueSerializer.Deserialize(adminMode.Value, "InteractiveAuto");
        if (settings.TryGetValue(GovernanceSettingKeys.Routing.RenderPolicy.Admin.PrerenderEnabled, out var adminPre))
            AdminPrerenderEnabled = SettingValueSerializer.Deserialize(adminPre.Value, true);
        if (settings.TryGetValue(GovernanceSettingKeys.Routing.RenderPolicy.Onboarding.RenderMode, out var onbMode))
            OnboardingRenderMode = SettingValueSerializer.Deserialize(onbMode.Value, "InteractiveAuto");
        if (settings.TryGetValue(GovernanceSettingKeys.Routing.RenderPolicy.Onboarding.PrerenderEnabled, out var onbPre))
            OnboardingPrerenderEnabled = SettingValueSerializer.Deserialize(onbPre.Value, true);
    }
}
