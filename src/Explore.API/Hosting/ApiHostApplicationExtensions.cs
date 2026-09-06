// ABOUTME: Applies the reusable ordered API middleware pipeline and maps API-owned endpoints.
// ABOUTME: Preserves OpenAPI, MCP, controllers, health routes, caching, tenancy, and authorization order.

using Explore.API.Authentication;
using Explore.API.Configuration;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Mcp;
using Explore.API.Middleware;
using Explore.ServiceDefaults.HealthChecks;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore;
using Scalar.AspNetCore;
using static Microsoft.AspNetCore.Http.StatusCodes;

namespace Explore.API.Hosting;

public static class ApiHostApplicationExtensions
{
    public static WebApplication UseApiHostMiddleware(
        this WebApplication app,
        ApiHostCompositionState state,
        Func<HttpContext, bool>? predicate = null,
        bool includeForwardedHeaders = true)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(state);

        if (predicate is not null)
        {
            app.UseWhen(predicate, branch => UseApiHostMiddlewareBeforeScheduler(
                branch,
                app,
                state,
                includeForwardedHeaders));
            UseApiHostScheduler(app, state);
            app.UseWhen(predicate, UseApiHostMiddlewareAfterScheduler);
            return app;
        }

        UseApiHostMiddlewareBeforeScheduler(app, app, state, includeForwardedHeaders);
        UseApiHostScheduler(app, state);
        UseApiHostMiddlewareAfterScheduler(app);
        return app;
    }

    private static void UseApiHostMiddlewareBeforeScheduler(
        IApplicationBuilder pipeline,
        WebApplication app,
        ApiHostCompositionState state,
        bool includeForwardedHeaders)
    {
        if (app.Environment.IsDevelopment() ||
            app.Environment.IsEnvironment("Testing") ||
            state.IsOpenApiGeneration)
        {
            pipeline.UseSwagger();
            pipeline.UseSwaggerUI(options =>
                options.SwaggerEndpoint("/swagger/v0.1/swagger.json", "Explore API v0.1"));
        }

        if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
        {
            Microsoft.IdentityModel.Logging.IdentityModelEventSource.ShowPII = true;
            pipeline.UseCors("DevPolicy");
        }
        else
        {
            pipeline.UseCors("InternalAppPolicy");
            pipeline.UseHsts();
        }

        pipeline.UseApiExceptionHandling();
        if (includeForwardedHeaders)
        {
            pipeline.UseForwardedHeaders();
        }
        pipeline.UseSecurityHeaders();
        pipeline.UseCorrelationId();
        pipeline.UseRequestLogging();
        pipeline.UseResponseCompression();
        if (state.HttpsRedirectionEnabled)
        {
            pipeline.UseHttpsRedirection();
        }

        pipeline.UseHateoas();
        pipeline.UseRouting();
        pipeline.UseWhen(context => AtprotoTransientAuthenticationDefaults.IsPrivatePath(context.Request.Path), branch =>
        {
            branch.Use(AtprotoTransientRequestBoundary.GuardAsync);
            branch.UseRequestTimeouts();
            branch.UseRateLimiter();
            branch.UseMiddleware<AtprotoTransientRequestBoundary>();
        });
        pipeline.UseMiddleware<ApiTenantResolutionMiddleware>();
        pipeline.UseWhen(context => !AtprotoTransientAuthenticationDefaults.IsPrivatePath(context.Request.Path),
            branch => branch.UseRequestTimeouts());
        pipeline.UseMiddleware<ApiAuthenticationConflictMiddleware>();
        pipeline.UseAuthentication();
        pipeline.UseMiddleware<ApiTenantPostAuthenticationMiddleware>();
        pipeline.UseMiddleware<McpRuntimeGateMiddleware>();
        pipeline.UseRequestLocalization();
        pipeline.UseMiddleware<PrivateNoStoreMiddleware>();
        pipeline.UseWhen(context => !AtprotoTransientAuthenticationDefaults.IsPrivatePath(context.Request.Path),
            branch => branch.UseRateLimiter());
        pipeline.UseAuthorization();
        pipeline.UseMiddleware<IdempotencyMiddleware>();
        pipeline.UseMiddleware<SupportAccessAuditMiddleware>();
    }

    private static void UseApiHostScheduler(WebApplication app, ApiHostCompositionState state)
    {
        if (!state.IsOpenApiGeneration &&
            state.UseQuartzScheduler &&
            QuartzSchedulerExtensions.IsQuartzSchedulerEnabled(app.Configuration, app.Environment))
        {
            app.UseApiQuartzScheduler();
        }
    }

    private static void UseApiHostMiddlewareAfterScheduler(IApplicationBuilder pipeline)
    {
        pipeline.UseOutputCache();
        pipeline.UseETag();
    }

    public static WebApplication MapApiHostEndpoints(
        this WebApplication app,
        ApiHostCompositionState state)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(state);

        if (app.Environment.IsDevelopment() ||
            app.Environment.IsEnvironment("Testing") ||
            state.IsOpenApiGeneration)
        {
            app.MapOpenApi().DisableRequestTimeout();
            app.MapScalarApiReference();
        }

        app.MapControllers();
        if (!state.IsOpenApiGeneration &&
            state.UseQuartzScheduler &&
            QuartzSchedulerExtensions.IsQuartzSchedulerEnabled(app.Configuration, app.Environment))
        {
            app.MapApiQuartzSchedulerEndpoints();
        }

        var mcpAdapterSettings = app.Services.GetRequiredService<IOptions<McpAdapterSettings>>().Value;
        if (mcpAdapterSettings.Enabled && !state.IsOpenApiGeneration)
        {
            app.MapMcp(mcpAdapterSettings.EndpointPath).AllowAnonymous();
        }

        app.MapDefaultEndpoints();
        app.MapHealthChecks(
            "/health/webhooks/local",
            CreateWebhookReadinessOptions("webhook-local-readiness"));
        app.MapHealthChecks(
            "/health/webhooks/svix",
            CreateWebhookReadinessOptions("webhook-svix-readiness"));
        app.MapHealthChecks(
            "/health/webhooks/coop-effects",
            CreateWebhookReadinessOptions("webhook-coop-effect-readiness"));
        return app;
    }

    private static HealthCheckOptions CreateWebhookReadinessOptions(string tag) => new()
    {
        Predicate = registration => registration.Tags.Contains(tag),
        ResponseWriter = HealthCheckResponseWriter.WriteAsync,
        ResultStatusCodes =
        {
            [HealthStatus.Healthy] = Status200OK,
            [HealthStatus.Degraded] = Status200OK,
            [HealthStatus.Unhealthy] = Status503ServiceUnavailable
        }
    };
}
