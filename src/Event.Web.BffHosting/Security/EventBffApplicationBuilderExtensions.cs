using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Net.Http.Headers;

namespace Event.Web.BffHosting.Security;

public static class EventBffApplicationBuilderExtensions
{
    private const string ContentSecurityPolicy =
        "default-src 'self'; " +
        "img-src 'self' data: https: blob:; " +
        "style-src 'self' 'unsafe-inline'; " +
        "script-src 'self' 'wasm-unsafe-eval'; " +
        "connect-src 'self' https: http: ws: wss:; " +
        "frame-ancestors 'none'; " +
        "base-uri 'self'; " +
        "object-src 'none'; " +
        "form-action 'self'";

    private const string PermissionsPolicy = "camera=(), microphone=(), geolocation=(), payment=()";

    public static WebApplication UseEventBffForwardedHeaders(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseForwardedHeaders();
        return app;
    }

    public static WebApplication UseEventBffSecurityHeaders(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers[HeaderNames.ContentSecurityPolicy] = ContentSecurityPolicy;
                headers[HeaderNames.XFrameOptions] = "DENY";
                headers[HeaderNames.XContentTypeOptions] = "nosniff";
                headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
                headers["Permissions-Policy"] = PermissionsPolicy;
                return Task.CompletedTask;
            });

            await next();
        });

        return app;
    }

    public static WebApplication UseEventBffAdminHostAccessControl(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.Use(async (context, next) =>
        {
            var accessPolicy = context.RequestServices.GetRequiredService<EventBffAdminHostAccessPolicy>();
            if (!accessPolicy.IsAllowed(context))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsync("Admin host access is not allowed from this network.");
                return;
            }

            await next();
        });

        return app;
    }

    public static WebApplication UseEventBffAntiforgeryToken(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var antiforgery = app.Services.GetRequiredService<IAntiforgery>();
        var secureCookie = !app.Environment.IsDevelopment();

        app.Use(async (context, next) =>
        {
            if (HttpMethods.IsGet(context.Request.Method))
            {
                var tokens = antiforgery.GetAndStoreTokens(context);
                if (!string.IsNullOrEmpty(tokens.RequestToken))
                {
                    context.Response.Cookies.Append("XSRF-TOKEN", tokens.RequestToken, new CookieOptions
                    {
                        HttpOnly = false,
                        Secure = secureCookie,
                        SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax,
                        Path = "/"
                    });
                }
            }

            await next();
        });

        return app;
    }
}
