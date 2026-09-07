using Microsoft.AspNetCore.Http;
using System.Net;
using Explore.Blazor.IntegrationTests.Fixtures;
using Explore.ServiceDefaults.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Explore.Blazor.IntegrationTests.Middleware;

public sealed class ForwardedHeadersSecurityTests
{
    [Test]
    public async Task DirectClientForwardedForDoesNotChangeRemoteIpAddress()
    {
        var context = new DefaultHttpContext();
        IPAddress directAddress = IPAddress.Parse("203.0.113.10");
        context.Connection.RemoteIpAddress = directAddress;
        context.Request.Headers["X-Forwarded-For"] = "198.51.100.99";
        var options = new ForwardedHeadersOptions();
        new ForwardedHeadersTrustOptions { TrustLoopbackProxy = true }.ApplyTo(
            options,
            ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);
        var middleware = new ForwardedHeadersMiddleware(
            _ => Task.CompletedTask,
            NullLoggerFactory.Instance,
            Options.Create(options));

        await middleware.Invoke(context);

        await Assert.That(context.Connection.RemoteIpAddress).IsEqualTo(directAddress);
    }

    [Test]
    public async Task TrustedProxyAppliesClientIpAndProtoButIgnoresForwardedHost()
    {
        var context = new DefaultHttpContext();
        IPAddress proxyAddress = IPAddress.Parse("10.20.30.40");
        context.Connection.RemoteIpAddress = proxyAddress;
        context.Request.Host = new HostString("event.example");
        context.Request.Headers["X-Forwarded-For"] = "198.51.100.99";
        context.Request.Headers["X-Forwarded-Proto"] = "https";
        context.Request.Headers["X-Forwarded-Host"] = "attacker.example";
        var options = new ForwardedHeadersOptions();
        new ForwardedHeadersTrustOptions { KnownProxies = [proxyAddress.ToString()] }.ApplyTo(
            options,
            ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);
        var middleware = new ForwardedHeadersMiddleware(
            _ => Task.CompletedTask,
            NullLoggerFactory.Instance,
            Options.Create(options));

        await middleware.Invoke(context);

        await Assert.That(context.Connection.RemoteIpAddress).IsEqualTo(IPAddress.Parse("198.51.100.99"));
        await Assert.That(context.Request.Scheme).IsEqualTo("https");
        await Assert.That(context.Request.Host).IsEqualTo(new HostString("event.example"));
    }

    [Test]
    [Arguments("ForwardedHeadersTrust:KnownProxies:0", "not-an-ip")]
    [Arguments("ForwardedHeadersTrust:KnownNetworks:0", "0.0.0.0/0")]
    [Arguments("ForwardedHeadersTrust:ForwardLimit", "11")]
    public async Task InvalidTrustedProxyConfigurationFailsStartup(string key, string value)
    {
        Func<Task> start = async () =>
        {
            await using WebApplicationFactory<Program> factory = new BlazorBffWebApplicationFactory()
                .WithWebHostBuilder(builder =>
                    builder.UseSetting(key, value));
            using HttpClient client = factory.CreateClient();
            using HttpResponseMessage response = await client.GetAsync("/auth/status");
        };

        await Assert.That(start).Throws<OptionsValidationException>();
    }
}
