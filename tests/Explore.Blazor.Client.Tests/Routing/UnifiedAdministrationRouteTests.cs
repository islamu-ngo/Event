using Blazouter.Models;
using Explore.Blazor.Client.Pages.Admin;
using Explore.Blazor.Client.Providers;
using Explore.Blazor.Client.Routing.Guards;

namespace Explore.Blazor.Client.Tests.Routing;

public sealed class UnifiedAdministrationRouteTests
{
    [Test]
    public async Task AdministrationEntriesPassScopeSpecificGuardsToTheSharedRouter()
    {
        using var context = new BlazorTestContext();
        context.ComponentFactories.AddStub<TenantContextProvider>(
            parameters => builder => builder.AddContent(0, parameters.Get(component => component.ChildContent)));
        context.ComponentFactories.AddStub<LanguageProvider>(
            parameters => builder => builder.AddContent(0, parameters.Get(component => component.ChildContent)));
        context.ComponentFactories.AddStub<Blazouter.Components.Router>();

        var component = context.Render<Routes>();
        IEnumerable<RouteConfig> routes = component.FindComponent<Stub<Blazouter.Components.Router>>()
            .Instance.Parameters.Get(router => router.Routes);

        foreach (var entry in new (string Path, Type Guard)[]
        {
            ("/admin/instance", typeof(AdminRouteGuard)),
            ("/settings/instance", typeof(AdminRouteGuard)),
            ("/settings/admin", typeof(TenantAdminRouteGuard))
        })
        {
            RouteConfig route = routes.Single(route => route.Path == entry.Path);
            await Assert.That(route.Component).IsEqualTo(typeof(AdminSettingsPage));
            await Assert.That(route.Guards).IsEquivalentTo([entry.Guard]);
        }
    }
}
