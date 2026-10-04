namespace Explore.Blazor.Client.Routing.InstanceAdmin;

public static class InstanceAdminRoutes
{
    public const string Root = "/admin/instance";
    public const string Overview = "/settings/instance";
    public const string Tenants = Root + "/tenants";
    public const string TenantConfiguration = Root + "/tenants/{TenantId}/configuration";
    public const string Domains = "/settings/instance?section=domain";
    public const string Operations = "/settings/instance?section=operations";
    public const string Plans = Root + "/plans";
    public const string PlanDetail = Root + "/plans/{Key}";
}
