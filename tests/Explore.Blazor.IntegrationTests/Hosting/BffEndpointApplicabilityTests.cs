using Explore.Blazor.IntegrationTests.Fixtures;
using Explore.ServiceDefaults.Diagnostics;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Blazor.IntegrationTests.Hosting;

public sealed class BffEndpointApplicabilityTests
{
    [Test]
    public async Task CompiledSplitInventoryRetainsBffAndCircuitMetadata()
    {
        await using var factory = new BlazorBffWebApplicationFactory();
        using HttpClient client = factory.CreateClient();
        IReadOnlyList<EndpointSecurityDescription> endpoints = EndpointSecurityInventory.Capture(
            factory.Services.GetServices<EndpointDataSource>());
        AuthenticationScheme[] schemes = (await factory.Services
            .GetRequiredService<IAuthenticationSchemeProvider>().GetAllSchemesAsync()).ToArray();

        await Assert.That(endpoints.Count).IsGreaterThan(0);
        await Assert.That(endpoints.Any(endpoint => endpoint.RoutePattern?.TrimStart('/').StartsWith("bff/",
            StringComparison.OrdinalIgnoreCase) == true)).IsTrue();
        await Assert.That(endpoints.Any(endpoint => endpoint.RoutePattern?.Contains("_blazor",
            StringComparison.OrdinalIgnoreCase) == true)).IsTrue();
        await Assert.That(schemes.Select(scheme => scheme.Name)).Contains(CookieAuthenticationDefaults.AuthenticationScheme);
    }
}
