// ABOUTME: Rejects API requests that send conflicting direct-auth credentials.
// ABOUTME: Keeps auth dispatch deterministic and fail-closed before authentication handlers run.

using Explore.API.Authentication;
using Explore.API.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Explore.API.Middleware;

public sealed class ApiAuthenticationConflictMiddleware
{
    private readonly RequestDelegate _next;

    public ApiAuthenticationConflictMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IProblemDetailsService problemDetailsService,
        IOptions<McpAdapterSettings> mcpAdapterOptions)
    {
        if (!IsApiOrMcpPath(context, mcpAdapterOptions.Value))
        {
            await _next(context);
            return;
        }

        if (AtprotoTransientAuthenticationDefaults.IsPrivatePath(context.Request.Path))
        {
            if (!AtprotoTransientRequestBoundary.HasOnlyTransientCredential(context.Request))
            {
                await AtprotoTransientRequestBoundary.WriteProblemAsync(context, StatusCodes.Status401Unauthorized);
                return;
            }
            await _next(context);
            return;
        }

        var hasAuthorizationHeader = context.Request.Headers.ContainsKey("Authorization");
        var hasApiKeyHeader = ApiKeyHeaderReader.HasNonEmptyApiKey(context.Request);
        var hasManagedControlPlaneHeader = ApiKeyHeaderReader.HasNonEmptyApiKey(
            context.Request,
            ManagedControlPlaneAuthenticationDefaults.HeaderName);

        if ((hasAuthorizationHeader ? 1 : 0)
            + (hasApiKeyHeader ? 1 : 0)
            + (hasManagedControlPlaneHeader ? 1 : 0) > 1)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;

            await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = context,
                ProblemDetails = new ProblemDetails
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "Conflicting authentication credentials",
                    Type = "https://tools.ietf.org/html/rfc9110#section-15.5.1",
                    Detail = "Send exactly one supported authentication credential header.",
                    Instance = context.Request.Path
                }
            });
            return;
        }

        await _next(context);
    }

    private static bool IsApiOrMcpPath(HttpContext context, McpAdapterSettings mcpAdapterSettings)
    {
        if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return mcpAdapterSettings.Enabled &&
               !string.IsNullOrWhiteSpace(mcpAdapterSettings.EndpointPath) &&
               context.Request.Path.StartsWithSegments(
                   mcpAdapterSettings.EndpointPath,
                   StringComparison.OrdinalIgnoreCase);
    }
}
