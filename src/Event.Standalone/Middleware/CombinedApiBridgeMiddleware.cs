using System.Security.Claims;
using Event.Web.BffHosting.Authentication;
using Event.Web.BffHosting.Security;
using Explore.Application.Constants;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Event.Standalone.Middleware;

public sealed class CombinedApiBridgeMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        EventBffRequestEnricher enricher,
        IAntiforgery antiforgery)
    {
        if (!context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        if (EventBffRequestPolicy.RequiresAntiforgeryValidation(context.Request))
        {
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
        }

        var session = await context.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (!session.Succeeded)
        {
            if (context.Items.ContainsKey(EventBffAuthenticationConstants.TokenRefreshRejectedItemKey))
            {
                BffProxyHeaderSanitizer.RemoveBrowserControlledHeaders(context.Request);
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            await next(context);
            return;
        }

        var enrichment = await enricher.ResolveForSessionAsync(context, session, context.RequestAborted);
        if (enrichment.AccessToken is null
            && !EventBffRequestPolicy.IsAnonymousOnboardingPath(context.Request.Path))
        {
            BffProxyHeaderSanitizer.RemoveBrowserControlledHeaders(context.Request);
            context.User = new ClaimsPrincipal(new ClaimsIdentity());
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        enrichment.ApplyTo(context.Request);
        context.User = new ClaimsPrincipal(new ClaimsIdentity());
        await next(context);
    }
}

public static class CombinedApiBridgeExtensions
{
    private const string CombinedAuthenticationScheme = "Event.Standalone.Combined";

    public static IServiceCollection AddCombinedApiBridge(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<EventBffRequestEnricher>();
        services.AddAuthentication(options =>
            {
                options.DefaultScheme = CombinedAuthenticationScheme;
                options.DefaultAuthenticateScheme = CombinedAuthenticationScheme;
                options.DefaultChallengeScheme = CombinedAuthenticationScheme;
            })
            .AddPolicyScheme(CombinedAuthenticationScheme, CombinedAuthenticationScheme, options =>
            {
                options.ForwardDefaultSelector = context =>
                    context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
                        ? ApiAuthenticationSchemeNames.MultiAuth
                        : CookieAuthenticationDefaults.AuthenticationScheme;
            });
        return services;
    }

    public static IApplicationBuilder UseCombinedApiBridge(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<CombinedApiBridgeMiddleware>();
    }
}
