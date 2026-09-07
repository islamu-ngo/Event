using Event.Standalone.Middleware;
using Explore.API.Configuration;
using Explore.API.Hosting;
using Explore.Blazor.Extensions;
using Explore.Blazor.Hosting;
using Explore.Blazor.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace Event.Standalone.Hosting;

public sealed class StandaloneHostMarker;

public static class StandaloneHostApplicationExtensions
{
    public static WebApplication UseStandaloneHostMiddleware(
        this WebApplication app,
        ApiHostCompositionState apiHost,
        BlazorHostProfile blazorProfile,
        GracefulShutdownState shutdownState)
    {
        var mcpPath = app.Services.GetRequiredService<IOptions<McpAdapterSettings>>().Value.EndpointPath;
        var schedulerPath = app.Configuration.GetValue<string>("Scheduler:Quartz:StatusEndpointPath")
            ?? "/admin/scheduler";

        var routeClassifier = new ApiHostRouteClassifier(
            app.Services.GetServices<EndpointDataSource>(),
            mcpPath,
            schedulerPath);

        app.UseForwardedHeaders();
        var schemeInitializer = app.Services.GetRequiredService<DynamicAuthSchemeInitializer>();
        app.Use(async (context, next) =>
        {
            if (!routeClassifier.IsApiOwned(context))
            {
                await schemeInitializer.InitializeAsync();
            }

            await next(context);
        });
        app.UseCombinedApiBridge();
        var dispatcher = app.Services.GetRequiredService<InProcessEventApiDispatcher>();
        app.Use(next =>
        {
            dispatcher.Bind(next);
            return next;
        });
        app.UseApiHostMiddleware(apiHost, routeClassifier.IsApiOwned, includeForwardedHeaders: false);
        app.Use(async (context, next) =>
        {
            if (routeClassifier.IsApiOwned(context) &&
                context.GetEndpoint() is RouteEndpoint endpoint &&
                !routeClassifier.IsApiOwned(endpoint))
            {
                context.SetEndpoint(null);
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await context.RequestServices.GetRequiredService<IProblemDetailsService>().TryWriteAsync(
                    new ProblemDetailsContext
                    {
                        HttpContext = context,
                        ProblemDetails = new ProblemDetails
                        {
                            Status = StatusCodes.Status404NotFound,
                            Title = "Not Found"
                        }
                    });
                return;
            }

            await next(context);
        });
        app.Use(async (context, next) =>
        {
            if (dispatcher.IsMarkedRequest(context))
            {
                if (context.GetEndpoint()?.RequestDelegate is { } endpoint)
                {
                    await endpoint(context);
                }

                return;
            }

            await next(context);
        });
        app.UseBlazorHostMiddleware(blazorProfile, shutdownState, context => !routeClassifier.IsApiOwned(context));
        return app;
    }

    public static WebApplication MapStandaloneHostEndpoints(
        this WebApplication app,
        ApiHostCompositionState apiHost,
        BlazorHostProfile blazorProfile)
    {
        app.MapApiHostEndpoints(apiHost);
        app.MapBlazorHostEndpoints(blazorProfile);

        // Mapped after the Blazor endpoints so the dashboard's concrete page routes are registered alongside
        // them; its paths are outside the API-owned set, so the combined host routes them through the Blazor
        // branch and its cookie authentication rather than the bearer API bridge.
        app.MapStandaloneSchedulerDashboard();
        return app;
    }

    public static WebApplication BindStandaloneInternalApiTransport(this WebApplication app)
    {
        var endpointDataSources = ((IEndpointRouteBuilder)app).DataSources.ToArray();
        var endpointSelector = new ApplicationBuilder(app.Services);
        endpointSelector.UseRouting();
        endpointSelector.Run(_ => Task.CompletedTask);
        endpointSelector.UseEndpoints(endpoints =>
        {
            foreach (var endpointDataSource in endpointDataSources)
            {
                endpoints.DataSources.Add(endpointDataSource);
            }
        });
        app.Services.GetRequiredService<InProcessEventApiDispatcher>()
            .BindEndpointSelector(endpointSelector.Build());
        return app;
    }
}
