using Blazouter.Models;
using Explore.Blazor.Client.Routing;

namespace Explore.Blazor.Client.Tests.Routing;

public class RouteConfigurationPathBaseTests
{
    [Test]
    public async Task TenantDocumentBasePrefixesConfiguredRoutes()
    {
        var routes = new List<RouteConfig>
        {
            new() { Path = "/", Component = typeof(Routes) },
            new() { Path = "/settings/admin", Component = typeof(Routes) },
        };

        RouteConfigurationPathBase.Apply(routes, "https://event.test/t/acme/");

        await Assert.That(routes[0].Path).IsEqualTo("/t/acme");
        await Assert.That(routes[1].Path).IsEqualTo("/t/acme/settings/admin");
    }

    [Test]
    public async Task RootDocumentBasePreservesConfiguredRoutes()
    {
        var routes = new List<RouteConfig>
        {
            new() { Path = "/settings/instance", Component = typeof(Routes) },
        };

        RouteConfigurationPathBase.Apply(routes, "https://event.test/");

        await Assert.That(routes[0].Path).IsEqualTo("/settings/instance");
    }

    [Test]
    public async Task ReapplyingDocumentBaseDoesNotDuplicatePrefix()
    {
        var routes = new List<RouteConfig>
        {
            new() { Path = "/settings", Component = typeof(Routes) },
        };

        RouteConfigurationPathBase.Apply(routes, "https://event.test/t/acme/");
        RouteConfigurationPathBase.Apply(routes, "https://event.test/t/acme/");

        await Assert.That(routes[0].Path).IsEqualTo("/t/acme/settings");
    }
}
