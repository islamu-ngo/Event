// ABOUTME: Exercises the shared browser YARP surface against a real upstream listener.
// ABOUTME: Proves private transient routes are denied outright and privileged assertions are stripped elsewhere.

using System.Net;
using System.Security.Cryptography;
using Event.Web.BffHosting.Proxy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Blazor.IntegrationTests.Endpoints;

public sealed class AtprotoTransientProxyTests
{
    [Test]
    public async Task PrivateRoutesCannotReachUpstream_AndOtherRoutesStripAssertion()
    {
        int reached = 0;
        bool assertionReached = false;
        var upstreamBuilder = WebApplication.CreateBuilder();
        upstreamBuilder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var upstream = upstreamBuilder.Build();
        upstream.Run(context =>
        {
            Interlocked.Increment(ref reached);
            assertionReached = context.Request.Headers.ContainsKey("X-Atproto-Transient-Assertion");
            context.Response.StatusCode = StatusCodes.Status204NoContent;
            return Task.CompletedTask;
        });
        await upstream.StartAsync();
        string address = upstream.Services.GetRequiredService<IServer>().Features
            .Get<IServerAddressesFeature>()!.Addresses.Single();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ExploreApi:BaseUrl"] = address });
        builder.Services.AddAuthentication("Cookies").AddCookie("Cookies");
        builder.Services.AddEventApiProxy(builder.Configuration, builder.Environment);
        await using var proxy = builder.Build();
        proxy.MapReverseProxy();
        await proxy.StartAsync();
        using var client = proxy.GetTestClient();
        string assertion = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        foreach (string operation in new[] { "create", "read", "consume", "probe" })
        foreach (string path in new[] { $"/api/auth/atproto/transient/{operation}", $"/API/AUTH/ATPROTO/TRANSIENT/{operation}/" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path);
            request.Headers.Add("X-Atproto-Transient-Assertion", assertion);
            using var response = await client.SendAsync(request);
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
            await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
            await Assert.That(reached).IsEqualTo(0);
        }
        using var ordinary = new HttpRequestMessage(HttpMethod.Get, "/api/auth/atproto/transient/read-neighbor");
        ordinary.Headers.Add("X-Atproto-Transient-Assertion", assertion);
        using var forwarded = await client.SendAsync(ordinary);
        await Assert.That(forwarded.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(reached).IsEqualTo(1);
        await Assert.That(assertionReached).IsFalse();
    }
}
