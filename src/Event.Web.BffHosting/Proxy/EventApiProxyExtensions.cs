// ABOUTME: Registers the shared YARP API proxy and server-owned BFF request transforms.
// ABOUTME: Strips browser-controlled privileged headers before adding trusted token, tenant, setup, and support context.

using Event.Web.BffHosting.Abstractions;
using Event.Web.BffHosting.Authentication;
using Event.Web.BffHosting.Security;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;
using Yarp.ReverseProxy.Transforms;

namespace Event.Web.BffHosting.Proxy;

public static class EventApiProxyExtensions
{
    public static IApplicationBuilder UseEventApiProxyAntiforgery(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.Use(ValidateApiProxyAntiforgeryAsync);
    }

    public static IServiceCollection AddEventApiProxy(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        services.TryAddScoped<IEventBffAccessTokenProvider, AuthenticationTicketEventBffAccessTokenProvider>();
        services.TryAddScoped<IEventBffTenantHintProvider, NoopEventBffTenantHintProvider>();
        services.TryAddScoped<IEventBffSetupSecretProvider, NoopEventBffSetupSecretProvider>();
        services.TryAddScoped<IEventBffSupportAccessProvider, NoopEventBffSupportAccessProvider>();
        services.TryAddScoped<EventBffRequestEnricher>();

        var apiBaseUrl = EventApiBaseAddressResolver.Resolve(configuration);

        var routes = new[]
        {
            new RouteConfig
            {
                RouteId = "event-api-vapid-public-key",
                ClusterId = "event-api",
                Match = new RouteMatch { Path = "/vapid-public-key" }
            },
            new RouteConfig
            {
                RouteId = "event-api",
                ClusterId = "event-api",
                Match = new RouteMatch { Path = "/api/{**catchall}" }
            }
        };

        var clusters = new[]
        {
            new ClusterConfig
            {
                ClusterId = "event-api",
                Destinations = new Dictionary<string, DestinationConfig>
                {
                    ["primary"] = new() { Address = apiBaseUrl }
                },
                HttpClient = new HttpClientConfig
                {
                    DangerousAcceptAnyServerCertificate =
                        BffDevelopmentHostPolicy.IsDevelopmentTrustedBaseAddress(apiBaseUrl, environment)
                },
                HttpRequest = new ForwarderRequestConfig
                {
                    ActivityTimeout = TimeSpan.FromSeconds(65)
                }
            }
        };

        services.AddReverseProxy()
            .LoadFromMemory(routes, clusters)
            .AddTransforms(context =>
            {
                context.AddRequestTransform(async transformContext =>
                {
                    // Deny the exact private capability even when a browser presents a valid assertion.
                    // Header stripping alone cannot establish a server-only transport boundary.
                    var path = transformContext.HttpContext.Request.Path.Value?.TrimEnd('/');
                    if (new[] { "create", "read", "consume", "probe" }.Any(operation => string.Equals(
                            path, "/api/auth/atproto/transient/" + operation, StringComparison.OrdinalIgnoreCase)))
                    {
                        transformContext.HttpContext.Response.StatusCode = StatusCodes.Status404NotFound;
                        transformContext.HttpContext.Response.Headers.CacheControl = "no-store";
                        return;
                    }

                    var enricher = transformContext.HttpContext.RequestServices
                        .GetRequiredService<EventBffRequestEnricher>();
                    var enrichment = await enricher.ResolveForProxyAsync(
                        transformContext.HttpContext,
                        transformContext.HttpContext.RequestAborted);
                    enrichment.ApplyTo(transformContext.ProxyRequest);
                });
            });

        return services;
    }

    private static async Task ValidateApiProxyAntiforgeryAsync(HttpContext context, Func<Task> next)
    {
        if (!EventBffRequestPolicy.RequiresAntiforgeryValidation(context.Request))
        {
            await next();
            return;
        }

        var antiforgery = context.RequestServices.GetRequiredService<IAntiforgery>();
        try
        {
            await antiforgery.ValidateRequestAsync(context);
        }
        catch (AntiforgeryValidationException)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("Antiforgery validation failed", context.RequestAborted);
            return;
        }

        await next();
    }

}
