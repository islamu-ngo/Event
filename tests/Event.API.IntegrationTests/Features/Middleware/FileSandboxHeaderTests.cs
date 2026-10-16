using System.Net;
using Explore.API.Attributes;
using Explore.API.Controllers;
using Explore.API.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Primitives;

namespace Event.Api.IntegrationTests.Features.Middleware;

public sealed class FileSandboxHeaderTests
{
    [Test]
    [Arguments(true, "absent")]
    [Arguments(true, "weak")]
    [Arguments(true, "conflicting")]
    [Arguments(true, "on-starting")]
    [Arguments(false, "absent")]
    [Arguments(false, "weak")]
    [Arguments(false, "conflicting")]
    [Arguments(false, "on-starting")]
    public async Task StartedResponse_UsesPolicySelectedByEndpointMetadata(
        bool sandboxed, string downstreamPolicy)
    {
        using var host = await CreateHost(downstreamPolicy).StartAsync();
        using var client = host.GetTestClient();

        using var response = await client.GetAsync(sandboxed ? "/file" : "/ordinary-api");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await response.Content.ReadAsStringAsync()).IsEqualTo("started-response");
        var policies = response.Headers.GetValues("Content-Security-Policy").ToArray();
        await Assert.That(policies.Length).IsEqualTo(1);
        await Assert.That(policies[0]).IsEqualTo(sandboxed
            ? "default-src 'none'; frame-ancestors 'none'; sandbox"
            : "default-src 'none'; frame-ancestors 'none'");
    }

    [Test]
    [Arguments(typeof(StorageObjectController), nameof(StorageObjectController.GetContent))]
    [Arguments(typeof(StorageObjectController), nameof(StorageObjectController.GetPublicImage))]
    [Arguments(typeof(EventResourceContentController), nameof(EventResourceContentController.Get))]
    public async Task DiscoveredFileAction_RetainsSandboxResponseMetadata(Type controller, string action)
    {
        using var host = await CreateHost("absent").StartAsync();
        var endpoints = host.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .Where(endpoint =>
            {
                var descriptor = endpoint.Metadata.GetMetadata<ControllerActionDescriptor>();
                return descriptor is not null
                    && descriptor.ControllerTypeInfo.AsType() == controller
                    && descriptor.ActionName == action;
            })
            .ToArray();

        await Assert.That(endpoints.Length).IsGreaterThan(0);
        foreach (var endpoint in endpoints)
        {
            await Assert.That(endpoint.Metadata.GetMetadata<SandboxedFileResponseAttribute>()).IsNotNull();
            var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>();
            await Assert.That(methods).IsNotNull();
            await Assert.That(methods!.HttpMethods.Contains(HttpMethods.Get)).IsTrue();
        }
    }

    private static IHostBuilder CreateHost(string downstreamPolicy) =>
        new HostBuilder().ConfigureWebHost(builder =>
        {
            builder.UseTestServer();
            builder.ConfigureServices(services =>
            {
                services.AddControllers()
                    .AddApplicationPart(typeof(StorageObjectController).Assembly)
                    .ConfigureApplicationPartManager(manager =>
                        manager.FeatureProviders.Add(new FileControllerFeatureProvider()));
            });
            builder.Configure(app =>
            {
                // Register before routing so only the late callback can see the selected metadata.
                app.UseSecurityHeaders();
                app.UseRouting();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapControllers();
                    endpoints.MapGet("/file", WriteStartedResponse)
                        .WithMetadata(new SandboxedFileResponseAttribute());
                    endpoints.MapGet("/ordinary-api", WriteStartedResponse);
                });
            });

            async Task WriteStartedResponse(HttpContext context)
            {
                switch (downstreamPolicy)
                {
                    case "weak":
                        context.Response.Headers["Content-Security-Policy"] = "default-src *";
                        break;
                    case "conflicting":
                        context.Response.Headers["Content-Security-Policy"] = new StringValues(
                            ["default-src *; frame-ancestors *", "sandbox allow-scripts allow-same-origin"]);
                        break;
                    case "on-starting":
                        context.Response.OnStarting(() =>
                        {
                            context.Response.Headers["Content-Security-Policy"] =
                                "default-src *; frame-ancestors *; sandbox allow-scripts";
                            return Task.CompletedTask;
                        });
                        break;
                }

                context.Response.ContentType = context.Request.Path == "/file"
                    ? "text/html"
                    : "application/json";
                await context.Response.StartAsync(context.RequestAborted);
                if (!context.Response.HasStarted)
                {
                    throw new InvalidOperationException("The response must start before the endpoint returns.");
                }

                await context.Response.WriteAsync("started-response", context.RequestAborted);
            }
        });

    private sealed class FileControllerFeatureProvider : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            for (var index = feature.Controllers.Count - 1; index >= 0; index--)
            {
                var controller = feature.Controllers[index].AsType();
                if (controller != typeof(StorageObjectController)
                    && controller != typeof(EventResourceContentController))
                {
                    feature.Controllers.RemoveAt(index);
                }
            }
        }
    }
}
