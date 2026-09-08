namespace Explore.Application.Features.Settings.Handlers.Commands;

using Explore.Application.Contracts.Infrastructure;
using Explore.Domain.Constants;
using Explore.Domain.Settings;

internal static class CerbosSettingsCacheInvalidation
{
    private static readonly HashSet<string> CerbosSettingKeys =
    [
        GovernanceSettingKeys.Cerbos.TenantCustomizationEnabled,
        GovernanceSettingKeys.Cerbos.Mode,
        GovernanceSettingKeys.Cerbos.CustomEndpoint,
        GovernanceSettingKeys.Cerbos.CustomAdminEndpoint,
        GovernanceSettingKeys.Cerbos.GrpcEndpoint
    ];

    public static void InvalidateIfCerbosSettingChanged(
        ICerbosConfigResolver? resolver,
        string key,
        SettingScope scope,
        Guid scopeId)
    {
        if (resolver is null || !CerbosSettingKeys.Contains(key))
            return;

        resolver.InvalidateCache(scope == SettingScope.Tenant ? scopeId : null);
    }

    public static void InvalidateIfAnyCerbosSettingChanged(
        ICerbosConfigResolver? resolver,
        IEnumerable<string> keys,
        SettingScope scope,
        Guid scopeId)
    {
        if (resolver is null)
            return;

        var changedCerbosKeys = keys.Where(CerbosSettingKeys.Contains).ToList();
        if (changedCerbosKeys.Count == 0)
            return;

        resolver.InvalidateCache(scope == SettingScope.Tenant ? scopeId : null);
    }
}
