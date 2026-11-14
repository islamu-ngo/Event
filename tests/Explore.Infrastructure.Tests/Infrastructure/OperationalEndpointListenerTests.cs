using System.Net;
using Explore.ServiceDefaults;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Explore.Infrastructure.Tests.Infrastructure;

public sealed class OperationalEndpointListenerTests
{
    [Test]
    [Arguments(true, HttpStatusCode.OK)]
    [Arguments(false, HttpStatusCode.NotFound)]
    public async Task DualStackListenerMatchesOnlyItsDeclaredPhysicalIPv4Address(
        bool declaredAddressMatches, HttpStatusCode expected)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseKestrel(options => options.Listen(IPAddress.IPv6Any, 0));
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OperationalEndpoints:Address"] = declaredAddressMatches ? "127.0.0.1" : "127.0.0.2"
        });
        builder.AddServiceDefaults();
        await using var app = builder.Build();
        app.MapDefaultEndpoints();
        app.MapGet("/connection", (HttpContext context) => context.Connection.LocalIpAddress!.ToString());
        await app.StartAsync();
        string address = app.Services.GetRequiredService<IServer>().Features
            .Get<IServerAddressesFeature>()!.Addresses.Single();
        int port = new Uri(address).Port;
        app.Configuration["OperationalEndpoints:Port"] = port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        string local = await client.GetStringAsync("/connection");
        await Assert.That(IPAddress.Parse(local).IsIPv4MappedToIPv6).IsTrue();

        using var response = await client.GetAsync("/health");

        await Assert.That(response.StatusCode).IsEqualTo(expected);
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
    }
}
