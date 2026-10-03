namespace Explore.Blazor.Client.Routing.InstanceAdmin;

public static class ConfigurationManifestExportRoutes
{
    public const string BffExport = "/bff/admin/instance/configuration-manifest/export";
    public const string BffTenantExport =
        "/bff/tenants/{tenantId:guid}/configuration-package/export";
}
