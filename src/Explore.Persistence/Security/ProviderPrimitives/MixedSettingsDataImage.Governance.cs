using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Modules;
using Explore.Domain.Settings.Documents;

namespace Explore.Persistence.Security;

internal static partial class MixedSettingsDataImage
{
    internal static readonly Type[] GovernanceReadTypes =
        [typeof(InstanceBootstrapState), typeof(ModuleDefinition), typeof(TenantCapability), typeof(TenantSettingsDocument)];
    internal static readonly Type[] GovernanceWriteTypes = [typeof(TenantCapability), typeof(TenantSettingsDocument)];

    internal static string GovernancePredicate(ExploreDbContext context, Type type)
    {
        var sql = new HumanPrincipalSourceSql(context);
        if (type == typeof(InstanceBootstrapState)) return "1 = 1";
        if (type == typeof(ModuleDefinition))
            return $"r.{sql.Column(type, nameof(ModuleDefinition.ModuleKey))} IN ('Mod_Islamic','Mod_Tech')";
        string tenant = $"r.{sql.Column(type, "TenantId")} = '{PlatformDefaults.DefaultTenantId:D}'";
        return type == typeof(TenantSettingsDocument)
            ? tenant + $" AND r.{sql.Column(type, nameof(TenantSettingsDocument.DocumentKey))} = '{SettingsDocumentKeys.Tenant.Branding}'"
            : tenant;
    }
}
