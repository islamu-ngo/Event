using Event.Standalone.IntegrationTests.Fixtures;
using Explore.API.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Standalone.IntegrationTests;

public sealed class StandaloneSchedulerSurfaceTests
{
    private const string DashboardPath = "/quartz";

    /// <summary>
    /// The dashboard is an operator console that widens the authenticated surface, so a host that never opted in
    /// must not carry its routes at all rather than carry them behind a runtime check.
    /// </summary>
    [Test]
    public async Task DashboardIsNotMappedWhenOperatorHasNotOptedIn()
    {
        await using var factory = new StandaloneWebApplicationFactory();
        _ = factory.CreateClient();

        var patterns = RoutePatterns(factory);

        await Assert.That(patterns.Any(pattern =>
                pattern.StartsWith("quartz", StringComparison.OrdinalIgnoreCase)))
            .IsFalse();
    }

    /// <summary>
    /// The scheduler administration API is a normal versioned controller surface, so the combined host must map
    /// it exactly like every other API route rather than as a special-cased endpoint.
    /// </summary>
    [Test]
    public async Task SchedulerAdministrationApiIsComposedIntoTheCombinedGraph()
    {
        await using var factory = new StandaloneWebApplicationFactory();
        _ = factory.CreateClient();

        var patterns = RoutePatterns(factory);

        await Assert.That(patterns).Contains("api/admin/scheduler");
        await Assert.That(patterns).Contains("api/admin/scheduler/jobs");
        await Assert.That(patterns).Contains("api/admin/scheduler/pause");
        await Assert.That(patterns).Contains("api/admin/scheduler/resume");
        await Assert.That(patterns).Contains("api/admin/scheduler/jobs/{group}/{name}/trigger");
    }

    /// <summary>
    /// Route ownership decides which authentication pipeline a request meets. The administration API is bearer
    /// API traffic; the dashboard runs on Blazor circuits and cookie authentication, so it must stay outside the
    /// API branch or its SignalR negotiation would be routed through the API bridge.
    /// </summary>
    [Test]
    public async Task SchedulerRouteOwnershipSeparatesApiTrafficFromDashboardCircuits()
    {
        await using var factory = new StandaloneWebApplicationFactory();
        _ = factory.CreateClient();

        var classifier = new ApiHostRouteClassifier(
            factory.Services.GetServices<EndpointDataSource>(),
            mcpPath: null,
            schedulerPath: "/admin/scheduler");

        await Assert.That(classifier.IsApiOwned(new PathString("/api/admin/scheduler"))).IsTrue();
        await Assert.That(classifier.IsApiOwned(new PathString("/api/admin/scheduler/jobs"))).IsTrue();
        await Assert.That(classifier.IsApiOwned(new PathString(DashboardPath))).IsFalse();
        await Assert.That(classifier.IsApiOwned(new PathString($"{DashboardPath}/jobs"))).IsFalse();
    }

    /// <summary>
    /// The combined host must own exactly one Blazor circuit endpoint. A second one would mean the dashboard
    /// registered its own hub alongside the application's, which breaks circuit negotiation for both.
    /// </summary>
    [Test]
    public async Task CombinedHostOwnsExactlyOneBlazorCircuitEndpoint()
    {
        await using var factory = new StandaloneWebApplicationFactory();
        _ = factory.CreateClient();

        var blazorEndpoints = RoutePatterns(factory)
            .Where(pattern => pattern.Equals("_blazor", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        await Assert.That(blazorEndpoints.Length).IsEqualTo(1);
    }

    private static string[] RoutePatterns(StandaloneWebApplicationFactory factory) =>
        [.. factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText?.TrimStart('/'))
            .Where(pattern => !string.IsNullOrEmpty(pattern))
            .Select(pattern => pattern!)];
}
