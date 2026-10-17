using System.Text;
using Explore.API.Attributes;
using Explore.API.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;

namespace Event.Standalone.IntegrationTests;

public sealed class CombinedFileSecurityHeadersTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task TrustedEndpointMetadataComposesFinalPolicyInsteadOfExistingHeader(bool file)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        await using var app = builder.Build();
        app.UseRouting();
        app.UseSecurityHeaders();
        var endpoint = app.MapGet("/content", async (HttpContext context) =>
        {
            context.Response.Headers["Content-Security-Policy"] = "default-src *; script-src 'unsafe-inline'";
            context.Response.ContentType = "application/octet-stream";
            await context.Response.Body.WriteAsync(Encoding.UTF8.GetBytes("exact-file-bytes"), context.RequestAborted);
        });
        if (file) endpoint.WithMetadata(new SandboxedFileResponseAttribute());
        await app.StartAsync();
        using var client = app.GetTestClient();
        using var response = await client.GetAsync("/content");
        await Assert.That(await response.Content.ReadAsStringAsync()).IsEqualTo("exact-file-bytes");
        var directives = response.Headers.GetValues("Content-Security-Policy").Single().Split(';', StringSplitOptions.TrimEntries);
        await Assert.That(directives).Contains("default-src 'none'");
        await Assert.That(directives).Contains("frame-ancestors 'none'");
        await Assert.That(directives.Contains("sandbox")).IsEqualTo(file);
        await Assert.That(response.Headers.GetValues("X-Content-Type-Options").Single()).IsEqualTo("nosniff");
    }
}
