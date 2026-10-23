using Event.Standalone.IntegrationTests.Fixtures;
using Explore.ServiceDefaults.Diagnostics;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Standalone.IntegrationTests;

public sealed class StandaloneEndpointApplicabilityTests
{
    [Test]
    public async Task CompiledCombinedInventoryRetainsApiAndBrowserBoundaries()
    {
        await using var factory = new StandaloneWebApplicationFactory();
        using HttpClient client = factory.CreateClient();
        IReadOnlyList<EndpointSecurityDescription> endpoints = EndpointSecurityInventory.Capture(
            factory.Services.GetServices<EndpointDataSource>());

        await Assert.That(endpoints.Count).IsGreaterThan(0);
        await Assert.That(endpoints.Any(endpoint => endpoint.ControllerType?.StartsWith("Explore.API.",
            StringComparison.Ordinal) == true)).IsTrue();
        await Assert.That(endpoints.Any(endpoint => endpoint.RoutePattern?.TrimStart('/').StartsWith("bff/",
            StringComparison.OrdinalIgnoreCase) == true)).IsTrue();
        await Assert.That(endpoints.Any(endpoint => endpoint.RoutePattern?.Contains("_blazor",
            StringComparison.OrdinalIgnoreCase) == true)).IsTrue();
        await Assert.That(endpoints.Any(endpoint => endpoint.ExcludedFromApiDescription)).IsTrue();
    }
}
