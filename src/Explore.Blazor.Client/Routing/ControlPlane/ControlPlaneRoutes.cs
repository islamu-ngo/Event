namespace Explore.Blazor.Client.Routing.ControlPlane;

public static class ControlPlaneRoutes
{
    public const string Root = "/admin/instance";
    public const string TenantRoot = "/tenant/{TenantSlug}";
    public const string Overview = Root;
    public const string Tenants = Root + "/tenants";
    public const string TenantConfiguration = Root + "/tenants/{TenantId}/configuration";
    public const string Domains = Root + "/domains";
    public const string Operations = Root + "/operations";
    public const string Plans = Root + "/plans";
    public const string PlanDetail = Root + "/plans/{Key}";
    public const string Onboarding = Root + "/onboarding";
    public const string Health = Root + "/health";
    public const string Storage = Root + "/storage";
    public const string Jobs = Root + "/jobs";
    public const string Security = Root + "/security";
    public const string Policies = Root + "/policies";
    public const string TenantSettings = TenantRoot + "/settings";
    public const string TenantBranding = TenantRoot + "/branding";
    public const string TenantModeration = TenantRoot + "/moderation";
    public const string TenantUsers = TenantRoot + "/users";
    public const string TenantFooterNavigation = TenantRoot + "/footer-navigation";
    public const string TenantReports = TenantRoot + "/reports";
    public const string TenantEvents = TenantRoot + "/events";
    public const string TenantPolicies = TenantRoot + "/policies";
}
