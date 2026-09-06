// ABOUTME: Provides shared Aspire service defaults, health endpoints, and OpenTelemetry setup.
// ABOUTME: Registers safe platform meters and activity sources used by API, workers, and infrastructure adapters.

using Explore.ServiceDefaults.Compliance;
using Explore.ServiceDefaults;
using Explore.ServiceDefaults.HealthChecks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Compliance.Redaction;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ServiceDiscovery;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

// Adds common .NET Aspire services: service discovery, resilience, health checks, and OpenTelemetry.
// This project should be referenced by each service project in your solution.
// To learn more about using this project, see https://aka.ms/dotnet/aspire/service-defaults
public static class Extensions
{
    private const string HealthEndpointPath = "/health";
    private const string AlivenessEndpointPath = "/alive";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.TryAddSingleton<HostProcessSignalSubscriptions>();
        builder.ConfigureRedaction();

        builder.ConfigureOpenTelemetry();

        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();

        // NOTE: We intentionally do NOT register global resilience/service-discovery handlers via
        // ConfigureHttpClientDefaults here. Doing so causes Microsoft.Extensions.Http.Resilience
        // to STACK a second ResilienceHandler on top of any per-client AddStandardResilienceHandler
        // call, resulting in double timeouts and spurious BrokenCircuitException cascades.
        //
        // Evidence: dotnet/extensions issues #4814, #5021, #5695. Official guidance is to scope
        // resilience per HttpClient (see docs/ARCHITECTURE.md and Explore.Blazor/Extensions/HttpClientExtensions.cs).
        //
        // If a consumer wants opt-in global resilience, add it in that project explicitly.

        // Uncomment the following to restrict the allowed schemes for service discovery.
        // builder.Services.Configure<ServiceDiscoveryOptions>(options =>
        // {
        //     options.AllowedSchemes = ["https"];
        // });

        return builder;
    }

    public static TBuilder ConfigureRedaction<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddRedaction(redaction =>
        {
            redaction.SetRedactor<StarRedactor>(DataTaxonomy.PiiData);
            redaction.SetRedactor<StarRedactor>(DataTaxonomy.SensitiveData);
            redaction.SetFallbackRedactor<StarRedactor>();
        });

        return builder;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddMeter("Explore.Business")
                    .AddMeter("Explore.Mcp")
                    .AddMeter("Explore.Translation")
                    .AddMeter("Explore.Projections")
                    .AddMeter("Explore.EventLocationPrivacy")
                    .AddMeter("Explore.AdmissionCheckIn")
                    .AddPrometheusExporter();
            })
            .WithTracing(tracing =>
            {
                tracing.AddSource(builder.Environment.ApplicationName)
                    .AddSource("Explore.Authorization")
                    .AddSource("Explore.Ai.Provider")
                    .AddSource("Explore.Hateoas.Authorization")
                    .AddSource("Explore.Mcp")
                    .AddSource("Quartz")
                    .AddAspNetCoreInstrumentation(tracing =>
                        // Exclude health check requests from tracing
                        tracing.Filter = context =>
                            !context.Request.Path.StartsWithSegments(HealthEndpointPath)
                            && !context.Request.Path.StartsWithSegments(AlivenessEndpointPath)
                    )
                    // Uncomment the following line to enable gRPC instrumentation (requires the OpenTelemetry.Instrumentation.GrpcNetClient package)
                    //.AddGrpcClientInstrumentation()
                    .AddHttpClientInstrumentation();
            });

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (useOtlpExporter)
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        // Uncomment the following lines to enable the Azure Monitor exporter (requires the Azure.Monitor.OpenTelemetry.AspNetCore package)
        //if (!string.IsNullOrEmpty(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
        //{
        //    builder.Services.AddOpenTelemetry()
        //       .UseAzureMonitor();
        //}

        return builder;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            // Add a default liveness check to ensure app is responsive
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    public static TBuilder AddOidcDiscoveryReadinessCheck<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHttpClient(OidcDiscoveryHealthCheck.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(5);
        });

        builder.Services.AddHealthChecks()
            .AddCheck<OidcDiscoveryHealthCheck>(
                "oidc-discovery",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["ready", "oidc", "infrastructure"]);

        return builder;
    }

    public static TBuilder AddDistributedCacheReadinessCheck<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            .AddCheck<DistributedCacheHealthCheck>(
                "distributed-cache",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["ready", "cache", "infrastructure"]);

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        // Health check endpoints for container orchestration (Coolify, K8s, etc.)
        // Returns: 200 OK when healthy, 503 Service Unavailable when unhealthy
        // Required for zero-downtime rolling deployments

        var healthCheckOptions = new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains("ready"),
            ResponseWriter = HealthCheckResponseWriter.WriteAsync,
            ResultStatusCodes =
            {
                [HealthStatus.Healthy] = StatusCodes.Status200OK,
                [HealthStatus.Degraded] = StatusCodes.Status200OK,
                [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
            }
        };

        // Only readiness checks must pass for app to be considered ready to accept traffic.
        app.MapHealthChecks(HealthEndpointPath, healthCheckOptions);

        // Only health checks tagged with the "live" tag must pass for app to be considered alive
        app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains("live"),
            ResponseWriter = HealthCheckResponseWriter.WriteAsync,
            ResultStatusCodes =
            {
                [HealthStatus.Healthy] = StatusCodes.Status200OK,
                [HealthStatus.Degraded] = StatusCodes.Status200OK,
                [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
            }
        });

        app.MapPrometheusScrapingEndpoint("/metrics");

        return app;
    }

}
