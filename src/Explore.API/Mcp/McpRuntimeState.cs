using Explore.API.Configuration;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.Settings;
using Explore.Application.Settings.Groups;
using Microsoft.Extensions.Options;

namespace Explore.API.Mcp;

public sealed record McpRuntimeState(
    bool StartupEnabled,
    bool RuntimeEnabled,
    bool EffectiveEnabled,
    bool StartupLegacySseCeiling,
    bool RuntimeLegacySseRequested,
    bool LegacySseRuntimeEnabled,
    bool TenantOverrideAllowed,
    bool TenantLegacySseOverrideAllowed,
    Guid? TenantId);

public interface IMcpRuntimeStateService
{
    Task<McpRuntimeState> GetAsync(Guid? tenantId = null, CancellationToken cancellationToken = default);
}

public sealed class McpRuntimeStateService : IMcpRuntimeStateService
{
    private readonly IOptions<McpAdapterSettings> _options;
    private readonly IHierarchicalSettingsResolver _settingsResolver;
    private readonly ITenantContextAccessor _tenantContextAccessor;
    private readonly IDeploymentModeProvider _deploymentModeProvider;

    public McpRuntimeStateService(
        IOptions<McpAdapterSettings> options,
        IHierarchicalSettingsResolver settingsResolver,
        ITenantContextAccessor tenantContextAccessor,
        IDeploymentModeProvider deploymentModeProvider)
    {
        _options = options;
        _settingsResolver = settingsResolver;
        _tenantContextAccessor = tenantContextAccessor;
        _deploymentModeProvider = deploymentModeProvider;
    }

    public async Task<McpRuntimeState> GetAsync(Guid? tenantId = null, CancellationToken cancellationToken = default)
    {
        var settings = _options.Value;
        var effectiveTenantId = tenantId ?? _tenantContextAccessor.TenantId;
        var isSingleTenant = await _deploymentModeProvider.IsSingleTenantAsync(cancellationToken);

        var instanceMcp = await _settingsResolver.ResolveGroupAsync<McpSettingGroup>(
            new SettingContext(), cancellationToken);
        var delegation = await _settingsResolver.ResolveGroupAsync<TenantDelegationSettingGroup>(
            new SettingContext(), cancellationToken);

        var tenantOverrideAllowed = isSingleTenant || !delegation.LockMcp;
        var tenantLegacyOverrideAllowed = isSingleTenant || !delegation.LockMcpLegacySse;

        var runtimeEnabled = instanceMcp.Enabled;
        var runtimeLegacySseRequested = instanceMcp.EnableLegacySse;

        if (effectiveTenantId is Guid tenant && tenant != Guid.Empty &&
            (tenantOverrideAllowed || tenantLegacyOverrideAllowed))
        {
            var tenantMcp = await _settingsResolver.ResolveGroupAsync<McpSettingGroup>(
                new SettingContext(TenantId: tenant), cancellationToken);

            if (tenantOverrideAllowed)
            {
                runtimeEnabled = tenantMcp.Enabled;
            }

            if (tenantLegacyOverrideAllowed)
            {
                runtimeLegacySseRequested = tenantMcp.EnableLegacySse;
            }
        }

        return new McpRuntimeState(
            StartupEnabled: settings.Enabled,
            RuntimeEnabled: runtimeEnabled,
            EffectiveEnabled: settings.Enabled && runtimeEnabled,
            StartupLegacySseCeiling: settings.EnableLegacySse,
            RuntimeLegacySseRequested: runtimeLegacySseRequested,
            LegacySseRuntimeEnabled: false,
            TenantOverrideAllowed: tenantOverrideAllowed,
            TenantLegacySseOverrideAllowed: tenantLegacyOverrideAllowed,
            TenantId: effectiveTenantId);
    }
}
