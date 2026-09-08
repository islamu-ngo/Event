using System.Diagnostics;
using Explore.API.Hateoas;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Services;
using Microsoft.Net.Http.Headers;

namespace Explore.API.Middleware;

/// <summary>
/// Logs structured metadata for every HTTP request.
/// Records method, path, status code, duration, user ID, and tenant ID.
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
            if (SafeRouteMetadata.TryGetSensitiveRouteIdentity(context, out string routeIdentity))
            {
                _logger.LogInformation(
                    "HTTP {Method} {Route} responded {StatusCode} in {ElapsedMs:0.00}ms | CorrelationId={CorrelationId} RequestPath={RequestPath}",
                    context.Request.Method,
                    routeIdentity,
                    context.Response.StatusCode,
                    elapsed.TotalMilliseconds,
                    correlationId ?? "-",
                    routeIdentity);
            }
            else
            {
                var platformIdentityPresent = context.User.GetPlatformUserId().HasValue;
                var tenantPresent = tenantContextAccessor.TenantId.HasValue;
                var tenantSlugPresent = context.Request.Headers.ContainsKey(
                    Explore.Application.Constants.TenantHeaderNames.TenantSlug);
                var authHeaderPresent = context.Request.Headers.ContainsKey(HeaderNames.Authorization);
                var isAuthenticated = context.User?.Identity?.IsAuthenticated ?? false;

                _logger.LogInformation(
                    "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMs:0.00}ms | PlatformIdentityPresent={PlatformIdentityPresent} Authenticated={IsAuthenticated} AuthHeaderPresent={AuthHeaderPresent} TenantPresent={TenantPresent} TenantSlugPresent={TenantSlugPresent} CorrelationId={CorrelationId}",
                    context.Request.Method,
                    context.Request.Path.Value,
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
}

internal static class SafeRouteMetadata
{
    internal const string UnresolvedRouteClassification = "route-unresolved";

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

        string pattern = endpoint.RoutePattern.RawText ?? string.Empty;
        if (!pattern.Contains("/admission/", StringComparison.OrdinalIgnoreCase))
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
