namespace Explore.API.Middleware;

/// <summary>
/// Adds security headers to all HTTP responses.
/// These headers protect against MIME sniffing, clickjacking, and information leakage.
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;

            // Prevent MIME type sniffing
            headers["X-Content-Type-Options"] = "nosniff";

            // Prevent clickjacking
            headers["X-Frame-Options"] = "DENY";

            // Control referrer information sent with requests
            if (!headers.ContainsKey("Referrer-Policy"))
            {
                headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            }

            // Restrict browser features the API does not use
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";

            // CSP for a REST API: deny all resource loading (API serves JSON, not HTML)
            headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";

            // Prevent caching of sensitive responses (write operations)
            if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
            {
                if (!headers.CacheControl.ToString().Contains("no-store", StringComparison.OrdinalIgnoreCase))
                {
                    headers.CacheControl = "no-store";
                }

                headers["Pragma"] = "no-cache";
            }

            return Task.CompletedTask;
        });

        await _next(context);
    }
}

public static class SecurityHeadersMiddlewareExtensions
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
    {
        return app.UseMiddleware<SecurityHeadersMiddleware>();
    }
}
