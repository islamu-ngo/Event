using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Contracts.ControlPlane;

public static class ControlPlaneHal
{
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
