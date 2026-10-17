using Explore.API.Configuration;
using Explore.API.Hosting;
using Explore.Domain.Constants;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Event.Architecture.Tests;

public sealed class ReservedTenantSlugRouteCatalogTests
{
    [Test]
    public async Task CompiledClientServerAndShippedStaticRootsCannotBecomeTenantSlugs()
    {
        var builder = WebApplication.CreateBuilder();
        builder.AddServiceDefaults();
        builder.Services.AddControllers().AddApplicationPart(typeof(ApiHostApplicationExtensions).Assembly);
        builder.Services.AddApiVersioning().AddMvc();
        builder.Services.Configure<McpAdapterSettings>(settings => settings.Enabled = false);
        await using var app = builder.Build();
        app.MapApiHostEndpoints(new ApiHostCompositionState(false, false, false));
        // The project copies the actual referenced host's build output, never a test-owned inventory.
        app.MapStaticAssets(Path.Combine(AppContext.BaseDirectory, "ReservedRoots.staticwebassets.endpoints.json"));
        var clientRoutes = Explore.Blazor.Client.Routes.CreateRouteCatalog();
        await Assert.That(clientRoutes.Count).IsGreaterThan(0);
        var endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>().ToArray();
        await Assert.That(endpoints.Length).IsGreaterThan(0);
        var failures = clientRoutes.Select(route => Diagnose("client", route.Path))
            .Concat(endpoints.Select(endpoint => Diagnose("endpoint", endpoint.RoutePattern.RawText ?? string.Empty)))
            .OfType<string>().Order(StringComparer.Ordinal).ToArray();
        await Assert.That(failures).IsEmpty();
    }

    [Test]
    [Arguments("/:tenant")]
    [Arguments("/{tenant}")]
    [Arguments("/{**path}")]
    [Arguments("/*")]
    public async Task UnknownParameterizedRootOwnershipFailsClosed(string route) =>
        await Assert.That(Diagnose("probe", route)).IsEqualTo($"reserved-root-unowned-parameter:probe:{route}");

    [Test]
    public async Task MissingRootDiagnosticIsDeterministicAndCaseInsensitive()
    {
        await Assert.That(Diagnose("probe", "/unreserved-feature/details"))
            .IsEqualTo("reserved-root-missing:probe:unreserved-feature");
        await Assert.That(Diagnose("probe", "/AdMiN/configuration")).IsNull();
        await Assert.That(Diagnose("probe", "/asset.fingerprint.js")).IsNull();
        await Assert.That(Diagnose("endpoint", "{**path:file}")).IsNull();
        await Assert.That(Diagnose("probe", "")).IsEqualTo("reserved-root-empty:probe");
    }

    private static string? Diagnose(string owner, string route)
    {
        if (string.IsNullOrWhiteSpace(route)) return $"reserved-root-empty:{owner}";
        // ASP.NET's file-only fallback cannot match a valid tenant slug.
        if (owner == "endpoint" && route == "{**path:file}")
            return null;
        var root = route.TrimStart('/').Split('/')[0];
        if (root.Length == 0) return null;
        if (root.IndexOfAny(['{', '}', ':', '*']) >= 0)
            return $"reserved-root-unowned-parameter:{owner}:{route}";
        return ReservedTenantSlugs.IsReserved(root) && ReservedTenantSlugs.IsReserved(root.ToUpperInvariant())
            ? null : $"reserved-root-missing:{owner}:{root.ToLowerInvariant()}";
    }
}
