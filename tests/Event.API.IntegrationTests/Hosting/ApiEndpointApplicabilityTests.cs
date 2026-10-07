using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Constants;
using Explore.ServiceDefaults.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Hosting;

public sealed class ApiEndpointApplicabilityTests
{
    [Test]
    public async Task CompiledApiInventoryIncludesPrivateAndOperationalSurfaces()
    {
        await using var factory = new CustomWebApplicationFactory();
        using HttpClient client = factory.CreateClient();
        IReadOnlyList<EndpointSecurityDescription> endpoints = EndpointSecurityInventory.Capture(
            factory.Services.GetServices<EndpointDataSource>());
        AuthenticationScheme[] schemes = (await factory.Services
            .GetRequiredService<IAuthenticationSchemeProvider>().GetAllSchemesAsync()).ToArray();

        await Assert.That(endpoints.Count).IsGreaterThan(0);
        await Assert.That(endpoints.Any(endpoint => endpoint.RoutePattern == "/health" ||
            endpoint.RoutePattern == "health")).IsTrue();
        await Assert.That(endpoints.Any(endpoint => endpoint.ExcludedFromApiDescription)).IsTrue();
        await Assert.That(endpoints.Any(endpoint => endpoint.ControllerType is not null)).IsTrue();
        await Assert.That(schemes.Select(scheme => scheme.Name)).Contains(ApiAuthenticationSchemeNames.MultiAuth);
        await Assert.That(schemes.Select(scheme => scheme.Name)).Contains(ApiAuthenticationSchemeNames.ApiKey);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task OptionalMcpTransportIsCapturedOnlyWhenMapped(bool enabled)
    {
        await using var factory = new OptionalMcpFactory(enabled);
        using HttpClient client = factory.CreateClient();
        IReadOnlyList<EndpointSecurityDescription> endpoints = EndpointSecurityInventory.Capture(
            factory.Services.GetServices<EndpointDataSource>());
        EndpointSecurityDescription[] transport = endpoints.Where(endpoint =>
            endpoint.RoutePattern?.Trim('/') == "mcp").ToArray();

        await Assert.That(transport.Length > 0).IsEqualTo(enabled);
        if (enabled)
        {
            await Assert.That(transport.All(endpoint => endpoint.AllowsAnonymous)).IsTrue();
            await Assert.That(transport.Any(endpoint => endpoint.HttpMethods.Contains("POST",
                StringComparer.Ordinal))).IsTrue();
        }
    }

    private sealed class OptionalMcpFactory(bool enabled) : CustomWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Mcp:Enabled"] = enabled ? "true" : "false",
                    ["Mcp:EndpointPath"] = "/mcp",
                    ["Mcp:Stateless"] = "true",
                    ["Mcp:EnableLegacySse"] = "false"
                }));
        }
    }
}
