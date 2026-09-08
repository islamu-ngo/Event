using Event.Web.BffHosting.Security;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.Http;

namespace Explore.Blazor.IntegrationTests.Services;

public class TenantCircuitHandlerTests
{
    [Test]
    public async Task CircuitActivity_WithTenantRoute_ForwardsSlugAcrossHandlerScope()
    {
        var routeAccessor = new TenantRouteContextAccessor(new HttpContextAccessor());
        var navigationManager = new TestNavigationManager(
            "https://event.test/",
            "https://event.test/t/acme/admin/tenant/settings");
        var configurationProvider = CreateConfigurationProvider();
        var handler = new TenantCircuitHandler(routeAccessor, navigationManager, configurationProvider);

        await handler.OnCircuitOpenedAsync(null!, CancellationToken.None);

        var capturedSlug = string.Empty;
        var activity = handler.CreateInboundActivityHandler(_ =>
        {
            var pooledHandlerAccessor = new TenantRouteContextAccessor(new HttpContextAccessor());
            capturedSlug = pooledHandlerAccessor.TenantSlug;
            return Task.CompletedTask;
        });

        await activity(null!);

        await Assert.That(routeAccessor.TenantSlug).IsEqualTo("acme");
        await Assert.That(capturedSlug).IsEqualTo("acme");
    }

    [Test]
    public async Task CircuitNavigation_OutsideTenantRoute_ClearsSlug()
    {
        var routeAccessor = new TenantRouteContextAccessor(new HttpContextAccessor());
        var navigationManager = new TestNavigationManager(
            "https://event.test/",
            "https://event.test/t/acme/admin/tenant/settings");
        var configurationProvider = CreateConfigurationProvider();
        var handler = new TenantCircuitHandler(routeAccessor, navigationManager, configurationProvider);

        await handler.OnCircuitOpenedAsync(null!, CancellationToken.None);
        navigationManager.NavigateTo("/admin/instance/settings");

        await Assert.That(routeAccessor.TenantSlug).IsNull();
    }

    [Test]
    public async Task CircuitOpen_WithCustomConfiguredPrefix_ExtractsSlug()
    {
        var routeAccessor = new TenantRouteContextAccessor(new HttpContextAccessor());
        var navigationManager = new TestNavigationManager(
            "https://event.test/",
            "https://event.test/community/acme/settings");
        var configurationProvider = CreateConfigurationProvider("/community");
        var handler = new TenantCircuitHandler(routeAccessor, navigationManager, configurationProvider);

        await handler.OnCircuitOpenedAsync(null!, CancellationToken.None);

        await Assert.That(routeAccessor.TenantSlug).IsEqualTo("acme");
    }

    private static IBffResolverConfigurationProvider CreateConfigurationProvider(string pathPrefix = "/t")
    {
        var provider = Substitute.For<IBffResolverConfigurationProvider>();
        provider.GetConfigurationAsync(Arg.Any<CancellationToken>())
            .Returns(new ResolverConfigurationDto
            {
                PathEnabled = true,
                PathPrefix = pathPrefix
            });
        return provider;
    }

    private sealed class TestNavigationManager : NavigationManager
    {
        public TestNavigationManager(string baseUri, string uri)
        {
            Initialize(baseUri, uri);
        }

        protected override void NavigateToCore(string uri, bool forceLoad)
        {
            Uri = ToAbsoluteUri(uri).ToString();
            NotifyLocationChanged(isInterceptedLink: false);
        }
    }
}
