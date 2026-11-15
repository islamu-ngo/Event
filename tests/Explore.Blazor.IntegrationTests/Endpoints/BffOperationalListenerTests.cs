using System.Net;
using Explore.Blazor.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Blazor.IntegrationTests.Endpoints;

[Category(BffTestCategories.Security)]
[NotInParallel("BlazorBootstrapEnvironment")]
public sealed class BffOperationalListenerTests
{
    [Test]
    [Arguments("/health", true)]
    [Arguments("/metrics", true)]
    [Arguments("/health", false)]
    [Arguments("/metrics", false)]
    [Arguments("/about", false)]
    public async Task HttpsRedirectionPreservesPrivateOperationalListeners(string path, bool privateListener)
    {
        await using var original = new BlazorBffWebApplicationFactory();
        await using var factory = original.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["OperationalEndpoints:Address"] = "127.0.0.1",
                    ["OperationalEndpoints:Port"] = "18088"
                }));
            builder.ConfigureTestServices(services =>
                services.PostConfigure<HttpsRedirectionOptions>(options => options.HttpsPort = 9443));
        });

        var result = await factory.Server.SendAsync(context =>
        {
            context.Request.Method = HttpMethods.Get;
            context.Request.Scheme = "http";
            context.Request.Host = new HostString("localhost", privateListener ? 18088 : 18089);
            context.Request.Path = path;
            context.Connection.LocalIpAddress = IPAddress.Loopback;
            context.Connection.LocalPort = privateListener ? 18088 : 18089;
        });

        if (path == "/about")
        {
            await Assert.That(result.Response.StatusCode is
                StatusCodes.Status307TemporaryRedirect or StatusCodes.Status308PermanentRedirect).IsTrue();
            await Assert.That(result.Response.Headers.Location.ToString())
                .IsEqualTo("https://localhost:9443/about");
            return;
        }

        await Assert.That(result.Response.Headers.Location.Count).IsEqualTo(0);
        if (!privateListener)
            await Assert.That(result.Response.StatusCode).IsEqualTo(StatusCodes.Status404NotFound);
        else if (path == "/metrics")
            await Assert.That(result.Response.StatusCode).IsEqualTo(StatusCodes.Status200OK);
        else
            await Assert.That(result.Response.StatusCode is
                StatusCodes.Status200OK or StatusCodes.Status503ServiceUnavailable).IsTrue();
        await Assert.That(result.Response.GetTypedHeaders().CacheControl?.NoStore).IsTrue();
    }
}
