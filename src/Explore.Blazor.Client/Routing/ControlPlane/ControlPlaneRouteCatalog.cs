namespace Explore.Blazor.Client.Routing.ControlPlane;

public sealed class ControlPlaneRouteCatalog : IControlPlaneRouteCatalog
{
    private static readonly ControlPlaneRouteDescriptor[] Routes =
    [
        new(ControlPlaneRouteKeys.Overview, ControlPlaneRoutes.Overview),
        new(ControlPlaneRouteKeys.Tenants, ControlPlaneRoutes.Tenants),
        new(ControlPlaneRouteKeys.TenantConfiguration, ControlPlaneRoutes.TenantConfiguration),
        new(ControlPlaneRouteKeys.Domains, ControlPlaneRoutes.Domains),
        new(ControlPlaneRouteKeys.Operations, ControlPlaneRoutes.Operations),
        new(ControlPlaneRouteKeys.Plans, ControlPlaneRoutes.Plans),
        new(ControlPlaneRouteKeys.PlanDetail, ControlPlaneRoutes.PlanDetail),
        new(ControlPlaneRouteKeys.Onboarding, ControlPlaneRoutes.Onboarding),
        new(ControlPlaneRouteKeys.Health, ControlPlaneRoutes.Health),
        new(ControlPlaneRouteKeys.Storage, ControlPlaneRoutes.Storage),
        new(ControlPlaneRouteKeys.Jobs, ControlPlaneRoutes.Jobs),
        new(ControlPlaneRouteKeys.Security, ControlPlaneRoutes.Security),
        new(ControlPlaneRouteKeys.Policies, ControlPlaneRoutes.Policies),
        new(ControlPlaneRouteKeys.TenantOverview, ControlPlaneRoutes.TenantRoot),
        new(ControlPlaneRouteKeys.TenantSettings, ControlPlaneRoutes.TenantSettings),
        new(ControlPlaneRouteKeys.TenantBranding, ControlPlaneRoutes.TenantBranding),
        new(ControlPlaneRouteKeys.TenantModeration, ControlPlaneRoutes.TenantModeration),
        new(ControlPlaneRouteKeys.TenantUsers, ControlPlaneRoutes.TenantUsers),
        new(ControlPlaneRouteKeys.TenantFooterNavigation, ControlPlaneRoutes.TenantFooterNavigation),
        new(ControlPlaneRouteKeys.TenantReports, ControlPlaneRoutes.TenantReports),
        new(ControlPlaneRouteKeys.TenantEvents, ControlPlaneRoutes.TenantEvents),
        new(ControlPlaneRouteKeys.TenantPolicies, ControlPlaneRoutes.TenantPolicies)
    ];

    private static readonly ControlPlaneRouteDescriptor[] NavigationRoutes =
    [
        new(ControlPlaneRouteKeys.Overview, ControlPlaneRoutes.Overview),
        new(ControlPlaneRouteKeys.Tenants, ControlPlaneRoutes.Tenants),
        new(ControlPlaneRouteKeys.Domains, ControlPlaneRoutes.Domains),
        new(ControlPlaneRouteKeys.Operations, ControlPlaneRoutes.Operations),
        new(ControlPlaneRouteKeys.Plans, ControlPlaneRoutes.Plans),
        new(ControlPlaneRouteKeys.Onboarding, ControlPlaneRoutes.Onboarding),
        new(ControlPlaneRouteKeys.Health, ControlPlaneRoutes.Health),
        new(ControlPlaneRouteKeys.Storage, ControlPlaneRoutes.Storage),
        new(ControlPlaneRouteKeys.Jobs, ControlPlaneRoutes.Jobs),
        new(ControlPlaneRouteKeys.Security, ControlPlaneRoutes.Security),
        new(ControlPlaneRouteKeys.Policies, ControlPlaneRoutes.Policies)
    ];

    private static readonly ControlPlaneRouteDescriptor[] TenantNavigationRoutes =
    [
        new(ControlPlaneRouteKeys.TenantOverview, ControlPlaneRoutes.TenantRoot),
        new(ControlPlaneRouteKeys.TenantSettings, ControlPlaneRoutes.TenantSettings),
        new(ControlPlaneRouteKeys.TenantBranding, ControlPlaneRoutes.TenantBranding),
        new(ControlPlaneRouteKeys.TenantModeration, ControlPlaneRoutes.TenantModeration),
        new(ControlPlaneRouteKeys.TenantUsers, ControlPlaneRoutes.TenantUsers),
        new(ControlPlaneRouteKeys.TenantFooterNavigation, ControlPlaneRoutes.TenantFooterNavigation),
        new(ControlPlaneRouteKeys.TenantReports, ControlPlaneRoutes.TenantReports),
        new(ControlPlaneRouteKeys.TenantEvents, ControlPlaneRoutes.TenantEvents),
        new(ControlPlaneRouteKeys.TenantPolicies, ControlPlaneRoutes.TenantPolicies)
    ];

    public string Root => ControlPlaneRoutes.Root;

    public IReadOnlyList<ControlPlaneRouteDescriptor> All => Routes;

    public IReadOnlyList<ControlPlaneRouteDescriptor> Navigation => NavigationRoutes;

    public IReadOnlyList<ControlPlaneRouteDescriptor> TenantNavigation => TenantNavigationRoutes;
}
