namespace Explore.Blazor.Client.Routing.ControlPlane;

public static class ConfigurationManifestExportRoutes
{
    public const string BffExport = "/bff/control-plane/configuration-manifest/export";
    public const string BffTenantExport =
        "/bff/tenants/{tenantId:guid}/configuration-package/export";
}
