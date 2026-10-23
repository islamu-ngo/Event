using System.Diagnostics;
using Explore.API.Hateoas;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Services;
using Microsoft.Net.Http.Headers;

namespace Explore.API.Middleware;

/// <summary>
/// Logs structured metadata for every HTTP request.
/// Records bounded method, native route identity, status, duration and correlation.
/// Sensitive data (authorization headers, request/response bodies) is never logged.
/// </summary>
public sealed class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, ITenantContextAccessor tenantContextAccessor)
    {
        var startTimestamp = Stopwatch.GetTimestamp();

        try
        {
            await _next(context);
        }
        finally
        {
            var elapsed = Stopwatch.GetElapsedTime(startTimestamp);

            var correlationId = context.Items["CorrelationId"] as string;
            string safeRoute = SafeRouteMetadata.GetRouteIdentityOrClassification(context);
            RedactSensitiveRouteActivity(Activity.Current, safeRoute, SafeRouteMetadata.GetMethod(context));
            if (SafeRouteMetadata.TryGetSensitiveRouteIdentity(context, out string routeIdentity))
            {
                _logger.LogInformation(
                    "HTTP {Method} {Route} responded {StatusCode} in {ElapsedMs:0.00}ms | CorrelationId={CorrelationId}",
                    SafeRouteMetadata.GetMethod(context),
                    routeIdentity,
                    context.Response.StatusCode,
                    elapsed.TotalMilliseconds,
                    correlationId ?? "-");
            }
            else
            {
                string safePath = SafeRouteMetadata.GetRouteIdentityOrClassification(context);
                var platformIdentityPresent = context.User.GetPlatformUserId().HasValue;
                var tenantPresent = tenantContextAccessor.TenantId.HasValue;
                var tenantSlugPresent = context.Request.Headers.ContainsKey(
                    Explore.Application.Constants.TenantHeaderNames.TenantSlug);
                var authHeaderPresent = context.Request.Headers.ContainsKey(HeaderNames.Authorization);
                var isAuthenticated = context.User?.Identity?.IsAuthenticated ?? false;

                _logger.LogInformation(
                    "HTTP {Method} {Route} responded {StatusCode} in {ElapsedMs:0.00}ms | PlatformIdentityPresent={PlatformIdentityPresent} Authenticated={IsAuthenticated} AuthHeaderPresent={AuthHeaderPresent} TenantPresent={TenantPresent} TenantSlugPresent={TenantSlugPresent} CorrelationId={CorrelationId}",
                    SafeRouteMetadata.GetMethod(context),
                    safePath,
                    context.Response.StatusCode,
                    elapsed.TotalMilliseconds,
                    platformIdentityPresent,
                    isAuthenticated,
                    authHeaderPresent,
                    tenantPresent,
                    tenantSlugPresent,
                    correlationId ?? "-");
            }
        }
    }

    internal static bool TryGetAdmissionRouteIdentity(HttpContext context, out string routeIdentity) =>
        SafeRouteMetadata.TryGetSensitiveRouteIdentity(context, out routeIdentity);

    private static void RedactSensitiveRouteActivity(Activity? activity, string routeIdentity, string method)
    {
        if (activity is null) return;
        activity.DisplayName = $"{method} {routeIdentity}";
        activity.SetTag("url.path", routeIdentity);
        activity.SetTag("url.query", null);
        activity.SetTag("url.full", null);
        activity.SetTag("http.url", null);
        activity.SetTag("http.target", routeIdentity);
    }
}

internal static class SafeRouteMetadata
{
    internal const string UnresolvedRouteClassification = "route-unresolved";

    internal static string GetMethod(HttpContext context) => context.Request.Method switch
    {
        "GET" or "POST" or "PUT" or "PATCH" or "DELETE" or "HEAD" or "OPTIONS" or "TRACE" or "CONNECT"
            => context.Request.Method,
        _ => "OTHER"
    };

    internal static string GetRouteIdentityOrClassification(HttpContext context)
    {
        Endpoint? selectedEndpoint = context.GetEndpoint();
        string? endpointName = selectedEndpoint?.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName;
        if (!string.IsNullOrWhiteSpace(endpointName))
        {
            return endpointName;
        }

        if (selectedEndpoint is RouteEndpoint endpoint &&
            !string.IsNullOrWhiteSpace(endpoint.RoutePattern.RawText))
        {
            return "/" + endpoint.RoutePattern.RawText.Trim('/').ToLowerInvariant();
        }

        return UnresolvedRouteClassification;
    }

    internal static bool TryGetSensitiveRouteIdentity(HttpContext context, out string routeIdentity)
    {
        routeIdentity = string.Empty;
        Endpoint? selectedEndpoint = context.GetEndpoint();
        if (selectedEndpoint?.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName
            == RouteNames.GetActorByDid)
        {
            routeIdentity = RouteNames.GetActorByDid;
            return true;
        }

        if (selectedEndpoint is not RouteEndpoint endpoint)
            return false;

        string pattern = (endpoint.RoutePattern.RawText ?? string.Empty).Trim('/');
        bool resource = pattern.StartsWith("api/eventresource/", StringComparison.OrdinalIgnoreCase)
            || pattern.StartsWith("api/event/", StringComparison.OrdinalIgnoreCase)
                && pattern.Contains("/resources", StringComparison.OrdinalIgnoreCase);
        bool storage = pattern.StartsWith("api/storageobject/", StringComparison.OrdinalIgnoreCase);
        if (!resource && !storage && !pattern.Contains("/admission/", StringComparison.OrdinalIgnoreCase))
            return false;

        routeIdentity = "/" + pattern.Trim('/').ToLowerInvariant();
        return true;
    }
}
public static class RequestLoggingMiddlewareExtensions

{
    public static IApplicationBuilder UseRequestLogging(this IApplicationBuilder app)
    {
        return app.UseMiddleware<RequestLoggingMiddleware>();
    }
}
