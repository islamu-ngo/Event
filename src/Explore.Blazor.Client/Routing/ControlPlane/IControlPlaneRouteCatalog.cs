namespace Explore.Blazor.Client.Routing.ControlPlane;

public interface IControlPlaneRouteCatalog
{
    string Root { get; }

    IReadOnlyList<ControlPlaneRouteDescriptor> All { get; }

    IReadOnlyList<ControlPlaneRouteDescriptor> Navigation { get; }

    IReadOnlyList<ControlPlaneRouteDescriptor> TenantNavigation { get; }
}
