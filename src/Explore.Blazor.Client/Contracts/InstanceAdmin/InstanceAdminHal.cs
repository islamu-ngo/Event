using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Contracts.InstanceAdmin;

public static class InstanceAdminHal
{
    public static string ExportConfigurationRelation(
        ConfigurationManifestExportView view, ConfigurationManifestExportScope scope) =>
        (view, scope) switch
        {
            (ConfigurationManifestExportView.Overrides, ConfigurationManifestExportScope.Instance) =>
                InstanceAdminLinkRelations.ExportConfigurationOverrides,
            (ConfigurationManifestExportView.Portable, ConfigurationManifestExportScope.Instance) =>
                InstanceAdminLinkRelations.ExportConfigurationPortable,
            (ConfigurationManifestExportView.Overrides, ConfigurationManifestExportScope.InstanceAndTenants) =>
                InstanceAdminLinkRelations.ExportConfigurationOverridesWithTenants,
            (ConfigurationManifestExportView.Portable, ConfigurationManifestExportScope.InstanceAndTenants) =>
                InstanceAdminLinkRelations.ExportConfigurationPortableWithTenants,
            _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unsupported export view or scope.")
        };

    public static bool HasLink(
        IDictionary<string, HalLink>? links,
        string relation) =>
        links?.ContainsKey(relation) == true;

    public static bool HasLinkForResource(
        IDictionary<string, HalLink>? links,
        string relation,
        Guid resourceId)
    {
        if (links?.TryGetValue(relation, out HalLink? link) != true
            || string.IsNullOrWhiteSpace(link.Href))
        {
            return false;
        }

        string path = link.Href.Split(['?', '#'], 2)[0];
        return path.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => Guid.TryParse(segment, out Guid parsed) && parsed == resourceId);
    }
}
